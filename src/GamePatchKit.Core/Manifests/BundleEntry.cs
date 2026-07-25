using System;
using System.Collections.Generic;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Manifests
{
    public sealed class BundleEntry
    {
        private const string Stage = "release-manifest";

        private static readonly HashSet<string> _knownProperties = new HashSet<string> { "path" };

        public string Path { get; }

        public BundleEntry(string path)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public static bool TryParse(JObject obj, out BundleEntry? entry, out IReadOnlyList<GamePatchKitError> errors)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            var errorList = new List<GamePatchKitError>();

            foreach (string unknown in JsonReadHelpers.FindUnknownProperties(obj, _knownProperties))
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.UnknownProperty, $"Unknown bundle entry property '{unknown}'."));
            }

            if (!JsonReadHelpers.TryGetRequiredString(obj, "path", out string path))
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Bundle entry 'path' must be a string."));
                entry = null;
                errors = errorList;
                return false;
            }

            entry = new BundleEntry(path);
            errors = errorList;
            return true;
        }

        public JObject ToJson()
        {
            return new JObject { ["path"] = Path };
        }
    }
}
