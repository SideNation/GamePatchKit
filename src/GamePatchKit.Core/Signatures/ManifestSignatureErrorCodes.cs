namespace GamePatchKit.Core.Signatures
{
    public static class ManifestSignatureErrorCodes
    {
        public const string UnknownProperty = "manifest-signature.unknown-property";

        public const string InvalidSchemaVersion = "manifest-signature.invalid-schema-version";

        public const string InvalidAlgorithm = "manifest-signature.invalid-algorithm";

        public const string InvalidKeyId = "manifest-signature.invalid-key-id";

        public const string InvalidSignature = "manifest-signature.invalid-signature";
    }
}
