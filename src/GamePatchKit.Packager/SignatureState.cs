namespace GamePatchKit.Packager;

public enum SignatureState
{
    // The release has no manifest.sig. Only reported when the caller did not require one.
    Absent,

    // manifest.sig exists and is a schema-valid, model-valid signature document, but nothing checked that the
    // 64 signature bytes actually sign this manifest - the caller supplied no IManifestSignatureVerifier.
    //
    // This is not evidence that the release was signed by anyone in particular. Any correctly shaped keyId
    // and any 64 bytes reach this state, so it must never be used as a signing gate.
    Present,

    // A supplied verifier accepted the signature over the published canonical manifest bytes.
    Verified,
}
