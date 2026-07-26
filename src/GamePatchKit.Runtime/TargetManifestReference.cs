using System;
using GamePatchKit.Core;

namespace GamePatchKit.Runtime
{
    public sealed class TargetManifestReference
    {
        public string PackageId { get; }

        public string DataVersion { get; }

        public string ManifestHash { get; }

        public TargetManifestReference(string packageId, string dataVersion, string manifestHash)
        {
            if (!KebabCaseId.IsValid(packageId))
            {
                throw new ArgumentException("packageId must be lowercase kebab-case.", nameof(packageId));
            }

            if (!DataVersionFormat.IsValid(dataVersion))
            {
                throw new ArgumentException("dataVersion must match 'v1-' + lowercase hex64.", nameof(dataVersion));
            }

            if (!Hex64.IsValid(manifestHash))
            {
                throw new ArgumentException("manifestHash must be lowercase hex64.", nameof(manifestHash));
            }

            PackageId = packageId;
            DataVersion = dataVersion;
            ManifestHash = manifestHash;
        }
    }
}
