namespace GamePatchKit.Runtime
{
    public static class RuntimeErrorCodes
    {
        public const string ManifestInvalid = "runtime.manifest-invalid";
        public const string StateInvalid = "runtime.state-invalid";
        public const string StateConflict = "runtime.state-conflict";
        public const string ArtifactCorrupted = "runtime.artifact-corrupted";
        public const string MissingCompressionCodec = "runtime.missing-compression-codec";
        public const string TransportFailed = "runtime.transport-failed";
        public const string StagingFailed = "runtime.staging-failed";
        public const string ActivationFailed = "runtime.activation-failed";
    }
}
