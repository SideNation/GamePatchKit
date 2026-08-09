namespace GamePatchKit.Cli;

internal static class RelativePathValidator
{
    public static bool IsNormalized(string? path, bool allowRepositoryRoot)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (allowRepositoryRoot && path == ".")
        {
            return true;
        }

        if (path == "."
            || path.Contains('\\')
            || path.Contains('\0')
            || path.StartsWith('/')
            || path.EndsWith('/')
            || path.Contains("//", StringComparison.Ordinal)
            || IsWindowsDrivePath(path))
        {
            return false;
        }

        foreach (string segment in path.Split('/'))
        {
            if (segment is "." or "..")
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWindowsDrivePath(string path)
    {
        return path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';
    }
}