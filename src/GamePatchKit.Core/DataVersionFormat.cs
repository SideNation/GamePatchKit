using System.Text.RegularExpressions;

namespace GamePatchKit.Core
{
    // "v1-" + lowercase hex64 SHA-256 of canonical identity bytes. The prefix keeps dataVersion visually
    // distinct from the bare-hex manifestHash/fileHash/artifactHash family (docs/plan/02 decision).
    public static class DataVersionFormat
    {
        public const string Prefix = "v1-";

        public const string Pattern = "^v1-[0-9a-f]{64}$";

        private static readonly Regex _regex = new Regex(Pattern, RegexOptions.Compiled);

        public static bool IsValid(string value)
        {
            return value != null && _regex.IsMatch(value);
        }
    }
}
