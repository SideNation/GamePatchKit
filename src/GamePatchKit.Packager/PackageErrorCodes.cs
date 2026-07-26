namespace GamePatchKit.Packager;

public static class PackageErrorCodes
{
    public const string InvalidConfiguration = "packager.invalid-configuration";

    public const string InvalidInputRoot = "packager.invalid-input-root";

    public const string UnsupportedFileSystem = "packager.unsupported-filesystem";

    public const string UnsupportedEntry = "packager.unsupported-entry";

    public const string SourceChanged = "packager.source-changed";

    public const string InvalidPreviousManifest = "packager.invalid-previous-manifest";

    public const string PackageIdMismatch = "packager.package-id-mismatch";

    public const string MissingCompressionCodec = "packager.missing-compression-codec";

    public const string ArtifactCorrupted = "packager.artifact-corrupted";

    public const string ImmutablePathConflict = "packager.immutable-path-conflict";

    public const string ManifestInvalid = "packager.manifest-invalid";

    public const string InvalidManifestDocument = "packager.invalid-manifest-document";

    public const string ManifestNotFound = "packager.manifest-not-found";

    public const string InvalidSignature = "packager.invalid-signature";

    public const string InvalidSigningKey = "packager.invalid-signing-key";
}
