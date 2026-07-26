using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GamePatchKit.Core;
using GamePatchKit.Core.Downloads;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Core.Signatures;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Runtime
{
    public sealed class PackageRuntime
    {
        private const string Stage = "runtime";
        private const int StreamBufferSize = 64 * 1024;
        private const int MaximumAttempts = 3;

        // The manifest is the one response read into memory whole, and it has to be read before its hash can
        // say anything about it - so without a bound, a faulty or hostile endpoint answering a trusted
        // reference can exhaust the process before the first integrity check ever runs. Artifact objects need
        // no such limit: each one is streamed through RuntimeHashingWriteStream against the size the manifest
        // already declared for it.
        //
        // The PRD's reference fixture is 10,000 files and 1 GiB of source, whose canonical manifest is a few
        // MiB, so this leaves roughly an order of magnitude of headroom and still sits far under the 512 MiB
        // peak-RSS budget.
        private const int MaximumManifestBytes = 64 * 1024 * 1024;

        // manifest.sig is a handful of short fixed-shape fields (schemaVersion, algorithm, keyId, a base64url
        // signature); this is generous headroom over that, not a size an honest signature ever approaches.
        private const int MaximumSignatureBytes = 4 * 1024;

        private static readonly UTF8Encoding _strictUtf8 =
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        private readonly IArtifactTransport _transport;
        private readonly IRuntimeStorage _storage;
        private readonly IReadOnlyDictionary<string, ICompressionCodec> _codecs;
        private readonly TrustedSigningKeys? _trustedSigningKeys;
        private readonly bool _requireSignature;

        public PackageRuntime(
            IArtifactTransport transport,
            IRuntimeStorage storage,
            IEnumerable<ICompressionCodec>? compressionCodecs = null,
            TrustedSigningKeys? trustedSigningKeys = null,
            bool requireSignature = false)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));

            var codecs = new Dictionary<string, ICompressionCodec>(StringComparer.Ordinal);

            if (compressionCodecs != null)
            {
                foreach (ICompressionCodec codec in compressionCodecs)
                {
                    if (codec == null)
                    {
                        throw new ArgumentException("compressionCodecs must not contain null.", nameof(compressionCodecs));
                    }

                    if (!codecs.TryAdd(codec.CodecId, codec))
                    {
                        throw new ArgumentException(
                            $"More than one codec uses ID '{codec.CodecId}'.",
                            nameof(compressionCodecs));
                    }
                }
            }

            _codecs = codecs;

            // Requiring a signature needs trusted keys; without any, verification cannot tell a real signature
            // from a forged one, so refusing this combination outright is safer than silently downgrading it
            // to a presence check.
            if (requireSignature && trustedSigningKeys == null)
            {
                throw new ArgumentException(
                    "Requiring a signature needs trusted signing keys.",
                    nameof(requireSignature));
            }

            _trustedSigningKeys = trustedSigningKeys;
            _requireSignature = requireSignature;
        }

        public async Task<PackageState> InstallOrUpdateAsync(
            TargetManifestReference target,
            IProgress<PatchProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            ReleaseManifest targetManifest = await LoadManifestAsync(
                target,
                progress,
                cancellationToken).ConfigureAwait(false);

            for (int attempt = 0; attempt < MaximumAttempts; attempt++)
            {
                StateSnapshot snapshot = await LoadStateAsync(
                    target.PackageId,
                    allowRecovery: true,
                    cancellationToken).ConfigureAwait(false);
                PreparedState prepared = await PrepareGlobalAsync(
                    snapshot,
                    targetManifest,
                    target.ManifestHash,
                    progress,
                    cancellationToken).ConfigureAwait(false);

                if (!prepared.RequiresCommit)
                {
                    progress?.Report(new PatchProgress(PatchStage.Completed));
                    return prepared.State;
                }

                progress?.Report(new PatchProgress(PatchStage.Activating));
                if (await TryCommitAsync(snapshot, prepared.State, cancellationToken).ConfigureAwait(false))
                {
                    progress?.Report(new PatchProgress(PatchStage.Completed));
                    return prepared.State;
                }
            }

            throw Failure(
                RuntimeErrorCodes.StateConflict,
                "PackageState changed during activation too many times; retry the operation.",
                target.PackageId);
        }

        public async Task<PackageState> InstallOptionalGroupsAsync(
            string packageId,
            IEnumerable<string> groupNames,
            IProgress<PatchProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!KebabCaseId.IsValid(packageId))
            {
                throw new ArgumentException("packageId must be lowercase kebab-case.", nameof(packageId));
            }

            if (groupNames == null)
            {
                throw new ArgumentNullException(nameof(groupNames));
            }

            string[] requestedGroups = groupNames
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (requestedGroups.Length == 0)
            {
                throw new ArgumentException("At least one optional group is required.", nameof(groupNames));
            }

            for (int attempt = 0; attempt < MaximumAttempts; attempt++)
            {
                StateSnapshot snapshot = await LoadStateAsync(
                    packageId,
                    allowRecovery: false,
                    cancellationToken).ConfigureAwait(false);
                PackageState state = snapshot.State
                    ?? throw Failure(
                        RuntimeErrorCodes.StateInvalid,
                        "Optional groups require a valid active PackageState.",
                        packageId);
                ReleaseManifest activeManifest = snapshot.Manifest!;
                ValidateOptionalGroupRequest(activeManifest, requestedGroups);

                PreparedState prepared = await PrepareOptionalAsync(
                    snapshot,
                    requestedGroups,
                    progress,
                    cancellationToken).ConfigureAwait(false);

                if (!prepared.RequiresCommit)
                {
                    progress?.Report(new PatchProgress(PatchStage.Completed));
                    return state;
                }

                progress?.Report(new PatchProgress(PatchStage.Activating));
                if (await TryCommitAsync(snapshot, prepared.State, cancellationToken).ConfigureAwait(false))
                {
                    progress?.Report(new PatchProgress(PatchStage.Completed));
                    return prepared.State;
                }
            }

            throw Failure(
                RuntimeErrorCodes.StateConflict,
                "PackageState changed during optional-group activation too many times; retry the operation.",
                packageId);
        }

        private async Task<PreparedState> PrepareGlobalAsync(
            StateSnapshot snapshot,
            ReleaseManifest target,
            string targetManifestHash,
            IProgress<PatchProgress>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report(new PatchProgress(PatchStage.Planning));
            var previousGroups = snapshot.State?.Groups.ToDictionary(group => group.Name, StringComparer.Ordinal)
                ?? new Dictionary<string, PackageGroupState>(StringComparer.Ordinal);
            var nextGroups = new List<PackageGroupState>(target.Groups.Count);

            foreach (ManifestGroupEntry targetGroup in target.Groups)
            {
                previousGroups.TryGetValue(targetGroup.Name, out PackageGroupState? previousGroup);

                if (targetGroup.Required)
                {
                    string installationKey = await PrepareRequiredGroupAsync(
                        target,
                        targetGroup.Name,
                        previousGroup,
                        progress,
                        cancellationToken).ConfigureAwait(false);
                    nextGroups.Add(
                        new PackageGroupState(
                            targetGroup.Name,
                            PackageGroupStatus.Ready,
                            targetManifestHash,
                            installationKey));
                    continue;
                }

                nextGroups.Add(
                    await TransitionOptionalGroupAsync(
                        target,
                        targetManifestHash,
                        targetGroup.Name,
                        previousGroup,
                        cancellationToken).ConfigureAwait(false));
            }

            long candidateRevision = snapshot.State?.StateRevision ?? 1;
            var candidateState = new PackageState(
                PackageState.CurrentSchemaVersion,
                candidateRevision,
                target.PackageId,
                new PackageActiveState(target.DataVersion, targetManifestHash),
                nextGroups);
            EnsureValidState(candidateState, target);

            if (snapshot.State != null && StateContentsEqual(snapshot.State, candidateState))
            {
                return new PreparedState(snapshot.State, requiresCommit: false);
            }

            var nextState = snapshot.State == null
                ? candidateState
                : new PackageState(
                    PackageState.CurrentSchemaVersion,
                    NextRevision(snapshot.State),
                    target.PackageId,
                    candidateState.Active,
                    candidateState.Groups);
            return new PreparedState(nextState, requiresCommit: true);
        }

        private async Task<PreparedState> PrepareOptionalAsync(
            StateSnapshot snapshot,
            IReadOnlyCollection<string> requestedGroups,
            IProgress<PatchProgress>? progress,
            CancellationToken cancellationToken)
        {
            PackageState current = snapshot.State!;
            ReleaseManifest manifest = snapshot.Manifest!;
            var requested = new HashSet<string>(requestedGroups, StringComparer.Ordinal);
            var currentGroups = current.Groups.ToDictionary(group => group.Name, StringComparer.Ordinal);
            var nextGroups = new List<PackageGroupState>(manifest.Groups.Count);

            progress?.Report(new PatchProgress(PatchStage.Planning));

            foreach (ManifestGroupEntry manifestGroup in manifest.Groups)
            {
                PackageGroupState currentGroup = currentGroups[manifestGroup.Name];

                if (!requested.Contains(manifestGroup.Name))
                {
                    nextGroups.Add(currentGroup);
                    continue;
                }

                EnsureCodecSupport(
                    manifest,
                    manifest.Files.Where(file => file.Group == manifestGroup.Name).ToArray(),
                    manifestGroup.Name);

                if (currentGroup.Status == PackageGroupStatus.Ready)
                {
                    nextGroups.Add(currentGroup);
                    continue;
                }

                string? reusableInstallation = await FindMatchingInstallationAsync(
                    manifest,
                    manifestGroup.Name,
                    currentGroup,
                    cancellationToken).ConfigureAwait(false);
                string installationKey = reusableInstallation
                    ?? await StageGroupAsync(
                        manifest,
                        manifestGroup.Name,
                        currentGroup,
                        progress,
                        cancellationToken).ConfigureAwait(false);
                nextGroups.Add(
                    new PackageGroupState(
                        manifestGroup.Name,
                        PackageGroupStatus.Ready,
                        current.Active.ManifestHash,
                        installationKey));
            }

            var candidateState = new PackageState(
                PackageState.CurrentSchemaVersion,
                current.StateRevision,
                current.PackageId,
                current.Active,
                nextGroups);
            EnsureValidState(candidateState, manifest);

            if (StateContentsEqual(current, candidateState))
            {
                return new PreparedState(current, requiresCommit: false);
            }

            var nextState = new PackageState(
                PackageState.CurrentSchemaVersion,
                NextRevision(current),
                current.PackageId,
                current.Active,
                candidateState.Groups);
            return new PreparedState(nextState, requiresCommit: true);
        }

        private async Task<string> PrepareRequiredGroupAsync(
            ReleaseManifest target,
            string group,
            PackageGroupState? previousGroup,
            IProgress<PatchProgress>? progress,
            CancellationToken cancellationToken)
        {
            EnsureCodecSupport(
                target,
                target.Files.Where(file => file.Group == group).ToArray(),
                group);
            string? reusableInstallation = await FindMatchingInstallationAsync(
                target,
                group,
                previousGroup,
                cancellationToken).ConfigureAwait(false);

            if (reusableInstallation != null)
            {
                return reusableInstallation;
            }

            return await StageGroupAsync(
                target,
                group,
                previousGroup,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task<PackageGroupState> TransitionOptionalGroupAsync(
            ReleaseManifest target,
            string targetManifestHash,
            string group,
            PackageGroupState? previousGroup,
            CancellationToken cancellationToken)
        {
            if (previousGroup == null || previousGroup.Status == PackageGroupStatus.NotInstalled)
            {
                return new PackageGroupState(group, PackageGroupStatus.NotInstalled);
            }

            string? reusableInstallation = await FindMatchingInstallationAsync(
                target,
                group,
                previousGroup,
                cancellationToken).ConfigureAwait(false);

            if (reusableInstallation != null)
            {
                return new PackageGroupState(
                    group,
                    PackageGroupStatus.Ready,
                    targetManifestHash,
                    reusableInstallation);
            }

            // Stale means "still valid for an earlier manifest". That claim is false when the manifest this
            // data was last verified against is the one being activated - the data just failed verification
            // against exactly that manifest. PackageStateValidator rejects such a group, so keeping stale
            // here made a rollback to the release the data was verified against fail with StateInvalid every
            // time, with no way forward. The group simply is not installed.
            if (previousGroup.VerifiedManifestHash == targetManifestHash)
            {
                return new PackageGroupState(group, PackageGroupStatus.NotInstalled);
            }

            return new PackageGroupState(
                group,
                PackageGroupStatus.Stale,
                previousGroup.VerifiedManifestHash,
                previousGroup.InstallationKey);
        }

        private async Task<string> StageGroupAsync(
            ReleaseManifest target,
            string group,
            PackageGroupState? previousGroup,
            IProgress<PatchProgress>? progress,
            CancellationToken cancellationToken)
        {
            List<ManifestFileEntry> targetFiles = target.Files
                .Where(file => file.Group == group)
                .ToList();
            var installedMatches = new Dictionary<string, LocalFileState>(StringComparer.Ordinal);
            if (previousGroup?.InstallationKey != null)
            {
                foreach (ManifestFileEntry file in targetFiles)
                {
                    if (await InstallationFileMatchesAsync(
                        previousGroup.InstallationKey,
                        file,
                        cancellationToken).ConfigureAwait(false))
                    {
                        installedMatches.Add(file.Path, new LocalFileState(file.Path, file.FileHash));
                    }
                }
            }

            DownloadPlan planWithoutCache = DownloadPlanner.Plan(
                target,
                new[] { group },
                installedMatches.Values,
                Array.Empty<CachedArtifactObject>());
            var validCache = new List<CachedArtifactObject>();

            foreach (PlannedArtifact planned in planWithoutCache.Artifacts)
            {
                foreach (ArtifactPayloadObject payload in planned.Artifact.GetPayloadObjects())
                {
                    if (await CachedObjectMatchesAsync(
                        target.PackageId,
                        payload,
                        cancellationToken).ConfigureAwait(false))
                    {
                        validCache.Add(new CachedArtifactObject(payload.Path, payload.ObjectHash));
                    }
                }
            }

            DownloadPlan plan = DownloadPlanner.Plan(
                target,
                new[] { group },
                installedMatches.Values,
                validCache);
            var tracker = new ProgressTracker(progress, plan, targetFiles.Count);

            foreach (PlannedArtifact planned in plan.Artifacts)
            {
                foreach (ArtifactPayloadObject payload in planned.ObjectsToDownload)
                {
                    await DownloadObjectAsync(
                        target.PackageId,
                        group,
                        payload,
                        tracker,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            await using IRuntimeStagingArea staging = await _storage
                .CreateStagingAreaAsync(target.PackageId, group, cancellationToken)
                .ConfigureAwait(false);
            var writtenPaths = new HashSet<string>(StringComparer.Ordinal);
            var plannedBundles = plan.Artifacts
                .Where(planned => planned.Artifact is ManifestArtifact.BundleArtifact)
                .Select(planned => ((ManifestArtifact.BundleArtifact)planned.Artifact).ArtifactHash)
                .ToHashSet(StringComparer.Ordinal);

            try
            {
                foreach (ManifestFileEntry file in targetFiles)
                {
                    if (file.Source is FileSource.BundleEntryReference bundleReference
                        && plannedBundles.Contains(bundleReference.ArtifactHash))
                    {
                        continue;
                    }

                    if (installedMatches.ContainsKey(file.Path))
                    {
                        await CopyInstalledFileAsync(
                            previousGroup!.InstallationKey!,
                            file,
                            staging,
                            writtenPaths,
                            tracker,
                            cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (file.Source is FileSource.FileReference)
                    {
                        ManifestArtifact.FileArtifact artifact = ResolveFileArtifact(target, file);
                        await MaterializeFileArtifactAsync(
                            target.PackageId,
                            file,
                            artifact,
                            staging,
                            writtenPaths,
                            tracker,
                            cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw Failure(
                        RuntimeErrorCodes.ArtifactCorrupted,
                        "A missing bundle entry has no planned bundle.",
                        target.PackageId,
                        file.Path,
                        group);
                }

                foreach (PlannedArtifact planned in plan.Artifacts)
                {
                    var bundle = planned.Artifact as ManifestArtifact.BundleArtifact;
                    if (bundle == null)
                    {
                        continue;
                    }

                    await ExtractBundleAsync(
                        target,
                        bundle,
                        staging,
                        writtenPaths,
                        cancellationToken).ConfigureAwait(false);

                    foreach (BundleEntry entry in bundle.Entries)
                    {
                        tracker.FileCompleted(group, entry.Path);
                    }
                }

                if (writtenPaths.Count != targetFiles.Count
                    || targetFiles.Any(file => !writtenPaths.Contains(file.Path)))
                {
                    throw Failure(
                        RuntimeErrorCodes.StagingFailed,
                        "The staged installation does not contain every target group file exactly once.",
                        target.PackageId,
                        group: group);
                }

                return await staging.PromoteAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (RuntimeException)
            {
                throw;
            }
            catch (InvalidDataException exception)
            {
                throw Failure(
                    RuntimeErrorCodes.ArtifactCorrupted,
                    "An artifact did not reconstruct the declared group files.",
                    target.PackageId,
                    group: group,
                    innerException: exception);
            }
            catch (Exception exception)
            {
                throw Failure(
                    RuntimeErrorCodes.StagingFailed,
                    "Group staging or immutable promotion failed.",
                    target.PackageId,
                    group: group,
                    innerException: exception);
            }
        }

        private async Task CopyInstalledFileAsync(
            string installationKey,
            ManifestFileEntry file,
            IRuntimeStagingArea staging,
            ISet<string> writtenPaths,
            ProgressTracker tracker,
            CancellationToken cancellationToken)
        {
            if (!writtenPaths.Add(file.Path))
            {
                throw new InvalidDataException($"File '{file.Path}' was staged more than once.");
            }

            await using Stream source = await _storage
                .OpenInstallationFileAsync(installationKey, file.Path, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException($"Installation file '{file.Path}' disappeared.");
            await using Stream destination = await staging
                .CreateFileAsync(file.Path, cancellationToken)
                .ConfigureAwait(false);
            await CopyVerifiedFileAsync(source, destination, file, cancellationToken).ConfigureAwait(false);
            tracker.FileCompleted(file.Group, file.Path);
        }

        private async Task MaterializeFileArtifactAsync(
            string packageId,
            ManifestFileEntry file,
            ManifestArtifact.FileArtifact artifact,
            IRuntimeStagingArea staging,
            ISet<string> writtenPaths,
            ProgressTracker tracker,
            CancellationToken cancellationToken)
        {
            if (!writtenPaths.Add(file.Path))
            {
                throw new InvalidDataException($"File '{file.Path}' was staged more than once.");
            }

            await using Stream payloadStream = await OpenFilePayloadAsync(
                packageId,
                file,
                artifact,
                cancellationToken).ConfigureAwait(false);
            using var verifiedPayload = new RuntimeHashingReadStream(payloadStream);
            await using Stream destination = await staging
                .CreateFileAsync(file.Path, cancellationToken)
                .ConfigureAwait(false);
            using var verifiedFile = new RuntimeHashingWriteStream(destination, file.Size);

            if (artifact.Compression == CompressionKind.None)
            {
                await verifiedPayload
                    .CopyToAsync(verifiedFile, StreamBufferSize, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                ICompressionCodec codec = GetZstdCodec(packageId, file.Path, file.Group);
                await codec
                    .DecompressAsync(verifiedPayload, verifiedFile, cancellationToken)
                    .ConfigureAwait(false);
            }

            await verifiedFile.FlushAsync(cancellationToken).ConfigureAwait(false);
            long expectedPayloadSize = SumPayloadSize(artifact.GetPayloadObjects());
            string actualPayloadHash = verifiedPayload.FinalizeHash();
            string actualFileHash = verifiedFile.FinalizeHash();

            if (verifiedPayload.BytesRead != expectedPayloadSize
                || actualPayloadHash != artifact.PrimaryArtifactHash
                || verifiedFile.BytesWritten != file.Size
                || actualFileHash != file.FileHash)
            {
                throw Failure(
                    RuntimeErrorCodes.ArtifactCorrupted,
                    "A file artifact did not reconstruct its declared source file.",
                    packageId,
                    file.Path,
                    file.Group);
            }

            tracker.FileCompleted(file.Group, file.Path);
        }

        private async Task<Stream> OpenFilePayloadAsync(
            string packageId,
            ManifestFileEntry file,
            ManifestArtifact.FileArtifact artifact,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<ArtifactPayloadObject> payloads = artifact.GetPayloadObjects();

            if (payloads.Count == 1)
            {
                return await _storage
                    .OpenCachedArtifactAsync(
                        packageId,
                        payloads[0].Path,
                        cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw Failure(
                        RuntimeErrorCodes.ArtifactCorrupted,
                        "A verified cache object disappeared before staging.",
                        packageId,
                        file.Path,
                        file.Group);
            }

            Stream scratch = await _storage
                .CreateScratchStreamAsync(packageId, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                if (!scratch.CanRead || !scratch.CanWrite || !scratch.CanSeek)
                {
                    throw new InvalidOperationException(
                        "Runtime scratch streams must be readable, writable, and seekable.");
                }

                scratch.SetLength(0);
                scratch.Position = 0;
                long expectedSize = SumPayloadSize(payloads);
                using (var combinedPayload = new RuntimeHashingWriteStream(scratch, expectedSize))
                {
                    foreach (ArtifactPayloadObject payload in payloads)
                    {
                        await using Stream part = await _storage
                            .OpenCachedArtifactAsync(
                                packageId,
                                payload.Path,
                                cancellationToken)
                            .ConfigureAwait(false)
                            ?? throw Failure(
                                RuntimeErrorCodes.ArtifactCorrupted,
                                "A verified cache part disappeared before staging.",
                                packageId,
                                file.Path,
                                file.Group);
                        await part
                            .CopyToAsync(
                                combinedPayload,
                                StreamBufferSize,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }

                    await combinedPayload
                        .FlushAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (combinedPayload.BytesWritten != expectedSize
                        || combinedPayload.FinalizeHash() != artifact.PrimaryArtifactHash)
                    {
                        throw new InvalidDataException(
                            "Multipart cache objects do not reconstruct their artifactHash.");
                    }
                }

                scratch.Position = 0;
                return scratch;
            }
            catch
            {
                await scratch.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private async Task ExtractBundleAsync(
            ReleaseManifest target,
            ManifestArtifact.BundleArtifact bundle,
            IRuntimeStagingArea staging,
            ISet<string> writtenPaths,
            CancellationToken cancellationToken)
        {
            ArtifactPayloadObject payload = bundle.GetPayloadObjects()[0];
            Stream? storedStream = await _storage
                .OpenCachedArtifactAsync(target.PackageId, payload.Path, cancellationToken)
                .ConfigureAwait(false);
            if (storedStream == null)
            {
                throw Failure(
                    RuntimeErrorCodes.ArtifactCorrupted,
                    "A verified bundle cache object disappeared before staging.",
                    target.PackageId,
                    group: bundle.Group);
            }

            await using (storedStream)
            using (var verifiedStored = new RuntimeHashingReadStream(storedStream))
            {
                Dictionary<string, ManifestFileEntry> filesByPath = target.Files
                    .Where(
                        file => file.Group == bundle.Group
                            && file.Source is FileSource.BundleEntryReference reference
                            && reference.ArtifactHash == bundle.ArtifactHash)
                    .ToDictionary(
                        file => ((FileSource.BundleEntryReference)file.Source).EntryPath,
                        StringComparer.Ordinal);
                long expectedTarSize = BundleArchiveExtractor.ComputeArchiveSize(bundle, filesByPath);

                if (bundle.Compression == CompressionKind.None)
                {
                    if (bundle.Size != expectedTarSize)
                    {
                        throw new InvalidDataException("Bundle size does not match its canonical tar size.");
                    }

                    await BundleArchiveExtractor.ExtractAsync(
                        verifiedStored,
                        bundle,
                        filesByPath,
                        staging,
                        writtenPaths,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await using Stream scratch = await _storage
                        .CreateScratchStreamAsync(target.PackageId, cancellationToken)
                        .ConfigureAwait(false);
                    if (!scratch.CanRead || !scratch.CanWrite || !scratch.CanSeek)
                    {
                        throw new InvalidOperationException("Runtime scratch streams must be readable, writable, and seekable.");
                    }

                    scratch.SetLength(0);
                    scratch.Position = 0;
                    using (var verifiedTar = new RuntimeHashingWriteStream(scratch, expectedTarSize))
                    {
                        ICompressionCodec codec = GetZstdCodec(
                            target.PackageId,
                            relativePath: null,
                            bundle.Group);
                        await codec
                            .DecompressAsync(verifiedStored, verifiedTar, cancellationToken)
                            .ConfigureAwait(false);
                        await verifiedTar.FlushAsync(cancellationToken).ConfigureAwait(false);

                        if (verifiedTar.BytesWritten != expectedTarSize)
                        {
                            throw new InvalidDataException("Decompressed bundle has an unexpected length.");
                        }
                    }

                    scratch.Position = 0;
                    await BundleArchiveExtractor.ExtractAsync(
                        scratch,
                        bundle,
                        filesByPath,
                        staging,
                        writtenPaths,
                        cancellationToken).ConfigureAwait(false);
                }

                if (verifiedStored.BytesRead != bundle.Size
                    || verifiedStored.FinalizeHash() != bundle.ArtifactHash)
                {
                    throw new InvalidDataException("Stored bundle bytes failed verification.");
                }
            }
        }

        private async Task DownloadObjectAsync(
            string packageId,
            string group,
            ArtifactPayloadObject payload,
            ProgressTracker tracker,
            CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < MaximumAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await using IRuntimeCacheWriter writer = await _storage
                        .CreateCacheWriterAsync(packageId, payload.Path, cancellationToken)
                        .ConfigureAwait(false);
                    await using Stream source = await _transport
                        .OpenArtifactAsync(packageId, payload.Path, cancellationToken)
                        .ConfigureAwait(false);
                    using var verifiedDestination = new RuntimeHashingWriteStream(
                        writer.Content,
                        payload.Size);
                    var buffer = new byte[StreamBufferSize];
                    int read;

                    while ((read = await source
                        .ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                        .ConfigureAwait(false)) > 0)
                    {
                        await verifiedDestination
                            .WriteAsync(buffer, 0, read, cancellationToken)
                            .ConfigureAwait(false);
                        tracker.Downloaded(group, payload.Path, read, attempt);
                    }

                    await verifiedDestination.FlushAsync(cancellationToken).ConfigureAwait(false);

                    if (verifiedDestination.BytesWritten != payload.Size
                        || verifiedDestination.FinalizeHash() != payload.ObjectHash)
                    {
                        throw Failure(
                            RuntimeErrorCodes.ArtifactCorrupted,
                            "Downloaded artifact object failed size or SHA-256 verification.",
                            packageId,
                            payload.Path,
                            group);
                    }

                    await writer.CommitAsync(cancellationToken).ConfigureAwait(false);

                    if (!await CachedObjectMatchesAsync(
                        packageId,
                        payload,
                        cancellationToken).ConfigureAwait(false))
                    {
                        throw Failure(
                            RuntimeErrorCodes.ArtifactCorrupted,
                            "Committed cache object failed verification.",
                            packageId,
                            payload.Path,
                            group);
                    }

                    return;
                }
                catch (ArtifactTransportException exception)
                    when (exception.IsTransient && attempt + 1 < MaximumAttempts)
                {
                    tracker.Retry(group, payload.Path, attempt + 1);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (RuntimeException)
                {
                    throw;
                }
                catch (InvalidDataException exception)
                {
                    throw Failure(
                        RuntimeErrorCodes.ArtifactCorrupted,
                        "Downloaded artifact object exceeded or violated its declared payload.",
                        packageId,
                        payload.Path,
                        group,
                        exception);
                }
                catch (ArtifactTransportException exception)
                {
                    throw Failure(
                        RuntimeErrorCodes.TransportFailed,
                        "Artifact transport failed.",
                        packageId,
                        payload.Path,
                        group,
                        exception);
                }
                catch (IOException exception)
                {
                    throw Failure(
                        RuntimeErrorCodes.ArtifactCorrupted,
                        "The verified cache object could not be written or committed.",
                        packageId,
                        payload.Path,
                        group,
                        exception);
                }
            }

            throw Failure(
                RuntimeErrorCodes.TransportFailed,
                "Artifact transport retry limit was reached.",
                packageId,
                payload.Path,
                group);
        }

        private async Task<bool> CachedObjectMatchesAsync(
            string packageId,
            ArtifactPayloadObject payload,
            CancellationToken cancellationToken)
        {
            try
            {
                await using Stream? stream = await _storage
                    .OpenCachedArtifactAsync(packageId, payload.Path, cancellationToken)
                    .ConfigureAwait(false);
                return stream != null
                    && await StreamMatchesAsync(
                        stream,
                        payload.Size,
                        payload.ObjectHash,
                        cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return false;
            }
        }

        private async Task<string?> FindMatchingInstallationAsync(
            ReleaseManifest target,
            string group,
            PackageGroupState? previousGroup,
            CancellationToken cancellationToken)
        {
            if (previousGroup?.InstallationKey == null
                || !await _storage
                    .InstallationExistsAsync(previousGroup.InstallationKey, cancellationToken)
                    .ConfigureAwait(false))
            {
                return null;
            }

            string[] targetPaths = target.Files
                .Where(file => file.Group == group)
                .Select(file => file.Path)
                .ToArray();
            if (!await InstallationPathsMatchAsync(
                previousGroup.InstallationKey,
                targetPaths,
                cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            foreach (ManifestFileEntry file in target.Files)
            {
                if (file.Group == group
                    && !await InstallationFileMatchesAsync(
                        previousGroup.InstallationKey,
                        file,
                        cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }
            }

            return previousGroup.InstallationKey;
        }

        private async Task<bool> InstallationFileMatchesAsync(
            string installationKey,
            ManifestFileEntry file,
            CancellationToken cancellationToken)
        {
            try
            {
                await using Stream? stream = await _storage
                    .OpenInstallationFileAsync(
                        installationKey,
                        file.Path,
                        cancellationToken)
                    .ConfigureAwait(false);
                return stream != null
                    && await StreamMatchesAsync(
                        stream,
                        file.Size,
                        file.FileHash,
                        cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return false;
            }
        }

        private async Task<bool> InstallationPathsMatchAsync(
            string installationKey,
            IReadOnlyCollection<string> targetPaths,
            CancellationToken cancellationToken)
        {
            try
            {
                IReadOnlyList<string> installationPaths = await _storage
                    .GetInstallationFilePathsAsync(
                        installationKey,
                        cancellationToken)
                    .ConfigureAwait(false);
                var installationPathSet = new HashSet<string>(
                    installationPaths,
                    StringComparer.Ordinal);

                return installationPathSet.Count == installationPaths.Count
                    && installationPathSet.Count == targetPaths.Count
                    && targetPaths.All(path => installationPathSet.Contains(path));
            }
            catch (IOException)
            {
                return false;
            }
        }

        // Reads at most the size the manifest declares, then looks for one more byte. Draining to EOF first
        // meant an oversized cache object - or an adapter handing back an endless stream - was read in full
        // before being called corrupt, and every state load and download plan goes through here.
        private static async Task<bool> StreamMatchesAsync(
            Stream source,
            long expectedSize,
            string expectedHash,
            CancellationToken cancellationToken)
        {
            using var verified = new RuntimeHashingReadStream(source);
            var buffer = new byte[StreamBufferSize];

            while (verified.BytesRead < expectedSize)
            {
                int wanted = (int)Math.Min(buffer.Length, expectedSize - verified.BytesRead);
                int read = await verified
                    .ReadAsync(buffer, 0, wanted, cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    return false;
                }
            }

            // Exactly one byte past the declared size is enough to know it is too long; nothing is gained by
            // reading the rest of it.
            if (await verified.ReadAsync(buffer, 0, 1, cancellationToken).ConfigureAwait(false) > 0)
            {
                return false;
            }

            return verified.FinalizeHash() == expectedHash;
        }

        private static async Task CopyVerifiedFileAsync(
            Stream source,
            Stream destination,
            ManifestFileEntry file,
            CancellationToken cancellationToken)
        {
            using var verifiedSource = new RuntimeHashingReadStream(source);
            using var verifiedDestination = new RuntimeHashingWriteStream(destination, file.Size);
            await verifiedSource
                .CopyToAsync(verifiedDestination, StreamBufferSize, cancellationToken)
                .ConfigureAwait(false);
            await verifiedDestination.FlushAsync(cancellationToken).ConfigureAwait(false);

            if (verifiedSource.BytesRead != file.Size
                || verifiedSource.FinalizeHash() != file.FileHash
                || verifiedDestination.BytesWritten != file.Size
                || verifiedDestination.FinalizeHash() != file.FileHash)
            {
                throw new InvalidDataException($"Installed file '{file.Path}' changed during staging.");
            }
        }

        private async Task<StateSnapshot> LoadStateAsync(
            string packageId,
            bool allowRecovery,
            CancellationToken cancellationToken)
        {
            byte[]? stateBytes = await _storage
                .ReadPackageStateAsync(packageId, cancellationToken)
                .ConfigureAwait(false);
            if (stateBytes == null)
            {
                return StateSnapshot.Absent();
            }

            if (!PackageStateSerializer.TryDeserialize(stateBytes, out PackageState? state, out _)
                || state!.PackageId != packageId)
            {
                if (allowRecovery)
                {
                    return StateSnapshot.Invalid(stateBytes);
                }

                throw Failure(
                    RuntimeErrorCodes.StateInvalid,
                    "PackageState bytes are corrupt or belong to another package.",
                    packageId);
            }

            var activeTarget = new TargetManifestReference(
                state.PackageId,
                state.Active.DataVersion,
                state.Active.ManifestHash);
            ReleaseManifest manifest;

            try
            {
                manifest = await LoadManifestAsync(
                    activeTarget,
                    progress: null,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (RuntimeException) when (allowRecovery)
            {
                // The active manifest is the only thing that can establish whether this state means anything,
                // so a state whose manifest cannot be fetched or validated is not evidence of anything -
                // whatever the reason. Recovering only from ManifestInvalid left a client permanently stuck
                // the moment its previous release's manifest was retired: the new target and its artifacts
                // were healthy, but reading the old manifest failed with TransportFailed and escaped, and
                // nothing short of deleting the local state could fix it.
                //
                // Safe because allowRecovery is only set after the new target has been validated, and every
                // installation the rebuild reuses is re-verified against that target before it is trusted.
                // The cost is that optional groups fall back to notInstalled and have to be reinstalled;
                // their bytes are left alone, and that is strictly better than being unable to update at all.
                return StateSnapshot.Invalid(stateBytes);
            }

            if (!PackageStateValidator.Validate(state, manifest).IsValid
                || !await StateInstallationsMatchAsync(
                    state,
                    manifest,
                    cancellationToken).ConfigureAwait(false))
            {
                if (allowRecovery)
                {
                    return StateSnapshot.Invalid(stateBytes);
                }

                throw Failure(
                    RuntimeErrorCodes.StateInvalid,
                    "PackageState invariants or installation references are invalid.",
                    packageId);
            }

            return StateSnapshot.Valid(stateBytes, state, manifest);
        }

        private async Task<bool> StateInstallationsMatchAsync(
            PackageState state,
            ReleaseManifest manifest,
            CancellationToken cancellationToken)
        {
            foreach (PackageGroupState group in state.Groups)
            {
                if (group.Status == PackageGroupStatus.NotInstalled)
                {
                    continue;
                }

                if (!await _storage
                    .InstallationExistsAsync(group.InstallationKey!, cancellationToken)
                    .ConfigureAwait(false))
                {
                    return false;
                }

                if (group.Status == PackageGroupStatus.Stale)
                {
                    continue;
                }

                string[] targetPaths = manifest.Files
                    .Where(file => file.Group == group.Name)
                    .Select(file => file.Path)
                    .ToArray();
                if (!await InstallationPathsMatchAsync(
                    group.InstallationKey!,
                    targetPaths,
                    cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }

                foreach (ManifestFileEntry file in manifest.Files)
                {
                    if (file.Group == group.Name
                        && !await InstallationFileMatchesAsync(
                            group.InstallationKey!,
                            file,
                            cancellationToken).ConfigureAwait(false))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private async Task<ReleaseManifest> LoadManifestAsync(
            TargetManifestReference target,
            IProgress<PatchProgress>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report(new PatchProgress(PatchStage.Manifest));
            byte[] manifestBytes = await ReadManifestBytesAsync(
                target,
                cancellationToken).ConfigureAwait(false);
            ValidationResult hashValidation = ReleaseIdentity.VerifyManifestHash(
                manifestBytes,
                target.ManifestHash);
            if (!hashValidation.IsValid)
            {
                throw Failure(
                    RuntimeErrorCodes.ManifestInvalid,
                    "Manifest bytes do not match the trusted target manifestHash.",
                    target.PackageId);
            }

            if (!TryReadJsonObject(manifestBytes, out JObject? json))
            {
                throw Failure(
                    RuntimeErrorCodes.ManifestInvalid,
                    "Manifest is not one strict UTF-8 JSON object.",
                    target.PackageId);
            }

            if (!ReleaseManifest.TryParse(
                json!,
                out ReleaseManifest? manifest,
                out IReadOnlyList<GamePatchKitError> parseErrors))
            {
                throw Failure(
                    RuntimeErrorCodes.ManifestInvalid,
                    parseErrors.Count == 0
                        ? "Manifest schema is invalid."
                        : parseErrors[0].Message,
                    target.PackageId);
            }

            byte[] canonicalBytes = ReleaseIdentity.ComputeCanonicalBytes(manifest!);
            ValidationResult semanticValidation = ManifestValidator.Validate(manifest!);
            if (!manifestBytes.SequenceEqual(canonicalBytes)
                || !semanticValidation.IsValid
                || manifest!.PackageId != target.PackageId
                || manifest.DataVersion != target.DataVersion
                || ReleaseIdentity.ComputeDataVersion(manifest) != manifest.DataVersion)
            {
                throw Failure(
                    RuntimeErrorCodes.ManifestInvalid,
                    "Manifest canonical bytes, identity, or semantic references are invalid.",
                    target.PackageId);
            }

            if (_trustedSigningKeys != null)
            {
                await VerifySignatureAsync(target, manifestBytes, cancellationToken).ConfigureAwait(false);
            }

            return manifest;
        }

        // Only reachable once a trust list is configured (see the constructor). Absence is tolerated unless
        // _requireSignature is set; anything present that is malformed, signed by a key outside the trust
        // list, or does not cryptographically check out is always rejected, trust list or not.
        private async Task VerifySignatureAsync(
            TargetManifestReference target,
            byte[] manifestBytes,
            CancellationToken cancellationToken)
        {
            byte[]? signatureBytes = await ReadManifestSignatureBytesAsync(target, cancellationToken).ConfigureAwait(false);

            if (signatureBytes == null)
            {
                if (_requireSignature)
                {
                    throw Failure(
                        RuntimeErrorCodes.SignatureInvalid,
                        "The release has no manifest signature, but a signature was required.",
                        target.PackageId);
                }

                return;
            }

            if (!TryReadJsonObject(signatureBytes, out JObject? json)
                || !ManifestSignature.TryParse(json!, out ManifestSignature? signature, out _)
                || !signatureBytes.SequenceEqual(CanonicalJsonWriter.Write(signature!.ToJson())))
            {
                throw Failure(
                    RuntimeErrorCodes.SignatureInvalid,
                    "The manifest signature document is invalid.",
                    target.PackageId);
            }

            if (!_trustedSigningKeys!.TryGetPublicKey(signature.KeyId, out byte[] publicKey)
                || !signature.TryGetSignatureBytes(out byte[] rawSignatureBytes)
                || !Ed25519Signatures.Verify(publicKey, manifestBytes, rawSignatureBytes))
            {
                throw Failure(
                    RuntimeErrorCodes.SignatureInvalid,
                    "The manifest signature is missing from the trusted key set or is not valid for the published manifest bytes.",
                    target.PackageId);
            }
        }

        // Only a transport failure the adapter positively confirms is absence (IsNotFound - a real HTTP 404,
        // say) is tolerated as "no signature was ever published"; _requireSignature is what turns that from
        // tolerated into a failure. Every other failure - a transient one that exhausts every retry, or a
        // non-transient one the adapter does not confirm as not-found (401, 403, an unrecognized response) -
        // is a genuine transport failure and must never be silently treated as evidence of an unsigned
        // release, regardless of _requireSignature: an adapter or intermediary that can make signature
        // requests fail for any other reason must not be able to downgrade a signed release to unsigned.
        private async Task<byte[]?> ReadManifestSignatureBytesAsync(
            TargetManifestReference target,
            CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < MaximumAttempts; attempt++)
            {
                try
                {
                    await using Stream source = await _transport
                        .OpenManifestSignatureAsync(target, cancellationToken)
                        .ConfigureAwait(false);

                    using var destination = new MemoryStream();
                    var buffer = new byte[StreamBufferSize];
                    int read;

                    while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        if (destination.Length + read > MaximumSignatureBytes)
                        {
                            throw Failure(
                                RuntimeErrorCodes.SignatureInvalid,
                                $"The manifest signature response exceeds the {MaximumSignatureBytes} byte limit.",
                                target.PackageId);
                        }

                        await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                    }

                    return destination.ToArray();
                }
                catch (ArtifactTransportException exception) when (exception.IsTransient && attempt + 1 < MaximumAttempts)
                {
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ArtifactTransportException exception) when (exception.IsNotFound)
                {
                    return null;
                }
                catch (ArtifactTransportException exception)
                {
                    throw Failure(
                        RuntimeErrorCodes.TransportFailed,
                        "Manifest signature transport failed and was not confirmed absent.",
                        target.PackageId,
                        innerException: exception);
                }
            }

            return null;
        }

        private async Task<byte[]> ReadManifestBytesAsync(
            TargetManifestReference target,
            CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < MaximumAttempts; attempt++)
            {
                try
                {
                    await using Stream source = await _transport
                        .OpenManifestAsync(target, cancellationToken)
                        .ConfigureAwait(false);

                    return await ReadBoundedAsync(source, target, cancellationToken).ConfigureAwait(false);
                }
                catch (ArtifactTransportException exception)
                    when (exception.IsTransient && attempt + 1 < MaximumAttempts)
                {
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ArtifactTransportException exception)
                {
                    throw Failure(
                        RuntimeErrorCodes.TransportFailed,
                        "Manifest transport failed.",
                        target.PackageId,
                        innerException: exception);
                }
            }

            throw Failure(
                RuntimeErrorCodes.TransportFailed,
                "Manifest transport retry limit was reached.",
                target.PackageId);
        }

        // Stops at the first byte past the limit rather than after the copy, so an endless response is
        // abandoned instead of read to completion. Not retried: a response too large to be this manifest is a
        // broken endpoint, not a transient failure, and retrying would just spend the limit twice more.
        private static async Task<byte[]> ReadBoundedAsync(
            Stream source,
            TargetManifestReference target,
            CancellationToken cancellationToken)
        {
            using var destination = new MemoryStream();
            var buffer = new byte[StreamBufferSize];
            int read;

            while ((read = await source
                .ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                .ConfigureAwait(false)) > 0)
            {
                if (destination.Length + read > MaximumManifestBytes)
                {
                    throw Failure(
                        RuntimeErrorCodes.ManifestInvalid,
                        $"The manifest response exceeds the {MaximumManifestBytes} byte limit.",
                        target.PackageId);
                }

                await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
            }

            return destination.ToArray();
        }

        private async Task<bool> TryCommitAsync(
            StateSnapshot snapshot,
            PackageState nextState,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using IAsyncDisposable writerLock = await _storage
                    .AcquirePackageWriterLockAsync(
                        nextState.PackageId,
                        cancellationToken)
                    .ConfigureAwait(false);
                byte[]? currentBytes = await _storage
                    .ReadPackageStateAsync(
                        nextState.PackageId,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!snapshot.Matches(currentBytes))
                {
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();
                await _storage
                    .ReplacePackageStateAsync(
                        nextState.PackageId,
                        PackageStateSerializer.Serialize(nextState),
                        cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (RuntimeException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw Failure(
                    RuntimeErrorCodes.ActivationFailed,
                    "Package writer lock or atomic PackageState replacement failed.",
                    nextState.PackageId,
                    innerException: exception);
            }
        }

        private void EnsureCodecSupport(
            ReleaseManifest target,
            IReadOnlyCollection<ManifestFileEntry> files,
            string group)
        {
            var artifactHashes = new HashSet<string>(StringComparer.Ordinal);

            foreach (ManifestFileEntry file in files)
            {
                if (file.Source is FileSource.FileReference fileReference)
                {
                    artifactHashes.Add("file:" + fileReference.ArtifactHash);
                }
                else
                {
                    artifactHashes.Add(
                        "bundle:" + ((FileSource.BundleEntryReference)file.Source).ArtifactHash);
                }
            }

            foreach (ManifestArtifact artifact in target.Artifacts)
            {
                if (!ArtifactIsSelected(artifact, artifactHashes)
                    || ArtifactCompression(artifact) != CompressionKind.Zstd)
                {
                    continue;
                }

                if (!_codecs.TryGetValue(CompressionCodecIds.Zstd, out ICompressionCodec? codec)
                    || codec.CodecId != CompressionCodecIds.Zstd)
                {
                    throw Failure(
                        RuntimeErrorCodes.MissingCompressionCodec,
                        "The target group requires zstd, but no compatible codec was supplied.",
                        target.PackageId,
                        group: group);
                }
            }
        }

        private static bool ArtifactIsSelected(
            ManifestArtifact artifact,
            ISet<string> selectedArtifactHashes)
        {
            return artifact is ManifestArtifact.FileArtifact fileArtifact
                ? selectedArtifactHashes.Contains("file:" + fileArtifact.PrimaryArtifactHash)
                : selectedArtifactHashes.Contains(
                    "bundle:" + ((ManifestArtifact.BundleArtifact)artifact).ArtifactHash);
        }

        private static CompressionKind ArtifactCompression(ManifestArtifact artifact)
        {
            return artifact is ManifestArtifact.FileArtifact fileArtifact
                ? fileArtifact.Compression
                : ((ManifestArtifact.BundleArtifact)artifact).Compression;
        }

        private ICompressionCodec GetZstdCodec(
            string packageId,
            string? relativePath,
            string group)
        {
            if (_codecs.TryGetValue(CompressionCodecIds.Zstd, out ICompressionCodec? codec)
                && codec.CodecId == CompressionCodecIds.Zstd)
            {
                return codec;
            }

            throw Failure(
                RuntimeErrorCodes.MissingCompressionCodec,
                "The artifact requires zstd, but no compatible codec was supplied.",
                packageId,
                relativePath,
                group);
        }

        private static ManifestArtifact.FileArtifact ResolveFileArtifact(
            ReleaseManifest target,
            ManifestFileEntry file)
        {
            string hash = ((FileSource.FileReference)file.Source).ArtifactHash;

            foreach (ManifestArtifact artifact in target.Artifacts)
            {
                if (artifact is ManifestArtifact.FileArtifact fileArtifact
                    && fileArtifact.PrimaryArtifactHash == hash)
                {
                    return fileArtifact;
                }
            }

            throw new InvalidDataException($"File '{file.Path}' has no file artifact.");
        }

        private static long SumPayloadSize(IReadOnlyList<ArtifactPayloadObject> payloads)
        {
            long total = 0;

            foreach (ArtifactPayloadObject payload in payloads)
            {
                total = checked(total + payload.Size);
            }

            return total;
        }

        private static long NextRevision(PackageState state)
        {
            if (state.StateRevision >= JsonNumbers.MaxSafeInteger)
            {
                throw Failure(
                    RuntimeErrorCodes.StateInvalid,
                    "PackageState stateRevision reached the I-JSON safe-integer limit.",
                    state.PackageId);
            }

            return state.StateRevision + 1;
        }

        private static void ValidateOptionalGroupRequest(
            ReleaseManifest manifest,
            IReadOnlyCollection<string> groupNames)
        {
            var groups = manifest.Groups.ToDictionary(group => group.Name, StringComparer.Ordinal);

            foreach (string groupName in groupNames)
            {
                if (!groups.TryGetValue(groupName, out ManifestGroupEntry? group))
                {
                    throw new ArgumentException(
                        $"Optional group '{groupName}' is not declared by the active manifest.",
                        nameof(groupNames));
                }

                if (group.Required)
                {
                    throw new ArgumentException(
                        $"Group '{groupName}' is required and cannot be installed as optional.",
                        nameof(groupNames));
                }
            }
        }

        private static void EnsureValidState(
            PackageState state,
            ReleaseManifest manifest)
        {
            ValidationResult validation = PackageStateValidator.Validate(state, manifest);
            if (!validation.IsValid)
            {
                throw Failure(
                    RuntimeErrorCodes.StateInvalid,
                    validation.Errors[0].Message,
                    state.PackageId);
            }
        }

        private static bool StateContentsEqual(PackageState left, PackageState right)
        {
            if (left.SchemaVersion != right.SchemaVersion
                || left.PackageId != right.PackageId
                || left.Active.DataVersion != right.Active.DataVersion
                || left.Active.ManifestHash != right.Active.ManifestHash
                || left.Groups.Count != right.Groups.Count)
            {
                return false;
            }

            for (int index = 0; index < left.Groups.Count; index++)
            {
                PackageGroupState leftGroup = left.Groups[index];
                PackageGroupState rightGroup = right.Groups[index];

                if (leftGroup.Name != rightGroup.Name
                    || leftGroup.Status != rightGroup.Status
                    || leftGroup.VerifiedManifestHash != rightGroup.VerifiedManifestHash
                    || leftGroup.InstallationKey != rightGroup.InstallationKey)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryReadJsonObject(byte[] bytes, out JObject? json)
        {
            json = null;

            try
            {
                string text = _strictUtf8.GetString(bytes);
                using var textReader = new StringReader(text);
                using var reader = new JsonTextReader(textReader)
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Decimal,
                    SupportMultipleContent = false,
                };
                JToken token = JToken.ReadFrom(
                    reader,
                    new JsonLoadSettings
                    {
                        CommentHandling = CommentHandling.Ignore,
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    });

                if (token.Type != JTokenType.Object || reader.Read())
                {
                    return false;
                }

                json = (JObject)token;
                return true;
            }
            catch (Exception exception) when (
                exception is DecoderFallbackException
                || exception is JsonException
                || exception is InvalidOperationException)
            {
                return false;
            }
        }

        private static RuntimeException Failure(
            string code,
            string message,
            string? packageId = null,
            string? relativePath = null,
            string? group = null,
            Exception? innerException = null)
        {
            var error = new GamePatchKitError(
                Stage,
                code,
                message,
                packageId,
                relativePath,
                group);
            return innerException == null
                ? new RuntimeException(error)
                : new RuntimeException(error, innerException);
        }

        private sealed class PreparedState
        {
            public PackageState State { get; }

            public bool RequiresCommit { get; }

            public PreparedState(PackageState state, bool requiresCommit)
            {
                State = state;
                RequiresCommit = requiresCommit;
            }
        }

        private sealed class StateSnapshot
        {
            public byte[]? RawBytes { get; }

            public PackageState? State { get; }

            public ReleaseManifest? Manifest { get; }

            private StateSnapshot(
                byte[]? rawBytes,
                PackageState? state,
                ReleaseManifest? manifest)
            {
                RawBytes = rawBytes;
                State = state;
                Manifest = manifest;
            }

            public static StateSnapshot Absent()
            {
                return new StateSnapshot(rawBytes: null, state: null, manifest: null);
            }

            public static StateSnapshot Invalid(byte[] rawBytes)
            {
                return new StateSnapshot(rawBytes, state: null, manifest: null);
            }

            public static StateSnapshot Valid(
                byte[] rawBytes,
                PackageState state,
                ReleaseManifest manifest)
            {
                return new StateSnapshot(rawBytes, state, manifest);
            }

            public bool Matches(byte[]? currentBytes)
            {
                if (State == null)
                {
                    return RawBytes == null
                        ? currentBytes == null
                        : currentBytes != null && RawBytes.SequenceEqual(currentBytes);
                }

                if (currentBytes == null
                    || !PackageStateSerializer.TryDeserialize(
                        currentBytes,
                        out PackageState? current,
                        out _))
                {
                    return false;
                }

                return current!.StateRevision == State.StateRevision
                    && current.PackageId == State.PackageId
                    && current.Active.DataVersion == State.Active.DataVersion
                    && current.Active.ManifestHash == State.Active.ManifestHash;
            }
        }

        private sealed class ProgressTracker
        {
            private readonly IProgress<PatchProgress>? _progress;
            private readonly long _totalBytes;
            private readonly int _totalFiles;
            private long _completedBytes;
            private int _completedFiles;

            public ProgressTracker(
                IProgress<PatchProgress>? progress,
                DownloadPlan plan,
                int totalFiles)
            {
                _progress = progress;
                _totalBytes = plan.EstimatedDownloadBytes;
                _totalFiles = totalFiles;
            }

            public void Downloaded(
                string group,
                string relativePath,
                int byteCount,
                int retryCount)
            {
                _completedBytes += byteCount;
                _progress?.Report(
                    new PatchProgress(
                        PatchStage.Downloading,
                        group,
                        relativePath,
                        _completedFiles,
                        _totalFiles,
                        _completedBytes,
                        _totalBytes,
                        retryCount));
            }

            public void Retry(
                string group,
                string relativePath,
                int retryCount)
            {
                _progress?.Report(
                    new PatchProgress(
                        PatchStage.Downloading,
                        group,
                        relativePath,
                        _completedFiles,
                        _totalFiles,
                        _completedBytes,
                        _totalBytes,
                        retryCount));
            }

            public void FileCompleted(string group, string relativePath)
            {
                _completedFiles++;
                _progress?.Report(
                    new PatchProgress(
                        PatchStage.Staging,
                        group,
                        relativePath,
                        _completedFiles,
                        _totalFiles,
                        _completedBytes,
                        _totalBytes));
            }
        }
    }
}
