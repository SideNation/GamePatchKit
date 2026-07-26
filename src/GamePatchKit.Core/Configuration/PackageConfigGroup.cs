using System;
using System.Collections.Generic;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Globbing;
using GamePatchKit.Core.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Configuration
{
    public sealed class PackageConfigGroup
    {
        private const string Stage = "package-config";

        private static readonly HashSet<string> _knownProperties = new HashSet<string>
        {
            "name", "include", "artifactMode", "required", "compression",
        };

        public string Name { get; }

        public IReadOnlyList<GlobPattern> Include { get; }

        public ArtifactMode ArtifactMode { get; }

        public bool Required { get; }

        public CompressionKind? Compression { get; }

        public PackageConfigGroup(
            string name,
            IReadOnlyList<GlobPattern> include,
            ArtifactMode artifactMode,
            bool required,
            CompressionKind? compression)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Include = include ?? throw new ArgumentNullException(nameof(include));
            ArtifactMode = artifactMode;
            Required = required;
            Compression = compression;
        }

        public static bool TryParse(JObject obj, out PackageConfigGroup? group, out IReadOnlyList<GamePatchKitError> errors)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            var errorList = new List<GamePatchKitError>();

            foreach (string unknown in JsonReadHelpers.FindUnknownProperties(obj, _knownProperties))
            {
                errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.UnknownProperty, $"Unknown group property '{unknown}'."));
            }

            bool hasValidName = TryParseName(obj, errorList, out string name);
            bool hasValidInclude = TryParseInclude(obj, errorList, out List<GlobPattern> include);
            bool hasValidArtifactMode = TryParseArtifactMode(obj, errorList, out ArtifactMode artifactMode);
            bool hasValidRequired = JsonReadHelpers.TryGetRequiredBoolean(obj, "required", out bool required);

            if (!hasValidRequired)
            {
                errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.MissingGroupRequired, "Group 'required' must be a boolean."));
            }

            bool hasValidCompression = TryParseOptionalCompression(obj, errorList, out CompressionKind? compression);

            if (!hasValidName || !hasValidInclude || !hasValidArtifactMode || !hasValidRequired || !hasValidCompression || errorList.Count > 0)
            {
                group = null;
                errors = errorList;
                return false;
            }

            group = new PackageConfigGroup(name, include, artifactMode, required, compression);
            errors = errorList;
            return true;
        }

        public JObject ToJson()
        {
            var array = new JArray();
            foreach (GlobPattern pattern in Include)
            {
                array.Add(pattern.SourceText);
            }

            var obj = new JObject
            {
                ["name"] = Name,
                ["include"] = array,
                ["artifactMode"] = ArtifactMode == ArtifactMode.File ? "file" : "bundle",
                ["required"] = Required,
            };

            if (Compression.HasValue)
            {
                obj["compression"] = CompressionKindJson.ToJson(Compression.Value);
            }

            return obj;
        }

        private static bool TryParseName(JObject obj, List<GamePatchKitError> errorList, out string name)
        {
            if (!JsonReadHelpers.TryGetRequiredString(obj, "name", out name))
            {
                errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.InvalidGroupName, "Group 'name' must be a string."));
                return false;
            }

            if (name == "default")
            {
                errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.ReservedGroupName, "'default' is reserved and must not be declared explicitly.", group: name));
                return false;
            }

            if (!KebabCaseId.IsValid(name))
            {
                errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.InvalidGroupName, "Group 'name' must be lowercase kebab-case.", group: name));
                return false;
            }

            return true;
        }

        private static bool TryParseInclude(JObject obj, List<GamePatchKitError> errorList, out List<GlobPattern> include)
        {
            include = new List<GlobPattern>();

            if (!JsonReadHelpers.TryGetRequiredArray(obj, "include", out JArray array))
            {
                errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.EmptyGroupInclude, "Group 'include' must be an array."));
                return false;
            }

            if (array.Count == 0)
            {
                errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.EmptyGroupInclude, "Group 'include' must not be empty."));
                return false;
            }

            bool ok = true;

            foreach (JToken item in array)
            {
                if (item.Type != JTokenType.String || !GlobPattern.TryParse((string)item!, out GlobPattern? pattern, out string errorCode))
                {
                    errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.InvalidGlobPattern, $"Invalid group include pattern: {item}"));
                    ok = false;
                    continue;
                }

                include.Add(pattern!);
            }

            return ok;
        }

        private static bool TryParseArtifactMode(JObject obj, List<GamePatchKitError> errorList, out ArtifactMode artifactMode)
        {
            artifactMode = ArtifactMode.File;

            if (!JsonReadHelpers.TryGetRequiredString(obj, "artifactMode", out string value))
            {
                errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.InvalidGroupArtifactMode, "Group 'artifactMode' must be a string."));
                return false;
            }

            switch (value)
            {
                case "file":
                    artifactMode = ArtifactMode.File;
                    return true;
                case "bundle":
                    artifactMode = ArtifactMode.Bundle;
                    return true;
                default:
                    errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.InvalidGroupArtifactMode, $"Group 'artifactMode' must be 'file' or 'bundle', was '{value}'."));
                    return false;
            }
        }

        private static bool TryParseOptionalCompression(JObject obj, List<GamePatchKitError> errorList, out CompressionKind? compression)
        {
            if (!JsonReadHelpers.TryGetOptionalObject(obj, "compression", out JObject? compressionObj))
            {
                compression = null;
                errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.InvalidCompression, "Group 'compression' must be an object."));
                return false;
            }

            if (compressionObj == null)
            {
                compression = null;
                return true;
            }

            if (!CompressionKindJson.TryParse(compressionObj, out CompressionKind kind, out string errorCode))
            {
                compression = null;
                errorList.Add(new GamePatchKitError(Stage, PackageConfigErrorCodes.InvalidCompression, $"Group 'compression' is invalid: {errorCode}"));
                return false;
            }

            compression = kind;
            return true;
        }
    }
}
