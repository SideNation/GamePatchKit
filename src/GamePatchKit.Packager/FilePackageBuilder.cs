using GamePatchKit.Compression.NativeCompressions;
using GamePatchKit.Core;
using GamePatchKit.Core.Configuration;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Core.Paths;

namespace GamePatchKit.Packager;

public sealed class FilePackageBuilder
{
    private const string Stage = "package";
    private const int ManifestSchemaVersion = 1;
    private const int StreamBufferSize = 64 * 1024;
    private const string DefaultGroupName = "default";

    private readonly ICompressionCodec? _zstdCodec;
    private readonly Func<CancellationToken, Task>? _beforeFinalSourceVerification;

    public FilePackageBuilder()
        : this(ZstdCompressionCodecFactory.Create(), beforeFinalSourceVerification: null)
    {
    }

    public FilePackageBuilder(ICompressionCodec? zstdCodec)
        : this(zstdCodec, beforeFinalSourceVerification: null)
    {
    }

    internal FilePackageBuilder(
        ICompressionCodec? zstdCodec,
        Func<CancellationToken, Task>? beforeFinalSourceVerification)
    {
        _zstdCodec = zstdCodec;
        _beforeFinalSourceVerification = beforeFinalSourceVerification;
    }

    public async Task<FilePackageResult> BuildAsync(
        FilePackageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        ValidateConfiguration(request.Config);
        ValidateRequestedCodec(request);
        SourceSnapshot source = SourceSnapshotter.Capture(request.Config);
        EnsureOutputIsOutsideSource(source.RootPath, request.OutputRoot, request.Config.PackageId);
        await SourceSnapshotter.HashFilesAsync(source, request.Config.PackageId, cancellationToken).ConfigureAwait(false);

        PreviousReleaseContext? previous = null;
        if (request.PreviousRelease != null)
        {
            previous = await PreviousReleaseReader.ReadAsync(
                request.PreviousRelease,
                request.Config.PackageId,
                request.OutputRoot,
                _zstdCodec,
                cancellationToken).ConfigureAwait(false);
        }

        string outputRoot = Path.GetFullPath(request.OutputRoot);
        PackagePath.EnsureOutputRootIsNotLink(outputRoot, request.Config.PackageId);
        Directory.CreateDirectory(outputRoot);
        string stagingRoot = Path.Combine(outputRoot, $".gpk-staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);

        try
        {
            BuildState state = await BuildCandidateAsync(
                request,
                source,
                previous,
                outputRoot,
                stagingRoot,
                cancellationToken).ConfigureAwait(false);

            GamePatchKitSchemaValidator.ValidateReleaseManifest(
                state.Finalized.GetCanonicalBytes(),
                request.Config.PackageId);

            ValidationResult manifestValidation = ManifestValidator.Validate(state.Finalized.Manifest);
            if (!manifestValidation.IsValid)
            {
                throw new PackageException(manifestValidation.Errors);
            }

            if (_beforeFinalSourceVerification != null)
            {
                await _beforeFinalSourceVerification(cancellationToken).ConfigureAwait(false);
            }

            SourceSnapshotter.VerifyUnchanged(source, request.Config);
            await PublishArtifactsAsync(
                state.Finalized.Manifest,
                outputRoot,
                stagingRoot,
                request.Config.PackageId,
                cancellationToken).ConfigureAwait(false);
            await PackagePayloadVerifier.VerifyAsync(outputRoot, state.Finalized.Manifest, _zstdCodec, cancellationToken).ConfigureAwait(false);
            await PublishManifestAsync(
                state.Finalized,
                request.WriteCompressedManifest,
                outputRoot,
                stagingRoot,
                cancellationToken).ConfigureAwait(false);

            bool reusedManifest = previous != null && previous.FinalizedManifest.ManifestHash == state.Finalized.ManifestHash;
            PackageBuildReport report = CreateReport(request, state);
            return new FilePackageResult(state.Finalized, report, reusedManifest);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }
        }
    }

    private async Task<BuildState> BuildCandidateAsync(
        FilePackageRequest request,
        SourceSnapshot source,
        PreviousReleaseContext? previous,
        string outputRoot,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        var state = new BuildState();
        Dictionary<string, PackageConfigGroup> configuredGroups = request.Config.Groups.ToDictionary(group => group.Name, StringComparer.Ordinal);
        Dictionary<(long Size, string FileHash), ManifestArtifact.FileArtifact> reusableFileArtifacts =
            previous?.FileArtifactsByContent.ToDictionary(pair => pair.Key, pair => pair.Value)
            ?? new Dictionary<(long Size, string FileHash), ManifestArtifact.FileArtifact>();
        HashSet<string> reusableBundleHashes = FindReusableBundles(source, request.Config, previous, configuredGroups);
        var initialBundleFilesByGroup = new Dictionary<string, List<SourceFileSnapshot>>(StringComparer.Ordinal);

        foreach (SourceFileSnapshot file in source.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ManifestFileEntry? previousFile = null;
            previous?.FilesByPath.TryGetValue(file.RelativePath, out previousFile);
            ClassifyChange(file, previousFile, state);

            if (previous == null && ResolveArtifactMode(request.Config, configuredGroups, file.Group) == ArtifactMode.Bundle)
            {
                if (!initialBundleFilesByGroup.TryGetValue(file.Group, out List<SourceFileSnapshot>? groupFiles))
                {
                    groupFiles = new List<SourceFileSnapshot>();
                    initialBundleFilesByGroup[file.Group] = groupFiles;
                }

                groupFiles.Add(file);
                continue;
            }

            FileSource sourceReference;

            if (CanReusePreviousFileArtifact(file, previousFile, previous, out ManifestArtifact.FileArtifact? exactFileArtifact))
            {
                sourceReference = AddFileArtifact(state, exactFileArtifact!, wasCreated: false);
            }
            else if (CanReusePreviousBundle(file, previousFile, previous, reusableBundleHashes, out ManifestArtifact.BundleArtifact? bundleArtifact))
            {
                state.ArtifactsByKey.TryAdd(bundleArtifact!.ContentAddressedSortKey(request.Config.PackageId), bundleArtifact);
                state.ReusedBundleHashes.Add(bundleArtifact.ArtifactHash);
                var bundleReference = (FileSource.BundleEntryReference)previousFile!.Source;
                sourceReference = new FileSource.BundleEntryReference(bundleReference.ArtifactHash, bundleReference.EntryPath);
            }
            else if (reusableFileArtifacts.TryGetValue((file.Size, file.FileHash), out ManifestArtifact.FileArtifact? reusableArtifact))
            {
                sourceReference = AddFileArtifact(state, reusableArtifact, wasCreated: false);
            }
            else
            {
                CompressionKind compression = ResolveCompression(request.Config, configuredGroups, file.Group);
                FileArtifactWriteResult writeResult = await FileArtifactWriter.WriteAsync(
                    file,
                    request.Config.PackageId,
                    compression,
                    request.Config.MaxArtifactBytes,
                    outputRoot,
                    stagingRoot,
                    _zstdCodec,
                    cancellationToken).ConfigureAwait(false);
                reusableFileArtifacts[(file.Size, file.FileHash)] = writeResult.Artifact;
                sourceReference = AddFileArtifact(state, writeResult.Artifact, writeResult.WasCreated);

                if (writeResult.WasCreated)
                {
                    state.CreatedFileArtifactBytes += writeResult.PayloadBytes;
                }
            }

            state.Files.Add(new ManifestFileEntry(file.RelativePath, file.Group, file.Size, file.FileHash, sourceReference));
        }

        foreach (KeyValuePair<string, List<SourceFileSnapshot>> pair in initialBundleFilesByGroup
            .OrderBy(item => item.Key, Utf8OrdinalStringComparer.Instance))
        {
            CompressionKind compression = ResolveCompression(request.Config, configuredGroups, pair.Key);
            BundleGroupWriteResult writeResult = await BundleArtifactWriter.WriteAsync(
                pair.Value,
                request.Config.PackageId,
                pair.Key,
                compression,
                request.Config.MaxArtifactBytes,
                outputRoot,
                stagingRoot,
                _zstdCodec,
                reusableFileArtifacts,
                cancellationToken).ConfigureAwait(false);
            var sourcesByPath = new Dictionary<string, FileSource>(StringComparer.Ordinal);

            foreach (BundleArtifactWriteResult bundle in writeResult.Bundles)
            {
                state.ArtifactsByKey.TryAdd(bundle.Artifact.ContentAddressedSortKey(request.Config.PackageId), bundle.Artifact);

                if (bundle.WasCreated)
                {
                    state.CreatedBundleHashes.Add(bundle.Artifact.ArtifactHash);
                    state.CreatedBundleBytes += bundle.PayloadBytes;
                }
                else
                {
                    state.ReusedBundleHashes.Add(bundle.Artifact.ArtifactHash);
                }

                foreach (BundleEntry entry in bundle.Artifact.Entries)
                {
                    sourcesByPath.Add(
                        entry.Path,
                        new FileSource.BundleEntryReference(bundle.Artifact.ArtifactHash, entry.Path));
                }
            }

            foreach (BundleFallbackWriteResult fallback in writeResult.Fallbacks)
            {
                sourcesByPath.Add(
                    fallback.File.RelativePath,
                    AddFileArtifact(state, fallback.ArtifactResult.Artifact, fallback.ArtifactResult.WasCreated));

                if (fallback.ArtifactResult.WasCreated)
                {
                    state.CreatedFileArtifactBytes += fallback.ArtifactResult.PayloadBytes;
                }
            }

            foreach (SourceFileSnapshot file in pair.Value)
            {
                state.Files.Add(new ManifestFileEntry(
                    file.RelativePath,
                    file.Group,
                    file.Size,
                    file.FileHash,
                    sourcesByPath[file.RelativePath]));
            }
        }

        state.Files.Sort((left, right) => Utf8OrdinalStringComparer.Instance.Compare(left.Path, right.Path));

        if (previous != null)
        {
            HashSet<string> currentPaths = source.Files.Select(file => file.RelativePath).ToHashSet(StringComparer.Ordinal);
            foreach (ManifestFileEntry previousFile in previous.Manifest.Files)
            {
                if (!currentPaths.Contains(previousFile.Path))
                {
                    state.DeletedFiles.Add(previousFile.Path);
                }
            }
        }

        List<ManifestGroupEntry> groups = BuildManifestGroups(request.Config, source.Files, configuredGroups);
        List<ManifestArtifact> artifacts = state.ArtifactsByKey.Values.ToList();
        artifacts.Sort((left, right) => Utf8OrdinalStringComparer.Instance.Compare(
            left.ContentAddressedSortKey(request.Config.PackageId),
            right.ContentAddressedSortKey(request.Config.PackageId)));

        var draft = new ReleaseManifest(
            ManifestSchemaVersion,
            request.Config.PackageId,
            DataVersionFormat.Prefix + new string('0', 64),
            previous?.Manifest.CompactVersion ?? 0,
            groups,
            artifacts,
            state.Files);
        state.Finalized = ReleaseIdentity.Finalize(draft, previous?.Manifest.CompactVersion ?? 0);
        return state;
    }

    private static HashSet<string> FindReusableBundles(
        SourceSnapshot source,
        PackageConfig config,
        PreviousReleaseContext? previous,
        IReadOnlyDictionary<string, PackageConfigGroup> configuredGroups)
    {
        var reusable = new HashSet<string>(StringComparer.Ordinal);

        if (previous == null)
        {
            return reusable;
        }

        var currentByPath = source.Files.ToDictionary(file => file.RelativePath, StringComparer.Ordinal);

        foreach (ManifestArtifact.BundleArtifact bundle in previous.BundleArtifactsByHash.Values)
        {
            bool canReuse = true;

            foreach (BundleEntry entry in bundle.Entries)
            {
                if (!previous.FilesByPath.TryGetValue(entry.Path, out ManifestFileEntry? previousFile)
                    || previousFile.Source is not FileSource.BundleEntryReference reference
                    || reference.ArtifactHash != bundle.ArtifactHash
                    || !currentByPath.TryGetValue(entry.Path, out SourceFileSnapshot? currentFile)
                    || currentFile.Size != previousFile.Size
                    || currentFile.FileHash != previousFile.FileHash
                    || currentFile.Group != previousFile.Group
                    || ResolveArtifactMode(config, configuredGroups, currentFile.Group) != ArtifactMode.Bundle)
                {
                    canReuse = false;
                    break;
                }
            }

            if (canReuse)
            {
                reusable.Add(bundle.ArtifactHash);
            }
        }

        return reusable;
    }

    private static bool CanReusePreviousFileArtifact(
        SourceFileSnapshot file,
        ManifestFileEntry? previousFile,
        PreviousReleaseContext? previous,
        out ManifestArtifact.FileArtifact? artifact)
    {
        artifact = null;

        if (previous == null
            || previousFile == null
            || previousFile.Size != file.Size
            || previousFile.FileHash != file.FileHash
            || previousFile.Source is not FileSource.FileReference reference)
        {
            return false;
        }

        return previous.FileArtifactsByHash.TryGetValue(reference.ArtifactHash, out artifact);
    }

    private static bool CanReusePreviousBundle(
        SourceFileSnapshot file,
        ManifestFileEntry? previousFile,
        PreviousReleaseContext? previous,
        IReadOnlySet<string> reusableBundleHashes,
        out ManifestArtifact.BundleArtifact? artifact)
    {
        artifact = null;

        if (previous == null
            || previousFile == null
            || previousFile.Size != file.Size
            || previousFile.FileHash != file.FileHash
            || previousFile.Group != file.Group
            || previousFile.Source is not FileSource.BundleEntryReference reference
            || !reusableBundleHashes.Contains(reference.ArtifactHash))
        {
            return false;
        }

        return previous.BundleArtifactsByHash.TryGetValue(reference.ArtifactHash, out artifact);
    }

    private static FileSource AddFileArtifact(BuildState state, ManifestArtifact.FileArtifact artifact, bool wasCreated)
    {
        state.ArtifactsByKey.TryAdd(artifact.PrimaryArtifactHash, artifact);

        if (wasCreated)
        {
            state.CreatedFileArtifactHashes.Add(artifact.PrimaryArtifactHash);
        }
        else if (!state.CreatedFileArtifactHashes.Contains(artifact.PrimaryArtifactHash))
        {
            state.ReusedFileArtifactHashes.Add(artifact.PrimaryArtifactHash);
        }

        return new FileSource.FileReference(artifact.PrimaryArtifactHash);
    }

    private static void ClassifyChange(SourceFileSnapshot file, ManifestFileEntry? previousFile, BuildState state)
    {
        if (previousFile == null)
        {
            state.AddedFiles.Add(file.RelativePath);
            return;
        }

        if (previousFile.Size != file.Size || previousFile.FileHash != file.FileHash)
        {
            state.ChangedFiles.Add(file.RelativePath);
        }

        if (previousFile.Group != file.Group)
        {
            state.MovedGroupFiles.Add(file.RelativePath);
        }
    }

    private static List<ManifestGroupEntry> BuildManifestGroups(
        PackageConfig config,
        IReadOnlyList<SourceFileSnapshot> files,
        IReadOnlyDictionary<string, PackageConfigGroup> configuredGroups)
    {
        var groups = configuredGroups.Values
            .Select(group => new ManifestGroupEntry(group.Name, group.Required))
            .ToList();

        if (files.Any(file => file.Group == DefaultGroupName))
        {
            groups.Add(new ManifestGroupEntry(DefaultGroupName, required: true));
        }

        groups.Sort((left, right) => Utf8OrdinalStringComparer.Instance.Compare(left.Name, right.Name));
        return groups;
    }

    internal static CompressionKind ResolveCompression(
        PackageConfig config,
        IReadOnlyDictionary<string, PackageConfigGroup> configuredGroups,
        string group)
    {
        return configuredGroups.TryGetValue(group, out PackageConfigGroup? configuredGroup)
            ? configuredGroup.Compression ?? config.Compression
            : config.Compression;
    }

    internal static ArtifactMode ResolveArtifactMode(
        PackageConfig config,
        IReadOnlyDictionary<string, PackageConfigGroup> configuredGroups,
        string group)
    {
        return configuredGroups.TryGetValue(group, out PackageConfigGroup? configuredGroup)
            ? configuredGroup.ArtifactMode
            : config.DefaultArtifactMode;
    }

    internal static async Task PublishArtifactsAsync(
        ReleaseManifest manifest,
        string outputRoot,
        string stagingRoot,
        string packageId,
        CancellationToken cancellationToken)
    {
        foreach (ManifestArtifact.FileArtifact artifact in manifest.Artifacts.OfType<ManifestArtifact.FileArtifact>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string canonicalDirectory = artifact.ContentAddressedSortKey(packageId);
            string stagedDirectory = PackagePath.Resolve(stagingRoot, canonicalDirectory);

            if (!Directory.Exists(stagedDirectory))
            {
                continue;
            }

            string destinationDirectory = PackagePath.Resolve(outputRoot, canonicalDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationDirectory)!);

            if (Directory.Exists(destinationDirectory))
            {
                await PackagePayloadVerifier.VerifyStoredObjectsAsync(
                    outputRoot,
                    artifact.GetPayloadObjects(),
                    packageId,
                    cancellationToken).ConfigureAwait(false);
                Directory.Delete(stagedDirectory, recursive: true);
                continue;
            }

            Directory.Move(stagedDirectory, destinationDirectory);
        }

        foreach (ManifestArtifact.BundleArtifact artifact in manifest.Artifacts.OfType<ManifestArtifact.BundleArtifact>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string stagedPath = PackagePath.Resolve(stagingRoot, artifact.Path);

            if (!File.Exists(stagedPath))
            {
                continue;
            }

            string destinationPath = PackagePath.Resolve(outputRoot, artifact.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            if (File.Exists(destinationPath))
            {
                await PackagePayloadVerifier.VerifyStoredObjectsAsync(
                    outputRoot,
                    artifact.GetPayloadObjects(),
                    packageId,
                    cancellationToken).ConfigureAwait(false);
                File.Delete(stagedPath);
                continue;
            }

            File.Move(stagedPath, destinationPath);
        }
    }

    internal async Task PublishManifestAsync(
        FinalizedManifest finalized,
        bool writeCompressedManifest,
        string outputRoot,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        byte[] canonicalBytes = finalized.GetCanonicalBytes();
        ValidationResult canonicalValidation = ReleaseIdentity.VerifyManifestHash(canonicalBytes, finalized.ManifestHash);
        if (!canonicalValidation.IsValid)
        {
            throw new PackageException(canonicalValidation.Errors);
        }

        string canonicalDirectory = $"{finalized.Manifest.PackageId}/manifests/{finalized.ManifestHash}";
        string destinationDirectory = PackagePath.Resolve(outputRoot, canonicalDirectory);
        string destinationManifestPath = Path.Combine(destinationDirectory, "manifest.json");

        if (File.Exists(destinationManifestPath))
        {
            byte[] existing = await File.ReadAllBytesAsync(destinationManifestPath, cancellationToken).ConfigureAwait(false);
            if (!existing.AsSpan().SequenceEqual(canonicalBytes))
            {
                throw Failure(
                    PackageErrorCodes.ImmutablePathConflict,
                    "An existing manifestHash path contains different manifest bytes.",
                    finalized.Manifest.PackageId);
            }
        }
        else
        {
            string stagedDirectory = PackagePath.Resolve(stagingRoot, canonicalDirectory);
            Directory.CreateDirectory(stagedDirectory);
            string stagedManifestPath = Path.Combine(stagedDirectory, "manifest.json");
            await File.WriteAllBytesAsync(stagedManifestPath, canonicalBytes, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationDirectory)!);

            if (Directory.Exists(destinationDirectory))
            {
                File.Move(stagedManifestPath, destinationManifestPath);
                Directory.Delete(stagedDirectory);
            }
            else
            {
                Directory.Move(stagedDirectory, destinationDirectory);
            }
        }

        byte[] publishedManifestBytes = await File.ReadAllBytesAsync(destinationManifestPath, cancellationToken).ConfigureAwait(false);
        if (!publishedManifestBytes.AsSpan().SequenceEqual(canonicalBytes))
        {
            throw Failure(
                PackageErrorCodes.ImmutablePathConflict,
                "The published manifest bytes changed during commit.",
                finalized.Manifest.PackageId);
        }

        if (!writeCompressedManifest)
        {
            return;
        }

        if (_zstdCodec == null || _zstdCodec.CodecId != CompressionCodecIds.Zstd)
        {
            throw Failure(
                PackageErrorCodes.MissingCompressionCodec,
                "A compressed manifest was requested, but no compatible zstd codec was supplied.",
                finalized.Manifest.PackageId);
        }

        string compressedPath = Path.Combine(destinationDirectory, "manifest.json.zst");
        string temporaryPath = Path.Combine(destinationDirectory, $".manifest-{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var source = new MemoryStream(canonicalBytes, writable: false))
            await using (var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                StreamBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await _zstdCodec.CompressAsync(source, destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            await VerifyCompressedManifestAsync(
                temporaryPath,
                canonicalBytes.LongLength,
                finalized.ManifestHash,
                finalized.Manifest.PackageId,
                cancellationToken).ConfigureAwait(false);

            if (File.Exists(compressedPath))
            {
                if (!await FilesEqualAsync(temporaryPath, compressedPath, cancellationToken).ConfigureAwait(false))
                {
                    throw Failure(
                        PackageErrorCodes.ImmutablePathConflict,
                        "An existing compressed manifest has different bytes.",
                        finalized.Manifest.PackageId);
                }
            }
            else
            {
                File.Move(temporaryPath, compressedPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task VerifyCompressedManifestAsync(
        string compressedPath,
        long expectedSize,
        string expectedHash,
        string packageId,
        CancellationToken cancellationToken)
    {
        await using var compressed = new FileStream(
            compressedPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            StreamBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var output = new HashingWriteStream();
        await _zstdCodec!.DecompressAsync(compressed, output, cancellationToken).ConfigureAwait(false);
        string actualHash = output.FinalizeHash();

        if (output.BytesWritten != expectedSize || actualHash != expectedHash)
        {
            throw new PackageException(
                new GamePatchKitError(
                    Stage,
                    PackageErrorCodes.ArtifactCorrupted,
                    "The compressed manifest does not reconstruct the canonical manifest.",
                    packageId,
                    relativePath: "manifest.json.zst"));
        }
    }

    private static async Task<bool> FilesEqualAsync(string leftPath, string rightPath, CancellationToken cancellationToken)
    {
        var leftInfo = new FileInfo(leftPath);
        var rightInfo = new FileInfo(rightPath);

        if (leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        await using var left = new FileStream(leftPath, FileMode.Open, FileAccess.Read, FileShare.Read, StreamBufferSize, FileOptions.Asynchronous);
        await using var right = new FileStream(rightPath, FileMode.Open, FileAccess.Read, FileShare.Read, StreamBufferSize, FileOptions.Asynchronous);
        byte[] leftBuffer = new byte[StreamBufferSize];
        byte[] rightBuffer = new byte[StreamBufferSize];

        while (true)
        {
            int leftRead = await left.ReadAsync(leftBuffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            int rightRead = await right.ReadAsync(rightBuffer.AsMemory(), cancellationToken).ConfigureAwait(false);

            if (leftRead != rightRead)
            {
                return false;
            }

            if (leftRead == 0)
            {
                return true;
            }

            if (!leftBuffer.AsSpan(0, leftRead).SequenceEqual(rightBuffer.AsSpan(0, rightRead)))
            {
                return false;
            }
        }
    }

    private static PackageBuildReport CreateReport(FilePackageRequest request, BuildState state)
    {
        var policies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DefaultGroupName] = CompressionName(request.Config.Compression),
        };

        foreach (PackageConfigGroup group in request.Config.Groups)
        {
            policies[group.Name] = CompressionName(group.Compression ?? request.Config.Compression);
        }

        return new PackageBuildReport(
            DateTimeOffset.UtcNow,
            Environment.MachineName,
            request.SourceRevision,
            state.Finalized.DataVersion,
            state.Finalized.CompactVersion,
            state.Finalized.ManifestHash,
            state.AddedFiles,
            state.ChangedFiles,
            state.DeletedFiles,
            state.MovedGroupFiles,
            state.CreatedFileArtifactHashes.Count,
            state.CreatedFileArtifactBytes,
            state.ReusedFileArtifactHashes.Count,
            state.CreatedBundleHashes.Count,
            state.CreatedBundleBytes,
            state.ReusedBundleHashes.Count,
            policies);
    }

    private static string CompressionName(CompressionKind compression)
    {
        return compression == CompressionKind.Zstd ? CompressionCodecIds.Zstd : "none";
    }

    internal static void ValidateConfiguration(PackageConfig config)
    {
        GamePatchKitSchemaValidator.ValidatePackageConfig(config);

        bool hasDefinedEnums = Enum.IsDefined(config.DefaultArtifactMode)
            && Enum.IsDefined(config.Compression)
            && config.Groups.All(group => Enum.IsDefined(group.ArtifactMode)
                && (!group.Compression.HasValue || Enum.IsDefined(group.Compression.Value)));
        bool parsed = PackageConfig.TryParse(
            config.ToJson(),
            out PackageConfig? reparsed,
            out IReadOnlyList<GamePatchKitError> parseErrors);

        if (!hasDefinedEnums || !parsed)
        {
            throw new PackageException(
                parseErrors.Count > 0
                    ? parseErrors
                    : new[]
                    {
                        new GamePatchKitError(
                            Stage,
                            PackageErrorCodes.InvalidConfiguration,
                            "The package configuration contains an unsupported enum value.",
                            config.PackageId),
                    });
        }

        ValidationResult validation = PackageConfigValidator.Validate(reparsed!);
        if (!validation.IsValid)
        {
            throw new PackageException(validation.Errors);
        }
    }

    private void ValidateRequestedCodec(FilePackageRequest request)
    {
        if (request.WriteCompressedManifest
            && (_zstdCodec == null || _zstdCodec.CodecId != CompressionCodecIds.Zstd))
        {
            throw Failure(
                PackageErrorCodes.MissingCompressionCodec,
                "The package request requires zstd, but no compatible codec was supplied.",
                request.Config.PackageId);
        }
    }

    private static void EnsureOutputIsOutsideSource(string sourceRoot, string outputRoot, string packageId)
    {
        string normalizedSource = Path.GetFullPath(sourceRoot);
        string normalizedOutput = Path.GetFullPath(outputRoot);
        string sourcePrefix = normalizedSource.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedSource
            : normalizedSource + Path.DirectorySeparatorChar;

        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (normalizedOutput.Equals(normalizedSource, pathComparison) || normalizedOutput.StartsWith(sourcePrefix, pathComparison))
        {
            throw Failure(
                PackageErrorCodes.InvalidConfiguration,
                "The output root must be outside inputRoot.",
                packageId);
        }
    }

    private static PackageException Failure(string code, string message, string packageId)
    {
        return new PackageException(new GamePatchKitError(Stage, code, message, packageId));
    }

    private sealed class BuildState
    {
        public Dictionary<string, ManifestArtifact> ArtifactsByKey { get; } = new Dictionary<string, ManifestArtifact>(StringComparer.Ordinal);

        public List<ManifestFileEntry> Files { get; } = new List<ManifestFileEntry>();

        public List<string> AddedFiles { get; } = new List<string>();

        public List<string> ChangedFiles { get; } = new List<string>();

        public List<string> DeletedFiles { get; } = new List<string>();

        public List<string> MovedGroupFiles { get; } = new List<string>();

        public HashSet<string> CreatedFileArtifactHashes { get; } = new HashSet<string>(StringComparer.Ordinal);

        public HashSet<string> ReusedFileArtifactHashes { get; } = new HashSet<string>(StringComparer.Ordinal);

        public HashSet<string> ReusedBundleHashes { get; } = new HashSet<string>(StringComparer.Ordinal);

        public HashSet<string> CreatedBundleHashes { get; } = new HashSet<string>(StringComparer.Ordinal);

        public long CreatedFileArtifactBytes { get; set; }

        public long CreatedBundleBytes { get; set; }

        public FinalizedManifest Finalized { get; set; } = null!;
    }
}