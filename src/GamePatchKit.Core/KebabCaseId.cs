using System.Text.RegularExpressions;

namespace GamePatchKit.Core
{
    // Shared by packageId and group names across config, manifest, and channel: lowercase kebab-case,
    // no leading/trailing/double hyphens, no empty string.
    public static class KebabCaseId
    {
        public const string Pattern = "^[a-z0-9]+(-[a-z0-9]+)*$";

        private static readonly Regex _regex = new Regex(Pattern, RegexOptions.Compiled);

        public static bool IsValid(string value)
        {
            return value != null && _regex.IsMatch(value);
        }
    }
}
