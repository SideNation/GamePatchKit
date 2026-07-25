using System;
using System.Collections.Generic;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Channels
{
    // External pointer an environment uses to select the manifestHash/dataVersion a client should target.
    // Not part of a release the Packager produces; a publisher creates and updates it separately.
    public sealed class Channel
    {
        public const int SupportedSchemaVersion = 1;

        private const string Stage = "channel";

        private static readonly HashSet<string> _knownProperties = new HashSet<string>
        {
            "schemaVersion", "packageId", "manifestHash", "dataVersion",
        };

        public int SchemaVersion { get; }

        public string PackageId { get; }

        public string ManifestHash { get; }

        public string DataVersion { get; }

        public Channel(int schemaVersion, string packageId, string manifestHash, string dataVersion)
        {
            SchemaVersion = schemaVersion;
            PackageId = packageId ?? throw new ArgumentNullException(nameof(packageId));
            ManifestHash = manifestHash ?? throw new ArgumentNullException(nameof(manifestHash));
            DataVersion = dataVersion ?? throw new ArgumentNullException(nameof(dataVersion));
        }

        public static bool TryParse(JObject obj, out Channel? channel, out IReadOnlyList<GamePatchKitError> errors)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            var errorList = new List<GamePatchKitError>();

            foreach (string unknown in JsonReadHelpers.FindUnknownProperties(obj, _knownProperties))
            {
                errorList.Add(new GamePatchKitError(Stage, ChannelErrorCodes.UnknownProperty, $"Unknown property '{unknown}'."));
            }

            bool hasSchemaVersion = JsonReadHelpers.TryGetRequiredInteger(obj, "schemaVersion", out long schemaVersion) && schemaVersion == SupportedSchemaVersion;
            if (!hasSchemaVersion)
            {
                errorList.Add(new GamePatchKitError(Stage, ChannelErrorCodes.InvalidSchemaVersion, $"'schemaVersion' must be {SupportedSchemaVersion}."));
            }

            bool hasPackageId = JsonReadHelpers.TryGetRequiredString(obj, "packageId", out string packageId) && KebabCaseId.IsValid(packageId);
            if (!hasPackageId)
            {
                errorList.Add(new GamePatchKitError(Stage, ChannelErrorCodes.InvalidPackageId, "'packageId' must be lowercase kebab-case."));
            }

            bool hasManifestHash = JsonReadHelpers.TryGetRequiredString(obj, "manifestHash", out string manifestHash) && Hex64.IsValid(manifestHash);
            if (!hasManifestHash)
            {
                errorList.Add(new GamePatchKitError(Stage, ChannelErrorCodes.InvalidManifestHash, "'manifestHash' must be lowercase hex64."));
            }

            bool hasDataVersion = JsonReadHelpers.TryGetRequiredString(obj, "dataVersion", out string dataVersion) && DataVersionFormat.IsValid(dataVersion);
            if (!hasDataVersion)
            {
                errorList.Add(new GamePatchKitError(Stage, ChannelErrorCodes.InvalidDataVersion, "'dataVersion' must match 'v1-' + lowercase hex64."));
            }

            if (!hasSchemaVersion || !hasPackageId || !hasManifestHash || !hasDataVersion || errorList.Count > 0)
            {
                channel = null;
                errors = errorList;
                return false;
            }

            channel = new Channel((int)schemaVersion, packageId, manifestHash, dataVersion);
            errors = errorList;
            return true;
        }

        public JObject ToJson()
        {
            return new JObject
            {
                ["schemaVersion"] = SchemaVersion,
                ["packageId"] = PackageId,
                ["manifestHash"] = ManifestHash,
                ["dataVersion"] = DataVersion,
            };
        }
    }
}
