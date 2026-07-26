using GamePatchKit.Compression.NativeCompressions;
using GamePatchKit.Core;
using GamePatchKit.Core.Configuration;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Core.Paths;

namespace GamePatchKit.Packager;

public sealed class BundleCompactor
{
    private const string Stage = "compact";
    private const int ManifestSchemaVersion = 1;
    private const int StreamBufferSize = 64 * 1024;

    private readonly ICompressionCodec? _zstdCodec;
    private readonly FilePackageBuilder _packagePublisher;

    public BundleCompactor()
        : this(ZstdCompressionCodecFactory.Create())
    {
    }

    public BundleCompactor(ICompressionCodec? zstdCodec)
    {
        _zstdCodec = zstdCodec;
        _packagePublisher = new FilePackageBuilder(zstdCodec);
    }

    public async Task<BundleCompactResult> CompactAsync(
        BundleCompactRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        FilePackageBuilder.ValidateConfiguration(request.Config);
        ValidateRequestCollections(request);
        ValidateRequestedCodec(request);
        string outputRoot = Path.GetFullPath(request.OutputRoot);
        PackagePath.EnsureOutputRootIsNotLink(outputRoot, request.Config.PackageId);
        Directory.CreateDirectory(outputRoot);

        PreviousReleaseContext source = await PreviousReleaseReader.ReadAsync(
            request.SourceRelease,
            request.Config.PackageId,
            outputRoot,
            _zstdCodec,
            cancellationToken).ConfigureAwait(false);
        string[] targetGroups = ValidateAndSortTargetGroups(request, source.Manifest);
        string stagingRoot = Path.Combine(outputRoot, $".gpk-staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);

        try
        {
            CompactBuildState state = await BuildCandidateAsync(
                request,
                source,
                targetGroups,
                outputRoot,
                stagingRoot,
                cancellationToken).ConfigureAwait(false);

            GamePatchKitSchemaValidator.ValidateReleaseManifest(
                ReleaseIdentity.ComputeCanonicalBytes(state.Candidate),
                request.Config.PackageId);
            ValidationResult semanticValidation = ManifestValidator.Validate(state.Candidate);
            if (!semanticValidation.IsValid)
            {
                throw new PackageException(semanticValidation.Errors);
            }

            VerifyLogicalStateUnchanged(source.Manifest, state.Candidate);
            CompactDecision decision;

            try
            {
                decision = CompactVersionRule.Resolve(
                    source.Manifest,
                    state.Candidate,
                    request.RetainedObjects);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or OverflowException)
            {
                throw Failure(
                    PackageErrorCodes.ManifestInvalid,
                    exception.Message,
                    request.Config.PackageId);
            }

            GamePatchKitSchemaValidator.ValidateReleaseManifest(
                decision.Result.GetCanonicalBytes(),
                request.Config.PackageId);

            if (!decision.Changed)
            {
                return new BundleCompactResult(
                    changed: false,
                    decision.Result,
                    createdBundleArtifactCount: 0,
                    createdBundleArtifactBytes: 0,
                    createdFileArtifactCount: 0,
                    createdFileArtifactBytes: 0,
                    state.ReusedFileArtifactHashes.Count);
            }

            await FilePackageBuilder.PublishArtifactsAsync(
                decision.Result.Manifest,
                outputRoot,
                stagingRoot,
                request.Config.PackageId,
                cancellationToken).ConfigureAwait(false);
            await PackagePayloadVerifier.VerifyAsync(
                outputRoot,
                decision.Result.Manifest,
                _zstdCodec,
                cancellationToken).ConfigureAwait(false);
            await _packagePublisher.PublishManifestAsync(
                decision.Result,
                request.WriteCompressedManifest,
                outputRoot,
                stagingRoot,
                cancellationToken).ConfigureAwait(false);

            return new BundleCompactResult(
                changed: true,
                decision.Result,
                state.CreatedBundleHashes.Count,
                state.CreatedBundleBytes,
                state.CreatedFileArtifactHashes.Count,
                state.CreatedFileArtifactBytes,
                state.ReusedFileArtifactHashes.Count);
        }
        catch (PackageException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw Failure(
                PackageErrorCodes.ArtifactCorrupted,
                "A compact source artifact changed or could not be reconstructed.",
                request.Config.PackageId);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }
        }
    }

    private async Task<CompactBuildState> BuildCandidateAsync(
        BundleCompactRequest request,
        PreviousReleaseContext source,
        IReadOnlyList<string> targetGroups,
        string outputRoot,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        string restoredRoot = Path.Combine(stagingRoot, "restored");
        Directory.CreateDirectory(restoredRoot);
        var targetGroupSet = targetGroups.ToHashSet(StringComparer.Ordinal);

        await RestoreSelectedFilesAsync(
            outputRoot,
            source,
            targetGroupSet,
            restoredRoot,
            cancellationToken).ConfigureAwait(false);

        var restoredByGroup = new Dictionary<string, List<SourceFileSnapshot>>(StringComparer.Ordinal);

        foreach (ManifestFileEntry file in source.Manifest.Files)
        {
            if (!targetGroupSet.Contains(file.Group))
            {
                continue;
            }

            string restoredPath = PackagePath.Resolve(restoredRoot, file.Path);
            StableFileMetadata metadata = NativeFileSystem.ReadPathMetadata(restoredPath, request.Config.PackageId, file.Path);
            if (metadata.Kind != SourceEntryKind.File || metadata.Size != file.Size)
            {
                throw Failure(
                    PackageErrorCodes.ArtifactCorrupted,
                    "A restored compact source file has unexpected metadata.",
                    request.Config.PackageId,
                    file.Path,
                    file.Group);
            }

            var snapshot = new SourceFileSnapshot(file.Path, restoredPath, file.Group, metadata)
            {
                FileHash = file.FileHash,
            };

            if (!restoredByGroup.TryGetValue(file.Group, out List<SourceFileSnapshot>? groupFiles))
            {
                groupFiles = new List<SourceFileSnapshot>();
                restoredByGroup[file.Group] = groupFiles;
            }

            groupFiles.Add(snapshot);
        }

        var state = new CompactBuildState();
        var replacementSources = new Dictionary<string, FileSource>(StringComparer.Ordinal);
        Dictionary<string, PackageConfigGroup> configuredGroups = request.Config.Groups.ToDictionary(group => group.Name, StringComparer.Ordinal);
        Dictionary<(long Size, string FileHash), ManifestArtifact.FileArtifact> reusableFileArtifacts =
            source.FileArtifactsByContent.ToDictionary(pair => pair.Key, pair => pair.Value);

        foreach (string group in targetGroups)
        {
            if (!restoredByGroup.TryGetValue(group, out List<SourceFileSnapshot>? groupFiles) || groupFiles.Count == 0)
            {
                continue;
            }

            CompressionKind compression = FilePackageBuilder.ResolveCompression(request.Config, configuredGroups, group);
            BundleGroupWriteResult writeResult = await BundleArtifactWriter.WriteAsync(
                groupFiles,
                request.Config.PackageId,
                group,
                compression,
                request.Config.MaxArtifactBytes,
                outputRoot,
                stagingRoot,
                _zstdCodec,
                reusableFileArtifacts,
                cancellationToken).ConfigureAwait(false);

            foreach (BundleArtifactWriteResult bundle in writeResult.Bundles)
            {
                state.NewArtifacts.Add(bundle.Artifact);

                if (bundle.WasCreated)
                {
                    state.CreatedBundleHashes.Add(bundle.Artifact.ArtifactHash);
                    state.CreatedBundleBytes += bundle.PayloadBytes;
                }

                foreach (BundleEntry entry in bundle.Artifact.Entries)
                {
                    replacementSources.Add(
                        entry.Path,
                        new FileSource.BundleEntryReference(bundle.Artifact.ArtifactHash, entry.Path));
                }
            }

            foreach (BundleFallbackWriteResult fallback in writeResult.Fallbacks)
            {
                state.NewArtifacts.Add(fallback.ArtifactResult.Artifact);
                replacementSources.Add(
                    fallback.File.RelativePath,
                    new FileSource.FileReference(fallback.ArtifactResult.Artifact.PrimaryArtifactHash));

                if (fallback.ArtifactResult.WasCreated)
                {
                    state.CreatedFileArtifactHashes.Add(fallback.ArtifactResult.Artifact.PrimaryArtifactHash);
                    state.ReusedFileArtifactHashes.Remove(fallback.ArtifactResult.Artifact.PrimaryArtifactHash);
                    state.CreatedFileArtifactBytes += fallback.ArtifactResult.PayloadBytes;
                }
                else if (!state.CreatedFileArtifactHashes.Contains(fallback.ArtifactResult.Artifact.PrimaryArtifactHash))
                {
                    state.ReusedFileArtifactHashes.Add(fallback.ArtifactResult.Artifact.PrimaryArtifactHash);
                }
            }
        }

        var candidateFiles = source.Manifest.Files
            .Select(file => targetGroupSet.Contains(file.Group)
                ? new ManifestFileEntry(
                    file.Path,
                    file.Group,
                    file.Size,
                    file.FileHash,
                    replacementSources[file.Path])
                : file)
            .ToList();
        List<ManifestArtifact> candidateArtifacts = CollectReferencedArtifacts(
            request.Config.PackageId,
            source.Manifest.Artifacts,
            state.NewArtifacts,
            candidateFiles);
        HashSet<string> sourceFileArtifactHashes = source.Manifest.Artifacts
            .OfType<ManifestArtifact.FileArtifact>()
            .Select(artifact => artifact.PrimaryArtifactHash)
            .ToHashSet(StringComparer.Ordinal);

        foreach (ManifestArtifact.FileArtifact artifact in candidateArtifacts.OfType<ManifestArtifact.FileArtifact>())
        {
            if (sourceFileArtifactHashes.Contains(artifact.PrimaryArtifactHash)
                && !state.CreatedFileArtifactHashes.Contains(artifact.PrimaryArtifactHash))
            {
                state.ReusedFileArtifactHashes.Add(artifact.PrimaryArtifactHash);
            }
        }

        var candidate = new ReleaseManifest(
            ManifestSchemaVersion,
            request.Config.PackageId,
            source.Manifest.DataVersion,
            source.Manifest.CompactVersion,
            source.Manifest.Groups,
            candidateArtifacts,
            candidateFiles);
        state.Candidate = candidate;
        return state;
    }

    private async Task RestoreSelectedFilesAsync(
        string outputRoot,
        PreviousReleaseContext source,
        IReadOnlySet<string> targetGroups,
        string restoredRoot,
        CancellationToken cancellationToken)
    {
        foreach (ManifestArtifact.BundleArtifact bundle in source.Manifest.Artifacts.OfType<ManifestArtifact.BundleArtifact>())
        {
            if (!targetGroups.Contains(bundle.Group))
            {
                continue;
            }

            await BundleArchiveReader.ExtractAsync(
                outputRoot,
                source.Manifest,
                bundle,
                _zstdCodec,
                restoredRoot,
                cancellationToken).ConfigureAwait(false);
        }

        foreach (ManifestFileEntry file in source.Manifest.Files)
        {
            if (!targetGroups.Contains(file.Group) || file.Source is not FileSource.FileReference reference)
            {
                continue;
            }

            if (!source.FileArtifactsByHash.TryGetValue(reference.ArtifactHash, out ManifestArtifact.FileArtifact? artifact))
            {
                throw Failure(
                    PackageErrorCodes.ManifestInvalid,
                    "A compact source file references an unknown file artifact.",
                    source.Manifest.PackageId,
                    file.Path,
                    file.Group);
            }

            string restoredPath = PackagePath.Resolve(restoredRoot, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(restoredPath)!);
            await using (var destination = new FileStream(
                restoredPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                StreamBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await PackagePayloadVerifier.WriteDecodedFileArtifactAsync(
                    outputRoot,
                    artifact,
                    _zstdCodec,
                    destination,
                    source.Manifest.PackageId,
                    cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var information = new FileInfo(restoredPath);
            string restoredHash = await Sha256File.ComputeAsync(restoredPath, cancellationToken).ConfigureAwait(false);
            if (information.Length != file.Size || restoredHash != file.FileHash)
            {
                throw Failure(
                    PackageErrorCodes.ArtifactCorrupted,
                    "A file artifact does not restore the compact source file.",
                    source.Manifest.PackageId,
                    file.Path,
                    file.Group);
            }
        }
    }

    private static List<ManifestArtifact> CollectReferencedArtifacts(
        string packageId,
        IReadOnlyList<ManifestArtifact> sourceArtifacts,
        IReadOnlyList<ManifestArtifact> newArtifacts,
        IReadOnlyList<ManifestFileEntry> candidateFiles)
    {
        var artifactByIdentity = new Dictionary<string, ManifestArtifact>(StringComparer.Ordinal);

        foreach (ManifestArtifact artifact in sourceArtifacts.Concat(newArtifacts))
        {
            artifactByIdentity[ArtifactIdentity(artifact)] = artifact;
        }

        var referencedIdentities = new HashSet<string>(StringComparer.Ordinal);

        foreach (ManifestFileEntry file in candidateFiles)
        {
            string identity = file.Source switch
            {
                FileSource.FileReference fileReference => FileIdentity(fileReference.ArtifactHash),
                FileSource.BundleEntryReference bundleReference => BundleIdentity(file.Group, bundleReference.ArtifactHash),
                _ => throw new InvalidOperationException("Unknown manifest file source."),
            };

            if (!artifactByIdentity.ContainsKey(identity))
            {
                throw Failure(
                    PackageErrorCodes.ManifestInvalid,
                    "A compact candidate references an artifact that was not produced or retained.",
                    packageId,
                    file.Path,
                    file.Group);
            }

            referencedIdentities.Add(identity);
        }

        var artifacts = referencedIdentities.Select(identity => artifactByIdentity[identity]).ToList();
        artifacts.Sort((left, right) => Utf8OrdinalStringComparer.Instance.Compare(
            left.ContentAddressedSortKey(packageId),
            right.ContentAddressedSortKey(packageId)));
        return artifacts;
    }

    private static string ArtifactIdentity(ManifestArtifact artifact)
    {
        return artifact switch
        {
            ManifestArtifact.FileArtifact file => FileIdentity(file.PrimaryArtifactHash),
            ManifestArtifact.BundleArtifact bundle => BundleIdentity(bundle.Group, bundle.ArtifactHash),
            _ => throw new InvalidOperationException("Unknown manifest artifact."),
        };
    }

    private static string FileIdentity(string artifactHash)
    {
        return $"file:{artifactHash}";
    }

    private static string BundleIdentity(string group, string artifactHash)
    {
        return $"bundle:{group}:{artifactHash}";
    }

    private static string[] ValidateAndSortTargetGroups(BundleCompactRequest request, ReleaseManifest source)
    {
        HashSet<string> sourceGroups = source.Groups.Select(group => group.Name).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, PackageConfigGroup> configuredGroups = request.Config.Groups.ToDictionary(group => group.Name, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var groups = new List<string>();

        foreach (string group in request.TargetGroups)
        {
            if (string.IsNullOrWhiteSpace(group)
                || !KebabCaseId.IsValid(group)
                || !sourceGroups.Contains(group)
                || !seen.Add(group))
            {
                throw Failure(
                    PackageErrorCodes.InvalidConfiguration,
                    "Compact target groups must be unique groups declared by the source release.",
                    request.Config.PackageId,
                    group: group);
            }

            if (FilePackageBuilder.ResolveArtifactMode(request.Config, configuredGroups, group) != ArtifactMode.Bundle)
            {
                throw Failure(
                    PackageErrorCodes.InvalidConfiguration,
                    "A compact target group must use bundle artifact mode in the current configuration.",
                    request.Config.PackageId,
                    group: group);
            }

            groups.Add(group);
        }

        groups.Sort(Utf8OrdinalStringComparer.Instance);
        return groups.ToArray();
    }

    private static void ValidateRequestCollections(BundleCompactRequest request)
    {
        if (request.TargetGroups.Count == 0)
        {
            throw Failure(
                PackageErrorCodes.InvalidConfiguration,
                "At least one compact target group is required.",
                request.Config.PackageId);
        }

        if (request.TargetGroups.Any(group => group == null)
            || request.RetainedObjects.Any(item => item == null))
        {
            throw Failure(
                PackageErrorCodes.InvalidConfiguration,
                "Compact target groups and retained objects must not contain null values.",
                request.Config.PackageId);
        }
    }

    private void ValidateRequestedCodec(BundleCompactRequest request)
    {
        if (request.WriteCompressedManifest
            && (_zstdCodec == null || _zstdCodec.CodecId != CompressionCodecIds.Zstd))
        {
            throw Failure(
                PackageErrorCodes.MissingCompressionCodec,
                "A compressed compact manifest was requested, but no compatible zstd codec was supplied.",
                request.Config.PackageId);
        }
    }

    private static void VerifyLogicalStateUnchanged(ReleaseManifest source, ReleaseManifest candidate)
    {
        if (source.Groups.Count != candidate.Groups.Count
            || source.Files.Count != candidate.Files.Count
            || !source.Groups.Zip(candidate.Groups, GroupsEqual).All(equal => equal)
            || !source.Files.Zip(candidate.Files, FilesEqual).All(equal => equal))
        {
            throw Failure(
                PackageErrorCodes.ManifestInvalid,
                "Compact changed the source release's logical file or group state.",
                source.PackageId);
        }
    }

    private static bool GroupsEqual(ManifestGroupEntry left, ManifestGroupEntry right)
    {
        return left.Name == right.Name && left.Required == right.Required;
    }

    private static bool FilesEqual(ManifestFileEntry left, ManifestFileEntry right)
    {
        return left.Path == right.Path
            && left.Group == right.Group
            && left.Size == right.Size
            && left.FileHash == right.FileHash;
    }

    private static PackageException Failure(
        string code,
        string message,
        string packageId,
        string? relativePath = null,
        string? group = null)
    {
        return new PackageException(new GamePatchKitError(Stage, code, message, packageId, relativePath, group));
    }

    private sealed class CompactBuildState
    {
        public List<ManifestArtifact> NewArtifacts { get; } = new List<ManifestArtifact>();

        public HashSet<string> CreatedBundleHashes { get; } = new HashSet<string>(StringComparer.Ordinal);

        public HashSet<string> ReusedFileArtifactHashes { get; } = new HashSet<string>(StringComparer.Ordinal);

        public HashSet<string> CreatedFileArtifactHashes { get; } = new HashSet<string>(StringComparer.Ordinal);

        public long CreatedBundleBytes { get; set; }

        public long CreatedFileArtifactBytes { get; set; }

        public ReleaseManifest Candidate { get; set; } = null!;
    }
}