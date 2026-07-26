using System;
using System.IO;

namespace GamePatchKit.DotNet;

// Defends the same threat GamePatchKit.Packager's PackagePath.Resolve defends against: a stored relative path
// (artifact cache key or manifest file path) is expected to stay within its root and contain no symlink or
// reparse component, but this adapter is the last line before a real filesystem write, so it checks rather
// than assumes.
internal static class AdapterPath
{
    public static string Resolve(string root, string relativePath)
    {
        string fullRoot = Path.GetFullPath(root);
        string nativeRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, nativeRelativePath));
        string rootPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;

        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!fullPath.StartsWith(rootPrefix, pathComparison))
        {
            throw new IOException("A stored relative path escaped its root directory.");
        }

        EnsureNoLinkComponents(fullRoot, fullPath);
        return fullPath;
    }

    private static void EnsureNoLinkComponents(string root, string fullPath)
    {
        string relativePath = Path.GetRelativePath(root, fullPath);
        string current = root;
        CheckExistingPath(current);

        foreach (string segment in relativePath.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            CheckExistingPath(current);
        }
    }

    private static void CheckExistingPath(string path)
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
            throw new IOException("A stored path contains a symlink or reparse point.");
        }
    }
}
