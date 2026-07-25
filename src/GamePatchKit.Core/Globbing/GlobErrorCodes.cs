namespace GamePatchKit.Core.Globbing
{
    public static class GlobErrorCodes
    {
        public const string Empty = "glob.empty";

        public const string ContainsNul = "glob.contains-nul";

        public const string ContainsBackslash = "glob.contains-backslash";

        public const string Absolute = "glob.absolute";

        public const string EmptySegment = "glob.empty-segment";

        public const string DotSegment = "glob.dot-segment";

        public const string PartialRecursive = "glob.partial-recursive";

        public const string Negation = "glob.negation";

        public const string DisallowedCharacter = "glob.disallowed-character";

        public const string AmbiguousGroupMatch = "glob.ambiguous-group-match";
    }
}
