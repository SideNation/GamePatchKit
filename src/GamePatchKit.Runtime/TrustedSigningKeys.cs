using System;
using System.Collections.Generic;
using GamePatchKit.Core.Signatures;

namespace GamePatchKit.Runtime
{
    // The host's trusted Ed25519 public keys, keyed by their derived fingerprint so a manifest.sig's keyId
    // picks out exactly the key that must have produced it. Trusting more than one key at once is what lets a
    // rotation have a window where both the old and new signing key verify.
    public sealed class TrustedSigningKeys
    {
        private readonly IReadOnlyDictionary<string, byte[]> _publicKeysByKeyId;

        public TrustedSigningKeys(IEnumerable<byte[]> trustedPublicKeys)
        {
            if (trustedPublicKeys == null)
            {
                throw new ArgumentNullException(nameof(trustedPublicKeys));
            }

            var map = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            foreach (byte[] publicKey in trustedPublicKeys)
            {
                if (publicKey == null)
                {
                    throw new ArgumentException("trustedPublicKeys must not contain null.", nameof(trustedPublicKeys));
                }

                string keyId = Ed25519Signatures.DeriveKeyId(publicKey);
                map[keyId] = (byte[])publicKey.Clone();
            }

            if (map.Count == 0)
            {
                throw new ArgumentException("At least one trusted public key is required.", nameof(trustedPublicKeys));
            }

            _publicKeysByKeyId = map;
        }

        public bool TryGetPublicKey(string keyId, out byte[] publicKey)
        {
            if (_publicKeysByKeyId.TryGetValue(keyId, out byte[]? found))
            {
                publicKey = (byte[])found.Clone();
                return true;
            }

            publicKey = Array.Empty<byte>();
            return false;
        }
    }
}
