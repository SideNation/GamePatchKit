using System;
using System.Collections.Generic;
using GamePatchKit.Core;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Core.Paths;

namespace GamePatchKit.Runtime
{
    public static class PackageStateValidator
    {
        private const string Stage = "package-state";

        public static ValidationResult Validate(PackageState state, ReleaseManifest activeManifest)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (activeManifest == null)
            {
                throw new ArgumentNullException(nameof(activeManifest));
            }

            var errors = new List<GamePatchKitError>();

            if (state.SchemaVersion != PackageState.CurrentSchemaVersion)
            {
                errors.Add(Error("Unsupported PackageState schemaVersion.", state.PackageId));
            }

            if (state.StateRevision < 1 || state.StateRevision > JsonNumbers.MaxSafeInteger)
            {
                errors.Add(Error("stateRevision must be a positive I-JSON safe integer.", state.PackageId));
            }

            if (state.PackageId != activeManifest.PackageId)
            {
                errors.Add(Error("PackageState packageId does not match its active manifest.", state.PackageId));
            }

            if (state.Active.DataVersion != activeManifest.DataVersion)
            {
                errors.Add(Error("PackageState dataVersion does not match its active manifest.", state.PackageId));
            }

            string activeManifestHash = ReleaseIdentity.ComputeManifestHash(
                ReleaseIdentity.ComputeCanonicalBytes(activeManifest));

            if (state.Active.ManifestHash != activeManifestHash)
            {
                errors.Add(Error("PackageState manifestHash does not match its active manifest.", state.PackageId));
            }

            ValidateGroups(state, activeManifest, errors);
            return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
        }

        private static void ValidateGroups(
            PackageState state,
            ReleaseManifest activeManifest,
            List<GamePatchKitError> errors)
        {
            var manifestGroups = new Dictionary<string, ManifestGroupEntry>(StringComparer.Ordinal);

            foreach (ManifestGroupEntry group in activeManifest.Groups)
            {
                manifestGroups.Add(group.Name, group);
            }

            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            string? previousName = null;

            foreach (PackageGroupState group in state.Groups)
            {
                if (!seenNames.Add(group.Name))
                {
                    errors.Add(Error($"Group '{group.Name}' appears more than once.", state.PackageId, group.Name));
                }

                if (previousName != null
                    && Utf8OrdinalStringComparer.Instance.Compare(previousName, group.Name) > 0)
                {
                    errors.Add(Error("groups[] is not in ordinal UTF-8 byte order.", state.PackageId));
                }

                previousName = group.Name;

                if (!manifestGroups.TryGetValue(group.Name, out ManifestGroupEntry? manifestGroup))
                {
                    errors.Add(Error($"Unknown group '{group.Name}'.", state.PackageId, group.Name));
                    continue;
                }

                ValidateGroup(state, manifestGroup, group, errors);
            }

            if (seenNames.Count != manifestGroups.Count)
            {
                errors.Add(Error("groups[] must contain every active manifest group exactly once.", state.PackageId));
            }
        }

        private static void ValidateGroup(
            PackageState state,
            ManifestGroupEntry manifestGroup,
            PackageGroupState group,
            List<GamePatchKitError> errors)
        {
            if (manifestGroup.Required && group.Status != PackageGroupStatus.Ready)
            {
                errors.Add(Error("Required groups must be ready.", state.PackageId, group.Name));
            }

            if (manifestGroup.Required
                && (group.Status == PackageGroupStatus.NotInstalled || group.Status == PackageGroupStatus.Stale))
            {
                errors.Add(Error("notInstalled and stale are allowed only for optional groups.", state.PackageId, group.Name));
            }

            if (group.Status == PackageGroupStatus.NotInstalled)
            {
                if (group.VerifiedManifestHash != null || group.InstallationKey != null)
                {
                    errors.Add(Error("notInstalled groups must not have verification fields.", state.PackageId, group.Name));
                }

                return;
            }

            if (!Hex64.IsValid(group.VerifiedManifestHash!)
                || string.IsNullOrWhiteSpace(group.InstallationKey)
                || LooksLikeAbsolutePath(group.InstallationKey!))
            {
                errors.Add(Error("Installed groups require a valid hash and opaque non-absolute installationKey.", state.PackageId, group.Name));
                return;
            }

            if (group.Status == PackageGroupStatus.Ready
                && group.VerifiedManifestHash != state.Active.ManifestHash)
            {
                errors.Add(Error("ready groups must be verified against the active manifest.", state.PackageId, group.Name));
            }

            if (group.Status == PackageGroupStatus.Stale
                && group.VerifiedManifestHash == state.Active.ManifestHash)
            {
                errors.Add(Error("stale groups must refer to a previous manifest.", state.PackageId, group.Name));
            }
        }

        private static bool LooksLikeAbsolutePath(string installationKey)
        {
            return installationKey.StartsWith("/", StringComparison.Ordinal)
                || installationKey.StartsWith("\\", StringComparison.Ordinal)
                || (installationKey.Length >= 3
                    && char.IsLetter(installationKey[0])
                    && installationKey[1] == ':'
                    && (installationKey[2] == '/' || installationKey[2] == '\\'));
        }

        private static GamePatchKitError Error(
            string message,
            string? packageId = null,
            string? group = null)
        {
            return new GamePatchKitError(
                Stage,
                RuntimeErrorCodes.StateInvalid,
                message,
                packageId,
                group: group);
        }
    }
}
