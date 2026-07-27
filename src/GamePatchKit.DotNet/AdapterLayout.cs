using System;
using System.IO;
using GamePatchKit.Core;

namespace GamePatchKit.DotNet;

// The fixed on-disk layout under a runtime root, and the opaque installationKey format that lets
// InstallationExistsAsync/GetInstallationFilePathsAsync/OpenInstallationFileAsync resolve a directory from the
// key alone - those three IRuntimeStorage members take no packageId, so the key has to carry it.
internal static class AdapterLayout
{
    private const string PackagesDirectoryName = "packages";
    private const string StateDirectoryName = "state";
    private const string StateFileName = "package-state.json";
    private const string StateLockFileName = "package-state.lock";
    private const string CacheDirectoryName = "cache";
    private const string InstallsDirectoryName = "installs";
    private const string StagingDirectoryName = "staging";

    public static string PackageRoot(string runtimeRoot, string packageId)
    {
        return Path.Combine(runtimeRoot, PackagesDirectoryName, packageId);
    }

    public static string StateDirectory(string runtimeRoot, string packageId)
    {
        return Path.Combine(PackageRoot(runtimeRoot, packageId), StateDirectoryName);
    }

    public static string StateFilePath(string runtimeRoot, string packageId)
    {
        return Path.Combine(StateDirectory(runtimeRoot, packageId), StateFileName);
    }

    public static string StateLockFilePath(string runtimeRoot, string packageId)
    {
        return Path.Combine(StateDirectory(runtimeRoot, packageId), StateLockFileName);
    }

    public static string CacheDirectory(string runtimeRoot, string packageId)
    {
        return Path.Combine(PackageRoot(runtimeRoot, packageId), CacheDirectoryName);
    }

    public static string InstallsDirectory(string runtimeRoot, string packageId)
    {
        return Path.Combine(PackageRoot(runtimeRoot, packageId), InstallsDirectoryName);
    }

    public static string StagingDirectory(string runtimeRoot, string packageId)
    {
        return Path.Combine(PackageRoot(runtimeRoot, packageId), StagingDirectoryName);
    }

    // packageId is validated kebab-case ([a-z0-9-]+) and localId is a GUID "N" string, so neither can contain
    // '/' and splitting on the first one always round-trips to the pair that built it.
    public static string InstallationKey(string packageId, string localId)
    {
        return $"{packageId}/{localId}";
    }

    public static string InstallationDirectory(string runtimeRoot, string installationKey)
    {
        (string packageId, string localId) = SplitInstallationKey(installationKey);
        return Path.Combine(InstallsDirectory(runtimeRoot, packageId), localId);
    }

    // installationKey is opaque to Runtime, but Runtime does round-trip whatever PackageState bytes it is
    // given back through here - and a corrupted-but-schema-valid state file is exactly the case this adapter's
    // recovery path has to tolerate. Checking for a bare separator is not enough: "pkg//tmp" would split into
    // localId "/tmp", which Path.Combine treats as rooted on Unix and discards the installs/<packageId>
    // prefix entirely, escaping the installation root. Requiring the exact shape this adapter itself produces
    // closes that off instead of trusting the string to only ever be well-formed.
    private static (string PackageId, string LocalId) SplitInstallationKey(string installationKey)
    {
        int separatorIndex = installationKey.IndexOf('/');
        if (separatorIndex <= 0 || separatorIndex == installationKey.Length - 1)
        {
            throw new ArgumentException(
                "installationKey must be in the 'packageId/localId' form produced by this adapter.",
                nameof(installationKey));
        }

        string packageId = installationKey.Substring(0, separatorIndex);
        string localId = installationKey.Substring(separatorIndex + 1);

        if (!KebabCaseId.IsValid(packageId) || !Guid.TryParseExact(localId, "N", out _))
        {
            throw new ArgumentException(
                "installationKey must be in the 'packageId/localId' form produced by this adapter.",
                nameof(installationKey));
        }

        return (packageId, localId);
    }
}
