using System;

namespace GamePatchKit.Runtime
{
    public sealed class PackageGroupState
    {
        public string Name { get; }

        public PackageGroupStatus Status { get; }

        public string? VerifiedManifestHash { get; }

        public string? InstallationKey { get; }

        public PackageGroupState(
            string name,
            PackageGroupStatus status,
            string? verifiedManifestHash = null,
            string? installationKey = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Status = status;
            VerifiedManifestHash = verifiedManifestHash;
            InstallationKey = installationKey;
        }
    }
}
