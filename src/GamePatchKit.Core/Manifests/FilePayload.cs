using System;
using System.Collections.Generic;
using System.Linq;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Manifests
{
    public abstract class FilePayload
    {
        private const string Stage = "release-manifest";

        private static readonly HashSet<string> _singleProperties = new HashSet<string> { "kind", "path", "size", "artifactHash" };
        private static readonly HashSet<string> _partsProperties = new HashSet<string> { "kind", "size", "artifactHash", "parts" };

        private protected FilePayload()
        {
        }

        public abstract JObject ToJson();

        public static bool TryParse(JObject obj, out FilePayload? payload, out IReadOnlyList<GamePatchKitError> errors)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            if (!JsonReadHelpers.TryGetRequiredString(obj, "kind", out string kind))
            {
                payload = null;
                errors = new List<GamePatchKitError> { new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "File payload 'kind' must be a string.") };
                return false;
            }

            switch (kind)
            {
                case "single":
                    return TryParseSingle(obj, out payload, out errors);
                case "parts":
                    return TryParseParts(obj, out payload, out errors);
                default:
                    payload = null;
                    errors = new List<GamePatchKitError> { new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, $"File payload 'kind' must be 'single' or 'parts', was '{kind}'.") };
                    return false;
            }
        }

        private static bool TryParseSingle(JObject obj, out FilePayload? payload, out IReadOnlyList<GamePatchKitError> errors)
        {
            var errorList = new List<GamePatchKitError>();

            foreach (string unknown in JsonReadHelpers.FindUnknownProperties(obj, _singleProperties))
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.UnknownProperty, $"Unknown single-payload property '{unknown}'."));
            }

            bool hasPath = JsonReadHelpers.TryGetRequiredString(obj, "path", out string path);
            if (!hasPath)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Single payload 'path' must be a string."));
            }

            bool hasSize = JsonReadHelpers.TryGetRequiredInteger(obj, "size", out long size) && size >= 0;
            if (!hasSize)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Single payload 'size' must be a non-negative integer."));
            }

            bool hasHash = JsonReadHelpers.TryGetRequiredString(obj, "artifactHash", out string artifactHash) && Hex64.IsValid(artifactHash);
            if (!hasHash)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Single payload 'artifactHash' must be lowercase hex64."));
            }

            if (!hasPath || !hasSize || !hasHash)
            {
                payload = null;
                errors = errorList;
                return false;
            }

            payload = new Single(path, size, artifactHash);
            errors = errorList;
            return true;
        }

        private static bool TryParseParts(JObject obj, out FilePayload? payload, out IReadOnlyList<GamePatchKitError> errors)
        {
            var errorList = new List<GamePatchKitError>();

            foreach (string unknown in JsonReadHelpers.FindUnknownProperties(obj, _partsProperties))
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.UnknownProperty, $"Unknown parts-payload property '{unknown}'."));
            }

            bool hasSize = JsonReadHelpers.TryGetRequiredInteger(obj, "size", out long size) && size >= 0;
            if (!hasSize)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Parts payload 'size' must be a non-negative integer."));
            }

            bool hasHash = JsonReadHelpers.TryGetRequiredString(obj, "artifactHash", out string artifactHash) && Hex64.IsValid(artifactHash);
            if (!hasHash)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Parts payload 'artifactHash' must be lowercase hex64."));
            }

            if (!JsonReadHelpers.TryGetRequiredArray(obj, "parts", out JArray partsArray) || partsArray.Count == 0)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Parts payload 'parts' must be a non-empty array."));
                payload = null;
                errors = errorList;
                return false;
            }

            var parts = new List<FilePart>();
            bool partsOk = true;

            foreach (JToken item in partsArray)
            {
                if (item.Type != JTokenType.Object)
                {
                    errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Each part must be an object."));
                    partsOk = false;
                    continue;
                }

                if (!FilePart.TryParse((JObject)item!, out FilePart? part, out IReadOnlyList<GamePatchKitError> partErrors))
                {
                    errorList.AddRange(partErrors);
                    partsOk = false;
                    continue;
                }

                parts.Add(part!);
            }

            if (!hasSize || !hasHash || !partsOk)
            {
                payload = null;
                errors = errorList;
                return false;
            }

            payload = new Parts(size, artifactHash, parts);
            errors = errorList;
            return true;
        }

        public sealed class Single : FilePayload
        {
            public string Path { get; }

            public long Size { get; }

            public string ArtifactHash { get; }

            public Single(string path, long size, string artifactHash)
            {
                Path = path ?? throw new ArgumentNullException(nameof(path));
                Size = size;
                ArtifactHash = artifactHash ?? throw new ArgumentNullException(nameof(artifactHash));
            }

            public override JObject ToJson()
            {
                return new JObject { ["kind"] = "single", ["path"] = Path, ["size"] = Size, ["artifactHash"] = ArtifactHash };
            }
        }

        public sealed class Parts : FilePayload
        {
            public long Size { get; }

            public string ArtifactHash { get; }

            public IReadOnlyList<FilePart> PartList { get; }

            public Parts(long size, string artifactHash, IReadOnlyList<FilePart> partList)
            {
                Size = size;
                ArtifactHash = artifactHash ?? throw new ArgumentNullException(nameof(artifactHash));
                PartList = partList ?? throw new ArgumentNullException(nameof(partList));
            }

            public override JObject ToJson()
            {
                var array = new JArray(PartList.Select(part => part.ToJson()));
                return new JObject { ["kind"] = "parts", ["size"] = Size, ["artifactHash"] = ArtifactHash, ["parts"] = array };
            }
        }
    }
}
