using GamePatchKit.Core.Errors;

namespace GamePatchKit.Packager;

internal static class PackagePath
{
    public static void EnsureOutputRootIsNotLink(string outputRoot, string packageId)
    {
        string root = Path.GetFullPath(outputRoot);
        FileSystemInfo information = Directory.Exists(root)
            ? new DirectoryInfo(root)
            : new FileInfo(root);

        if (information.Exists && (information.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw LinkFailure(packageId, ".");
        }
    }

    public static string Resolve(string outputRoot, string canonicalPath)
    {
        string root = Path.GetFullPath(outputRoot);
        string nativeRelativePath = canonicalPath.Replace('/', Path.DirectorySeparatorChar);
        string fullPath = Path.GetFullPath(Path.Combine(root, nativeRelativePath));
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!fullPath.StartsWith(rootPrefix, pathComparison))
        {
            throw new InvalidOperationException("A canonical package path escaped the output root.");
        }

        EnsureNoLinkComponents(root, fullPath, canonicalPath);
        return fullPath;
    }

    private static void EnsureNoLinkComponents(string root, string fullPath, string canonicalPath)
    {
        string packageId = canonicalPath.Split('/')[0];
        string relativePath = Path.GetRelativePath(root, fullPath);
        string current = root;
        CheckExistingPath(current, packageId, relativePath);

        foreach (string segment in relativePath.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            CheckExistingPath(current, packageId, relativePath);
        }
    }

    private static void CheckExistingPath(string path, string packageId, string relativePath)
    {
        FileSystemInfo? information = null;

        if (Directory.Exists(path))
        {
            information = new DirectoryInfo(path);
        }
        else if (File.Exists(path))
        {
            information = new FileInfo(path);
        }
        else
        {
            var possibleLink = new FileInfo(path);
            if (possibleLink.LinkTarget != null)
            {
                information = possibleLink;
            }
        }

        if (information != null && (information.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw LinkFailure(packageId, relativePath.Replace(Path.DirectorySeparatorChar, '/'));
        }
    }

    private static PackageException LinkFailure(string packageId, string relativePath)
    {
        return new PackageException(
            new GamePatchKitError(
                "package-path",
                PackageErrorCodes.ImmutablePathConflict,
                "The output path contains a symlink or reparse point.",
                packageId,
                relativePath));
    }
}
