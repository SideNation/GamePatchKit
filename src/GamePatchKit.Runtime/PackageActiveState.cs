using System;

namespace GamePatchKit.Runtime
{
    public sealed class PackageActiveState
    {
        public string DataVersion { get; }

        public string ManifestHash { get; }

        public PackageActiveState(string dataVersion, string manifestHash)
        {
            DataVersion = dataVersion ?? throw new ArgumentNullException(nameof(dataVersion));
            ManifestHash = manifestHash ?? throw new ArgumentNullException(nameof(manifestHash));
        }
    }
}
