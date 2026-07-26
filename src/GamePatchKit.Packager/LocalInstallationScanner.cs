using GamePatchKit.Core.Downloads;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Paths;

namespace GamePatchKit.Packager;

// Turns two local directories into the verified state Core's DownloadPlanner takes: an installation tree of
// files and a content-addressed object cache.
//
// Both scans hash what is actually on disk rather than trusting any local bookkeeping, because that is what
// planning treats as proof - a recorded hash that does not match the bytes would let a damaged file or a
// half-written cache object be planned around instead of re-fetched.
//
// A missing root is an empty result, not a failure: a first install has no installation and no cache yet.
public static class LocalInstallationScanner
{
    private const string Stage = "local-state";

    public static async Task<IReadOnlyList<LocalFileState>> ScanInstalledFilesAsync(
        string installRoot,
        CancellationToken cancellationToken = default)
    {
        var states = new List<LocalFileState>();

        foreach ((string canonicalPath, string nativePath) in EnumerateFiles(installRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            states.Add(new LocalFileState(canonicalPath, await Sha256File.ComputeAsync(nativePath, cancellationToken).ConfigureAwait(false)));
        }

        RejectCaseInsensitiveDuplicates(states.Select(state => state.Path));
        return states;
    }

    public static async Task<IReadOnlyList<CachedArtifactObject>> ScanCachedObjectsAsync(
        string cacheRoot,
        CancellationToken cancellationToken = default)
    {
        var objects = new List<CachedArtifactObject>();

        foreach ((string canonicalPath, string nativePath) in EnumerateFiles(cacheRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            objects.Add(new CachedArtifactObject(canonicalPath, await Sha256File.ComputeAsync(nativePath, cancellationToken).ConfigureAwait(false)));
        }

        RejectCaseInsensitiveDuplicates(objects.Select(cached => cached.Path));
        return objects;
    }

    private static IEnumerable<(string CanonicalPath, string NativePath)> EnumerateFiles(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("Root must not be empty.", nameof(root));
        }

        string fullRoot = Path.GetFullPath(root);

        if (!Directory.Exists(fullRoot))
        {
            if (File.Exists(fullRoot))
            {
                throw Failure(PackageErrorCodes.InvalidInputRoot, "The local state root is a file, not a directory.", ".");
            }

            yield break;
        }

        var rootInformation = new DirectoryInfo(fullRoot);
        if ((rootInformation.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw Failure(PackageErrorCodes.UnsupportedEntry, "The local state root is a symlink or reparse point.", ".");
        }

        foreach (FileSystemInfo entry in rootInformation.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(fullRoot, entry.FullName)
                .Replace(Path.DirectorySeparatorChar, '/');

            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw Failure(PackageErrorCodes.UnsupportedEntry, "The local state tree contains a symlink or reparse point.", relativePath);
            }

            if (entry is DirectoryInfo)
            {
                continue;
            }

            if (entry is not FileInfo)
            {
                throw Failure(PackageErrorCodes.UnsupportedEntry, "The local state tree contains an entry that is neither a file nor a directory.", relativePath);
            }

            if (!RelativePathNormalizer.TryNormalize(relativePath, out string canonicalPath, out string pathErrorCode))
            {
                throw Failure(pathErrorCode, "A local state path cannot be expressed as a canonical relative path.", relativePath);
            }

            yield return (canonicalPath, entry.FullName);
        }
    }

    private static void RejectCaseInsensitiveDuplicates(IEnumerable<string> canonicalPaths)
    {
        IReadOnlyList<IReadOnlyList<string>> duplicates =
            RelativePathNormalizer.FindCaseInsensitiveDuplicateGroups(canonicalPaths);

        if (duplicates.Count > 0)
        {
            throw Failure(
                PathErrorCodes.CaseInsensitiveDuplicate,
                "Two local paths differ only by case, so they cannot both be matched against one manifest path.",
                duplicates[0][0]);
        }
    }

    private static PackageException Failure(string code, string message, string relativePath)
    {
        return new PackageException(new GamePatchKitError(Stage, code, message, packageId: null, relativePath));
    }
}
