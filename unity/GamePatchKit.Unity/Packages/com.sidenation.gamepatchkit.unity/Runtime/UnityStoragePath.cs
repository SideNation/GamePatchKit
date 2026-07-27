using System;
using System.IO;

namespace GamePatchKit.Unity
{
    internal static class UnityStoragePath
    {
        public static string Resolve(string root, string relativePath)
        {
            string fullRoot = Path.GetFullPath(root);
            string nativeRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.GetFullPath(Path.Combine(fullRoot, nativeRelativePath));
            string rootPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? fullRoot
                : fullRoot + Path.DirectorySeparatorChar;
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!fullPath.StartsWith(rootPrefix, comparison))
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
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("A stored path contains a symlink or reparse point.");
                }
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}

