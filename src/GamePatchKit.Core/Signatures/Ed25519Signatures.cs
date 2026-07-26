using System;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace GamePatchKit.Core.Signatures
{
    // The Ed25519 primitive shared by everything that signs or verifies a canonical manifest: Packager's
    // signer derives its keyId here, and both Packager's and Runtime's signature verifiers call Verify here.
    // One implementation means a signature accepted in one place is accepted everywhere, and a keyId derived
    // in one place matches everywhere. Verification-only: this type never holds or accepts private key
    // material.
    public static class Ed25519Signatures
    {
        public const int PublicKeyByteLength = 32;

        // "ed25519-" + lowercase hex64 SHA-256 of the raw 32-byte public key. Never an operator-chosen alias -
        // the fingerprint is derived from the key itself, so a keyId names exactly one key.
        public static string DeriveKeyId(byte[] publicKey)
        {
            if (publicKey == null)
            {
                throw new ArgumentNullException(nameof(publicKey));
            }

            if (publicKey.Length != PublicKeyByteLength)
            {
                throw new ArgumentException($"An Ed25519 public key must be exactly {PublicKeyByteLength} bytes.", nameof(publicKey));
            }

            return "ed25519-" + Sha256Hash.ComputeHex(publicKey);
        }

        // Returns false for any cryptographically invalid signature - wrong length, wrong key, bit-flipped
        // message, or a non-canonical (unreduced) S component - instead of throwing, since all of those are
        // properties of untrusted input, not caller error. Only a malformed publicKey (the caller's own
        // configured trust material) throws.
        public static bool Verify(byte[] publicKey, byte[] message, byte[] signature)
        {
            if (publicKey == null)
            {
                throw new ArgumentNullException(nameof(publicKey));
            }

            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            if (signature == null)
            {
                throw new ArgumentNullException(nameof(signature));
            }

            if (publicKey.Length != PublicKeyByteLength)
            {
                throw new ArgumentException($"An Ed25519 public key must be exactly {PublicKeyByteLength} bytes.", nameof(publicKey));
            }

            if (signature.Length != ManifestSignature.SignatureByteLength)
            {
                return false;
            }

            try
            {
                var verifier = new Ed25519Signer();
                verifier.Init(forSigning: false, new Ed25519PublicKeyParameters(publicKey, 0));
                verifier.BlockUpdate(message, 0, message.Length);
                return verifier.VerifySignature(signature);
            }
            catch (Exception)
            {
                // BouncyCastle throws for some malformed inputs (e.g. an encoded point it cannot decode)
                // rather than returning false. Either way the signature is not valid.
                return false;
            }
        }
    }
}
