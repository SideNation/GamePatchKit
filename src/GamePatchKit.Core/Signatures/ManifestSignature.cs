using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Signatures
{
    // Canonical JSON content of a manifests/<manifestHash>/manifest.sig file.
    public sealed class ManifestSignature
    {
        public const int SupportedSchemaVersion = 1;
        public const string SupportedAlgorithm = "Ed25519";
        public const int SignatureByteLength = 64;

        private const string Stage = "manifest-signature";
        private const string KeyIdPattern = "^ed25519-[0-9a-f]{64}$";
        private const string SignaturePattern = "^[A-Za-z0-9_-]{86}$";

        private static readonly Regex _keyIdRegex = new Regex(KeyIdPattern, RegexOptions.Compiled);
        private static readonly Regex _signatureRegex = new Regex(SignaturePattern, RegexOptions.Compiled);

        private static readonly HashSet<string> _knownProperties = new HashSet<string>
        {
            "schemaVersion", "algorithm", "keyId", "signature",
        };

        public int SchemaVersion { get; }

        public string Algorithm { get; }

        public string KeyId { get; }

        public string Signature { get; }

        public ManifestSignature(int schemaVersion, string algorithm, string keyId, string signature)
        {
            SchemaVersion = schemaVersion;
            Algorithm = algorithm ?? throw new ArgumentNullException(nameof(algorithm));
            KeyId = keyId ?? throw new ArgumentNullException(nameof(keyId));
            Signature = signature ?? throw new ArgumentNullException(nameof(signature));
        }

        public static bool TryParse(JObject obj, out ManifestSignature? signature, out IReadOnlyList<GamePatchKitError> errors)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            var errorList = new List<GamePatchKitError>();

            foreach (string unknown in JsonReadHelpers.FindUnknownProperties(obj, _knownProperties))
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestSignatureErrorCodes.UnknownProperty, $"Unknown property '{unknown}'."));
            }

            bool hasSchemaVersion = JsonReadHelpers.TryGetRequiredInteger(obj, "schemaVersion", out long schemaVersion) && schemaVersion == SupportedSchemaVersion;
            if (!hasSchemaVersion)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestSignatureErrorCodes.InvalidSchemaVersion, $"'schemaVersion' must be {SupportedSchemaVersion}."));
            }

            bool hasAlgorithm = JsonReadHelpers.TryGetRequiredString(obj, "algorithm", out string algorithm) && algorithm == SupportedAlgorithm;
            if (!hasAlgorithm)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestSignatureErrorCodes.InvalidAlgorithm, $"'algorithm' must be '{SupportedAlgorithm}'."));
            }

            bool hasKeyId = JsonReadHelpers.TryGetRequiredString(obj, "keyId", out string keyId) && _keyIdRegex.IsMatch(keyId);
            if (!hasKeyId)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestSignatureErrorCodes.InvalidKeyId, "'keyId' must match 'ed25519-' + lowercase hex64 of the raw public key's SHA-256."));
            }

            bool hasSignatureValue = JsonReadHelpers.TryGetRequiredString(obj, "signature", out string signatureValue);
            bool signatureDecodesToExpectedLength = hasSignatureValue
                && _signatureRegex.IsMatch(signatureValue)
                && TryDecodeBase64Url(signatureValue, out byte[] decoded)
                && decoded.Length == SignatureByteLength;

            if (!signatureDecodesToExpectedLength)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestSignatureErrorCodes.InvalidSignature, "'signature' must be an unpadded base64url encoding of exactly 64 bytes with zero padding bits."));
            }

            if (!hasSchemaVersion || !hasAlgorithm || !hasKeyId || !signatureDecodesToExpectedLength || errorList.Count > 0)
            {
                signature = null;
                errors = errorList;
                return false;
            }

            signature = new ManifestSignature((int)schemaVersion, algorithm, keyId, signatureValue);
            errors = errorList;
            return true;
        }

        public JObject ToJson()
        {
            return new JObject
            {
                ["schemaVersion"] = SchemaVersion,
                ["algorithm"] = Algorithm,
                ["keyId"] = KeyId,
                ["signature"] = Signature,
            };
        }

        // The raw 64 signature bytes Signature encodes, decoded once here instead of by every verifier that
        // consumes this model. Every instance TryParse produced decodes cleanly, since decodability is one of
        // the things it checks; an instance built directly (not through TryParse) may not.
        public bool TryGetSignatureBytes(out byte[] signatureBytes)
        {
            if (TryDecodeBase64Url(Signature, out byte[] bytes) && bytes.Length == SignatureByteLength)
            {
                signatureBytes = bytes;
                return true;
            }

            signatureBytes = Array.Empty<byte>();
            return false;
        }

        // Throws only for an instance built directly (not through TryParse) with a signature string that is
        // not validly-encoded. An IManifestSignatureVerifier must reject rather than throw for bad signature
        // data (see IManifestSignatureVerifier's contract), so a verifier should call TryGetSignatureBytes
        // instead of this.
        public byte[] GetSignatureBytes()
        {
            if (!TryGetSignatureBytes(out byte[] bytes))
            {
                throw new InvalidOperationException("This ManifestSignature instance does not hold a validly-encoded 64-byte signature.");
            }

            return bytes;
        }

        // Convert.FromBase64String does NOT reject non-zero unused bits in the final base64 group - e.g. both
        // "AA==" and "AP==" decode to the same single zero byte, even though only "AA==" is the canonical
        // encoding of that byte. Re-encoding the decoded bytes and comparing back to the original string is
        // what actually enforces "there is exactly one valid encoding for these bytes", the same canonical-
        // round-trip pattern this codebase already uses for canonical JSON bytes.
        private static bool TryDecodeBase64Url(string value, out byte[] bytes)
        {
            string base64 = value.Replace('-', '+').Replace('_', '/');
            int paddingNeeded = (4 - (base64.Length % 4)) % 4;
            base64 += new string('=', paddingNeeded);

            byte[] decoded;

            try
            {
                decoded = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                bytes = Array.Empty<byte>();
                return false;
            }

            string reencoded = Convert.ToBase64String(decoded).TrimEnd('=').Replace('+', '-').Replace('/', '_');

            if (reencoded != value)
            {
                bytes = Array.Empty<byte>();
                return false;
            }

            bytes = decoded;
            return true;
        }
    }
}
