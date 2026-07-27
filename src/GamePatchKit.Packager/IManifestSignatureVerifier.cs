using GamePatchKit.Core.Signatures;

namespace GamePatchKit.Packager;

// The seam between "this manifest.sig is a well-formed signature document" - which the Packager decides on its
// own - and "these 64 bytes are a valid Ed25519 signature over these manifest bytes by a key we trust", which
// needs a signature primitive and a trusted-key set the Packager does not own.
//
// Step 11 supplies the implementation over Core's shared verification API, so Runtime and Packager accept and
// reject exactly the same signatures. Until then a verify run with no verifier is an unsigned verification:
// it reports whether a signature is present and structurally valid and never claims it was checked.
public interface IManifestSignatureVerifier
{
    // canonicalManifestBytes is the manifest exactly as published - the bytes manifestHash was taken over and
    // the bytes that were signed. Return false to reject; throw only for an unusable trusted-key set.
    bool Verify(byte[] canonicalManifestBytes, ManifestSignature signature);
}
