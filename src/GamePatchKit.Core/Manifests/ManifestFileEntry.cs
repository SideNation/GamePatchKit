using System;
using System.Collections.Generic;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Manifests
{
    public sealed class ManifestFileEntry
    {
        private const string Stage = "release-manifest";

        private static readonly HashSet<string> _knownProperties = new HashSet<string> { "path", "group", "size", "fileHash", "source" };

        public string Path { get; }

        public string Group { get; }

        public long Size { get; }

        public string FileHash { get; }

        public FileSource Source { get; }

        public ManifestFileEntry(string path, string group, long size, string fileHash, FileSource source)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Group = group ?? throw new ArgumentNullException(nameof(group));
            Size = size;
            FileHash = fileHash ?? throw new ArgumentNullException(nameof(fileHash));
            Source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public static bool TryParse(JObject obj, out ManifestFileEntry? entry, out IReadOnlyList<GamePatchKitError> errors)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            var errorList = new List<GamePatchKitError>();

            foreach (string unknown in JsonReadHelpers.FindUnknownProperties(obj, _knownProperties))
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.UnknownProperty, $"Unknown file property '{unknown}'."));
            }

            bool hasPath = JsonReadHelpers.TryGetRequiredString(obj, "path", out string path);
            if (!hasPath)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "File 'path' must be a string."));
            }

            bool hasGroup = JsonReadHelpers.TryGetRequiredString(obj, "group", out string group) && KebabCaseId.IsValid(group);
            if (!hasGroup)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "File 'group' must be lowercase kebab-case."));
            }

            bool hasSize = JsonReadHelpers.TryGetRequiredInteger(obj, "size", out long size) && size >= 0;
            if (!hasSize)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "File 'size' must be a non-negative integer."));
            }

            bool hasFileHash = JsonReadHelpers.TryGetRequiredString(obj, "fileHash", out string fileHash) && Hex64.IsValid(fileHash);
            if (!hasFileHash)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "File 'fileHash' must be lowercase hex64."));
            }

            if (!JsonReadHelpers.TryGetRequiredObject(obj, "source", out JObject sourceObj))
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "File 'source' must be an object."));
                entry = null;
                errors = errorList;
                return false;
            }

            if (!FileSource.TryParse(sourceObj, out FileSource? source, out IReadOnlyList<GamePatchKitError> sourceErrors))
            {
                errorList.AddRange(sourceErrors);
                entry = null;
                errors = errorList;
                return false;
            }

            if (!hasPath || !hasGroup || !hasSize || !hasFileHash)
            {
                entry = null;
                errors = errorList;
                return false;
            }

            entry = new ManifestFileEntry(path, group, size, fileHash, source!);
            errors = errorList;
            return true;
        }

        public JObject ToJson()
        {
            return new JObject
            {
                ["path"] = Path,
                ["group"] = Group,
                ["size"] = Size,
                ["fileHash"] = FileHash,
                ["source"] = Source.ToJson(),
            };
        }
    }
}
