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

        // Convert.FromBase64String rejects non-zero trailing padding bits (FormatException), which is
        // exactly the canonical-encoding check an unpadded base64url signature needs.
        private static bool TryDecodeBase64Url(string value, out byte[] bytes)
        {
            string base64 = value.Replace('-', '+').Replace('_', '/');
            int paddingNeeded = (4 - (base64.Length % 4)) % 4;
            base64 += new string('=', paddingNeeded);

            try
            {
                bytes = Convert.FromBase64String(base64);
                return true;
            }
            catch (FormatException)
            {
                bytes = Array.Empty<byte>();
                return false;
            }
        }
    }
}
