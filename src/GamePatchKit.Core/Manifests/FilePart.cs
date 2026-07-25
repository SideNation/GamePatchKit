using System;
using System.Collections.Generic;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Manifests
{
    public sealed class FilePart
    {
        private const string Stage = "release-manifest";

        private static readonly HashSet<string> _knownProperties = new HashSet<string> { "index", "path", "size", "partHash" };

        public long Index { get; }

        public string Path { get; }

        public long Size { get; }

        public string PartHash { get; }

        public FilePart(long index, string path, long size, string partHash)
        {
            Index = index;
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Size = size;
            PartHash = partHash ?? throw new ArgumentNullException(nameof(partHash));
        }

        public static bool TryParse(JObject obj, out FilePart? part, out IReadOnlyList<GamePatchKitError> errors)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            var errorList = new List<GamePatchKitError>();

            foreach (string unknown in JsonReadHelpers.FindUnknownProperties(obj, _knownProperties))
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.UnknownProperty, $"Unknown part property '{unknown}'."));
            }

            bool hasIndex = JsonReadHelpers.TryGetRequiredInteger(obj, "index", out long index) && index >= 0;
            if (!hasIndex)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Part 'index' must be a non-negative integer."));
            }

            bool hasPath = JsonReadHelpers.TryGetRequiredString(obj, "path", out string path);
            if (!hasPath)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Part 'path' must be a string."));
            }

            bool hasSize = JsonReadHelpers.TryGetRequiredInteger(obj, "size", out long size) && size >= 0;
            if (!hasSize)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Part 'size' must be a non-negative integer."));
            }

            bool hasPartHash = JsonReadHelpers.TryGetRequiredString(obj, "partHash", out string partHash) && Hex64.IsValid(partHash);
            if (!hasPartHash)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Part 'partHash' must be lowercase hex64."));
            }

            if (!hasIndex || !hasPath || !hasSize || !hasPartHash)
            {
                part = null;
                errors = errorList;
                return false;
            }

            part = new FilePart(index, path, size, partHash);
            errors = errorList;
            return true;
        }

        public JObject ToJson()
        {
            return new JObject { ["index"] = Index, ["path"] = Path, ["size"] = Size, ["partHash"] = PartHash };
        }
    }
}
