using System;
using System.Collections.Generic;
using System.Linq;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Manifests
{
    // Canonical JSON artifact produced by the Packager. manifestHash is the SHA-256 of these exact
    // canonical bytes with no manifestHash field present, so this type never has a ManifestHash property -
    // computing that hash is GamePatchKit.Core's identity API (docs/plan/03), not this model's concern.
    public sealed class ReleaseManifest
    {
        private const string Stage = "release-manifest";
        private const int SupportedSchemaVersion = 1;

        private static readonly HashSet<string> _knownProperties = new HashSet<string>
        {
            "schemaVersion", "packageId", "dataVersion", "compactVersion", "groups", "artifacts", "files",
        };

        public int SchemaVersion { get; }

        public string PackageId { get; }

        public string DataVersion { get; }

        public long CompactVersion { get; }

        public IReadOnlyList<ManifestGroupEntry> Groups { get; }

        public IReadOnlyList<ManifestArtifact> Artifacts { get; }

        public IReadOnlyList<ManifestFileEntry> Files { get; }

        public ReleaseManifest(
            int schemaVersion,
            string packageId,
            string dataVersion,
            long compactVersion,
            IReadOnlyList<ManifestGroupEntry> groups,
            IReadOnlyList<ManifestArtifact> artifacts,
            IReadOnlyList<ManifestFileEntry> files)
        {
            SchemaVersion = schemaVersion;
            PackageId = packageId ?? throw new ArgumentNullException(nameof(packageId));
            DataVersion = dataVersion ?? throw new ArgumentNullException(nameof(dataVersion));
            CompactVersion = compactVersion;
            Groups = groups ?? throw new ArgumentNullException(nameof(groups));
            Artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
            Files = files ?? throw new ArgumentNullException(nameof(files));
        }

        public static bool TryParse(JObject obj, out ReleaseManifest? manifest, out IReadOnlyList<GamePatchKitError> errors)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            var errorList = new List<GamePatchKitError>();

            foreach (string unknown in JsonReadHelpers.FindUnknownProperties(obj, _knownProperties))
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.UnknownProperty, $"Unknown property '{unknown}'."));
            }

            bool hasSchemaVersion = JsonReadHelpers.TryGetRequiredInteger(obj, "schemaVersion", out long schemaVersion) && schemaVersion == SupportedSchemaVersion;
            if (!hasSchemaVersion)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, $"'schemaVersion' must be {SupportedSchemaVersion}."));
            }

            bool hasPackageId = JsonReadHelpers.TryGetRequiredString(obj, "packageId", out string packageId) && KebabCaseId.IsValid(packageId);
            if (!hasPackageId)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "'packageId' must be lowercase kebab-case."));
            }

            bool hasDataVersion = JsonReadHelpers.TryGetRequiredString(obj, "dataVersion", out string dataVersion) && DataVersionFormat.IsValid(dataVersion);
            if (!hasDataVersion)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "'dataVersion' must match 'v1-' + lowercase hex64."));
            }

            bool hasCompactVersion = JsonReadHelpers.TryGetRequiredInteger(obj, "compactVersion", out long compactVersion) && compactVersion >= 0;
            if (!hasCompactVersion)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "'compactVersion' must be a non-negative integer."));
            }

            bool hasGroups = TryParseArray(obj, "groups", errorList, ManifestGroupEntry.TryParse, out List<ManifestGroupEntry> groups);
            bool hasArtifacts = TryParseArray(obj, "artifacts", errorList, ManifestArtifact.TryParse, out List<ManifestArtifact> artifacts);
            bool hasFiles = TryParseArray(obj, "files", errorList, ManifestFileEntry.TryParse, out List<ManifestFileEntry> files);

            if (!hasSchemaVersion || !hasPackageId || !hasDataVersion || !hasCompactVersion || !hasGroups || !hasArtifacts || !hasFiles || errorList.Count > 0)
            {
                manifest = null;
                errors = errorList;
                return false;
            }

            manifest = new ReleaseManifest((int)schemaVersion, packageId, dataVersion, compactVersion, groups, artifacts, files);
            errors = errorList;
            return true;
        }

        public JObject ToJson()
        {
            return new JObject
            {
                ["schemaVersion"] = SchemaVersion,
                ["packageId"] = PackageId,
                ["dataVersion"] = DataVersion,
                ["compactVersion"] = CompactVersion,
                ["groups"] = new JArray(Groups.Select(g => g.ToJson())),
                ["artifacts"] = new JArray(Artifacts.Select(a => a.ToJson())),
                ["files"] = new JArray(Files.Select(f => f.ToJson())),
            };
        }

        private delegate bool TryParseElement<T>(JObject obj, out T? element, out IReadOnlyList<GamePatchKitError> errors)
            where T : class;

        private static bool TryParseArray<T>(JObject obj, string propertyName, List<GamePatchKitError> errorList, TryParseElement<T> tryParseElement, out List<T> elements)
            where T : class
        {
            elements = new List<T>();

            if (!JsonReadHelpers.TryGetRequiredArray(obj, propertyName, out JArray array))
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, $"'{propertyName}' must be an array."));
                return false;
            }

            bool ok = true;

            foreach (JToken item in array)
            {
                if (item.Type != JTokenType.Object)
                {
                    errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, $"Each '{propertyName}' element must be an object."));
                    ok = false;
                    continue;
                }

                if (!tryParseElement((JObject)item!, out T? element, out IReadOnlyList<GamePatchKitError> elementErrors))
                {
                    errorList.AddRange(elementErrors);
                    ok = false;
                    continue;
                }

                elements.Add(element!);
            }

            return ok;
        }
    }
}
