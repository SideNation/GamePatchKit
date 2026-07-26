namespace GamePatchKit.Core.Manifests
{
    public static class ManifestErrorCodes
    {
        // Shape (TryParse): kept generic, GamePatchKitError.Message carries the specific field/value.
        public const string UnknownProperty = "manifest.unknown-property";

        public const string InvalidField = "manifest.invalid-field";

        // Semantic (ManifestValidator).
        public const string DuplicateGroupName = "manifest.duplicate-group-name";

        public const string DuplicateFilePath = "manifest.duplicate-file-path";

        public const string NonCanonicalPath = "manifest.non-canonical-path";

        public const string UnknownFileGroup = "manifest.unknown-file-group";

        public const string DuplicateArtifactIdentity = "manifest.duplicate-artifact-identity";

        public const string ArtifactPathMismatch = "manifest.artifact-path-mismatch";

        public const string UnknownFileArtifactReference = "manifest.unknown-file-artifact-reference";

        public const string UnknownBundleArtifactReference = "manifest.unknown-bundle-artifact-reference";

        public const string UnknownBundleEntryReference = "manifest.unknown-bundle-entry-reference";

        public const string BundleEntryGroupMismatch = "manifest.bundle-entry-group-mismatch";

        public const string NonContiguousPartIndex = "manifest.non-contiguous-part-index";

        public const string DuplicatePartPath = "manifest.duplicate-part-path";

        public const string PartSizeSumMismatch = "manifest.part-size-sum-mismatch";

        public const string UnreferencedFileArtifact = "manifest.unreferenced-file-artifact";

        public const string UnreferencedBundleEntry = "manifest.unreferenced-bundle-entry";

        public const string DuplicateBundleEntryReference = "manifest.duplicate-bundle-entry-reference";

        public const string UnsortedGroups = "manifest.unsorted-groups";

        public const string UnsortedFiles = "manifest.unsorted-files";

        public const string UnsortedArtifacts = "manifest.unsorted-artifacts";

        public const string UnsortedBundleEntries = "manifest.unsorted-bundle-entries";

        public const string CaseInsensitiveDuplicateFilePath = "manifest.case-insensitive-duplicate-file-path";

        public const string InconsistentFileArtifactContent = "manifest.inconsistent-file-artifact-content";

        public const string DuplicateBundleEntryPath = "manifest.duplicate-bundle-entry-path";

        public const string BundleEntryPathMismatch = "manifest.bundle-entry-path-mismatch";

        // Identity (ReleaseIdentity).
        public const string InvalidManifestHashFormat = "manifest.invalid-manifest-hash-format";

        public const string ManifestHashMismatch = "manifest.manifest-hash-mismatch";
    }
}
