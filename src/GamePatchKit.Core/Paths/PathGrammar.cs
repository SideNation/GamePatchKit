namespace GamePatchKit.Core.Paths
{
    // Shared by RelativePathNormalizer and GlobPattern so both reject the same absolute-path forms.
    internal static class PathGrammar
    {
        internal static bool ContainsNul(string value)
        {
            return value.IndexOf('\0') >= 0;
        }

        internal static bool ContainsBackslash(string value)
        {
            return value.IndexOf('\\') >= 0;
        }

        internal static bool IsAbsolute(string value)
        {
            if (value.Length == 0)
            {
                return false;
            }

            if (value[0] == '/')
            {
                return true;
            }

            return value.Length >= 2 && value[1] == ':';
        }
    }
}
