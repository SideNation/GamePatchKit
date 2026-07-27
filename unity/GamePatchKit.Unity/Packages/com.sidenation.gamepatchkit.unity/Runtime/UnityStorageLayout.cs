using System;
using System.IO;
using GamePatchKit.Core;

namespace GamePatchKit.Unity
{
    internal static class UnityStorageLayout
    {
        private const string CACHE_DIRECTORY_NAME = "cache";
        private const string INSTALLS_DIRECTORY_NAME = "installs";
        private const string PACKAGES_DIRECTORY_NAME = "packages";
        private const string STAGING_DIRECTORY_NAME = "staging";
        private const string STATE_DIRECTORY_NAME = "state";
        private const string STATE_FILE_NAME = "package-state.json";
        private const string STATE_LOCK_FILE_NAME = "package-state.lock";

        public static string PackageRoot(string runtimeRoot, string packageId)
        {
            return Path.Combine(runtimeRoot, PACKAGES_DIRECTORY_NAME, packageId);
        }

        public static string StateDirectory(string runtimeRoot, string packageId)
        {
            return Path.Combine(PackageRoot(runtimeRoot, packageId), STATE_DIRECTORY_NAME);
        }

        public static string StateFilePath(string runtimeRoot, string packageId)
        {
            return Path.Combine(StateDirectory(runtimeRoot, packageId), STATE_FILE_NAME);
        }

        public static string StateLockFilePath(string runtimeRoot, string packageId)
        {
            return Path.Combine(StateDirectory(runtimeRoot, packageId), STATE_LOCK_FILE_NAME);
        }

        public static string CacheDirectory(string runtimeRoot, string packageId)
        {
            return Path.Combine(PackageRoot(runtimeRoot, packageId), CACHE_DIRECTORY_NAME);
        }

        public static string InstallsDirectory(string runtimeRoot, string packageId)
        {
            return Path.Combine(PackageRoot(runtimeRoot, packageId), INSTALLS_DIRECTORY_NAME);
        }

        public static string StagingDirectory(string runtimeRoot, string packageId)
        {
            return Path.Combine(PackageRoot(runtimeRoot, packageId), STAGING_DIRECTORY_NAME);
        }

        public static string InstallationKey(string packageId, string localId)
        {
            return $"{packageId}/{localId}";
        }

        public static string InstallationDirectory(string runtimeRoot, string installationKey)
        {
            (string packageId, string localId) = SplitInstallationKey(installationKey);
            return Path.Combine(InstallsDirectory(runtimeRoot, packageId), localId);
        }

        private static (string PackageId, string LocalId) SplitInstallationKey(string installationKey)
        {
            int separatorIndex = installationKey.IndexOf('/');
            if (separatorIndex <= 0 || separatorIndex == installationKey.Length - 1)
            {
                throw InvalidInstallationKey(nameof(installationKey));
            }

            string packageId = installationKey.Substring(0, separatorIndex);
            string localId = installationKey.Substring(separatorIndex + 1);

            if (!KebabCaseId.IsValid(packageId) || !Guid.TryParseExact(localId, "N", out _))
            {
                throw InvalidInstallationKey(nameof(installationKey));
            }

            return (packageId, localId);
        }

        private static ArgumentException InvalidInstallationKey(string parameterName)
        {
            return new ArgumentException(
                "installationKey must be in the 'packageId/localId' form produced by this adapter.",
                parameterName);
        }
    }
}

