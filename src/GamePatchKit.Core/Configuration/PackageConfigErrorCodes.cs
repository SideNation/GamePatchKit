namespace GamePatchKit.Core.Configuration
{
    public static class PackageConfigErrorCodes
    {
        public const string UnknownProperty = "package-config.unknown-property";

        public const string InvalidSchemaVersion = "package-config.invalid-schema-version";

        public const string InvalidPackageId = "package-config.invalid-package-id";

        public const string MissingInputRoot = "package-config.missing-input-root";

        public const string EmptyInclude = "package-config.empty-include";

        public const string InvalidGlobPattern = "package-config.invalid-glob-pattern";

        public const string InvalidMaxArtifactBytes = "package-config.invalid-max-artifact-bytes";

        public const string InvalidDefaultArtifactMode = "package-config.invalid-default-artifact-mode";

        public const string InvalidCompression = "package-config.invalid-compression";

        public const string InvalidGroup = "package-config.invalid-group";

        public const string InvalidGroupName = "package-config.invalid-group-name";

        public const string ReservedGroupName = "package-config.reserved-group-name";

        public const string EmptyGroupInclude = "package-config.empty-group-include";

        public const string InvalidGroupArtifactMode = "package-config.invalid-group-artifact-mode";

        public const string MissingGroupRequired = "package-config.missing-group-required";

        public const string DuplicateGroupName = "package-config.duplicate-group-name";
    }
}
