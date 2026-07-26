namespace GamePatchKit.Core.Paths
{
    public static class PathErrorCodes
    {
        public const string Empty = "path.empty";

        public const string ContainsNul = "path.contains-nul";

        public const string ContainsBackslash = "path.contains-backslash";

        public const string Absolute = "path.absolute";

        public const string EmptySegment = "path.empty-segment";

        public const string DotSegment = "path.dot-segment";

        public const string DotDotSegment = "path.dot-dot-segment";

        public const string CaseInsensitiveDuplicate = "path.case-insensitive-duplicate";
    }
}
