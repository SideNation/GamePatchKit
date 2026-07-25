using System.Text.RegularExpressions;

namespace GamePatchKit.Core
{
    // Shared by fileHash, artifactHash, partHash, and channel manifestHash: lowercase hex, exactly 64 chars.
    public static class Hex64
    {
        public const string Pattern = "^[0-9a-f]{64}$";

        private static readonly Regex _regex = new Regex(Pattern, RegexOptions.Compiled);

        public static bool IsValid(string value)
        {
            return value != null && _regex.IsMatch(value);
        }
    }
}
