using System;
using System.Collections.Generic;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Globbing;
using GamePatchKit.Core.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Configuration
{
    public sealed class PackageConfig
    {
        public const long DefaultMaxArtifactBytes = 10_485_760L;

        private const string Stage = "package-config";
        private const int SupportedSchemaVersion = 1;

        private static readonly HashSet<string> _knownProperties = new HashSet<string>
        {
            "schemaVersion", "packageId", "inputRoot", "include", "exclude",
            "maxArtifactBytes", "defaultArtifactMode", "compression", "groups",
        };

        public int SchemaVersion { get; }

        public string PackageId { get; }

        public string InputRoot { get; }

        public IReadOnlyList<GlobPattern> Include { get; }

        public IReadOnlyList<GlobPattern> Exclude { get; }

        public long MaxArtifactBytes { get; }

        public ArtifactMode DefaultArtifactMode { get; }

        public CompressionKind Compression { get; }

        public IReadOnlyList<PackageConfigGroup> Groups { get; }

        public PackageConfig(
            int schemaVersion,
            string packageId,
            string inputRoot,
            IReadOnlyList<GlobPattern> include,
            IReadOnlyList<GlobPattern> exclude,
            long maxArtifactBytes,
            ArtifactMode defaultArtifactMode,
            CompressionKind compression,
            IReadOnlyList<PackageConfigGroup> groups)
        {
            SchemaVersion = schemaVersion;
            PackageId = packageId ?? throw new ArgumentNullException(nameof(packageId));
            InputRoot = inputRoot ?? throw new ArgumentNullException(nameof(inputRoot));
            Include = include ?? throw new ArgumentNullException(nameof(include));
            Exclude = exclude ?? throw new ArgumentNullException(nameof(exclude));
            MaxArtifactBytes = maxArtifactBytes;
            DefaultArtifactMode = defaultArtifactMode;
            Compression = compression;
            Groups = groups ?? throw new ArgumentNullException(nameof(groups));
        }

        public static bool TryParse(JObject obj, out PackageConfig? config, out IReadOnlyList<GamePatchKitError> errors)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            var errorList = new List<GamePatchKitError>();

            foreach (string unknown in JsonReadHelpers.FindUnknownProperties(obj, _knownProperties))
            {
                errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.UnknownProperty, $"Unknown property '{unknown}'."));
            }

            bool ok = true;

            if (!JsonReadHelpers.TryGetRequiredInteger(obj, "schemaVersion", out long schemaVersionValue) || schemaVersionValue != SupportedSchemaVersion)
            {
                errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.InvalidSchemaVersion, $"'schemaVersion' must be {SupportedSchemaVersion}."));
                ok = false;
            }

            if (!JsonReadHelpers.TryGetRequiredString(obj, "packageId", out string packageId) || !KebabCaseId.IsValid(packageId))
            {
                errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.InvalidPackageId, "'packageId' must be lowercase kebab-case."));
                ok = false;
            }

            if (!JsonReadHelpers.TryGetRequiredString(obj, "inputRoot", out string inputRoot) || inputRoot.Length == 0)
            {
                errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.MissingInputRoot, "'inputRoot' must be a non-empty string."));
                ok = false;
            }

            bool hasValidInclude = TryParseGlobArray(obj, "include", requireNonEmpty: true, errorList, out List<GlobPattern> include);
            bool hasValidExclude = TryParseGlobArray(obj, "exclude", requireNonEmpty: false, errorList, out List<GlobPattern> exclude);
            ok &= hasValidInclude & hasValidExclude;

            long maxArtifactBytes = DefaultMaxArtifactBytes;
            if (obj.ContainsKey("maxArtifactBytes"))
            {
                if (!JsonReadHelpers.TryGetRequiredInteger(obj, "maxArtifactBytes", out maxArtifactBytes) || maxArtifactBytes < 1)
                {
                    errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.InvalidMaxArtifactBytes, "'maxArtifactBytes' must be a positive integer."));
                    ok = false;
                }
            }

            ArtifactMode defaultArtifactMode = ArtifactMode.File;
            if (obj.ContainsKey("defaultArtifactMode"))
            {
                if (!TryParseArtifactModeString(obj, "defaultArtifactMode", out defaultArtifactMode))
                {
                    errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.InvalidDefaultArtifactMode, "'defaultArtifactMode' must be 'file' or 'bundle'."));
                    ok = false;
                }
            }

            bool hasValidCompression = TryParseCompression(obj, errorList, out CompressionKind compression);
            ok &= hasValidCompression;

            bool hasValidGroups = TryParseGroups(obj, errorList, out List<PackageConfigGroup> groups);
            ok &= hasValidGroups;

            if (!ok)
            {
                config = null;
                errors = errorList;
                return false;
            }

            config = new PackageConfig(
                (int)schemaVersionValue,
                packageId,
                inputRoot,
                include,
                exclude,
                maxArtifactBytes,
                defaultArtifactMode,
                compression,
                groups);
            errors = errorList;
            return true;
        }

        public JObject ToJson()
        {
            var includeArray = new JArray();
            foreach (GlobPattern pattern in Include)
            {
                includeArray.Add(pattern.SourceText);
            }

            var excludeArray = new JArray();
            foreach (GlobPattern pattern in Exclude)
            {
                excludeArray.Add(pattern.SourceText);
            }

            var groupsArray = new JArray();
            foreach (PackageConfigGroup group in Groups)
            {
                groupsArray.Add(group.ToJson());
            }

            return new JObject
            {
                ["schemaVersion"] = SchemaVersion,
                ["packageId"] = PackageId,
                ["inputRoot"] = InputRoot,
                ["include"] = includeArray,
                ["exclude"] = excludeArray,
                ["maxArtifactBytes"] = MaxArtifactBytes,
                ["defaultArtifactMode"] = DefaultArtifactMode == ArtifactMode.File ? "file" : "bundle",
                ["compression"] = CompressionKindJson.ToJson(Compression),
                ["groups"] = groupsArray,
            };
        }

        private static bool TryParseGlobArray(JObject obj, string propertyName, bool requireNonEmpty, List<GamePatchKitError> errorList, out List<GlobPattern> patterns)
        {
            patterns = new List<GlobPattern>();

            if (!JsonReadHelpers.TryGetOptionalArray(obj, propertyName, out JArray array, out bool wasPresent))
            {
                errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.InvalidGlobPattern, $"'{propertyName}' must be an array."));
                return false;
            }

            if (requireNonEmpty && (!wasPresent || array.Count == 0))
            {
                errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.EmptyInclude, $"'{propertyName}' must not be empty."));
                return false;
            }

            bool ok = true;

            foreach (JToken item in array)
            {
                if (item.Type != JTokenType.String || !GlobPattern.TryParse((string)item!, out GlobPattern? pattern, out string errorCode))
                {
                    errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.InvalidGlobPattern, $"Invalid '{propertyName}' pattern: {item}"));
                    ok = false;
                    continue;
                }

                patterns.Add(pattern!);
            }

            return ok;
        }

        private static bool TryParseArtifactModeString(JObject obj, string propertyName, out ArtifactMode mode)
        {
            mode = ArtifactMode.File;

            if (!JsonReadHelpers.TryGetRequiredString(obj, propertyName, out string value))
            {
                return false;
            }

            switch (value)
            {
                case "file":
                    mode = ArtifactMode.File;
                    return true;
                case "bundle":
                    mode = ArtifactMode.Bundle;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryParseCompression(JObject obj, List<GamePatchKitError> errorList, out CompressionKind compression)
        {
            compression = CompressionKind.None;

            if (!JsonReadHelpers.TryGetRequiredObject(obj, "compression", out JObject compressionObj))
            {
                errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.InvalidCompression, "'compression' must be an object."));
                return false;
            }

            if (!CompressionKindJson.TryParse(compressionObj, out compression, out string errorCode))
            {
                errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.InvalidCompression, $"'compression' is invalid: {errorCode}"));
                return false;
            }

            return true;
        }

        private static bool TryParseGroups(JObject obj, List<GamePatchKitError> errorList, out List<PackageConfigGroup> groups)
        {
            groups = new List<PackageConfigGroup>();

            if (!JsonReadHelpers.TryGetOptionalArray(obj, "groups", out JArray array, out bool wasPresent))
            {
                errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.InvalidGroup, "'groups' must be an array."));
                return false;
            }

            bool ok = true;

            foreach (JToken item in array)
            {
                if (item.Type != JTokenType.Object)
                {
                    errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.InvalidGroup, "Each group must be an object."));
                    ok = false;
                    continue;
                }

                if (!PackageConfigGroup.TryParse((JObject)item!, out PackageConfigGroup? group, out IReadOnlyList<GamePatchKitError> groupErrors))
                {
                    errorList.AddRange(groupErrors);
                    ok = false;
                    continue;
                }

                groups.Add(group!);
            }

            return ok;
        }
    }
}
