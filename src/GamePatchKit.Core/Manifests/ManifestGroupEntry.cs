using System;
using System.Collections.Generic;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Manifests
{
    public sealed class ManifestGroupEntry
    {
        private const string Stage = "release-manifest";

        private static readonly HashSet<string> _knownProperties = new HashSet<string> { "name", "required" };

        public string Name { get; }

        public bool Required { get; }

        public ManifestGroupEntry(string name, bool required)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Required = required;
        }

        public static bool TryParse(JObject obj, out ManifestGroupEntry? entry, out IReadOnlyList<GamePatchKitError> errors)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            var errorList = new List<GamePatchKitError>();

            foreach (string unknown in JsonReadHelpers.FindUnknownProperties(obj, _knownProperties))
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.UnknownProperty, $"Unknown group property '{unknown}'."));
            }

            bool hasName = JsonReadHelpers.TryGetRequiredString(obj, "name", out string name) && KebabCaseId.IsValid(name);
            if (!hasName)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Group 'name' must be lowercase kebab-case."));
            }

            bool hasRequired = JsonReadHelpers.TryGetRequiredBoolean(obj, "required", out bool required);
            if (!hasRequired)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Group 'required' must be a boolean."));
            }

            if (!hasName || !hasRequired || errorList.Count > 0)
            {
                entry = null;
                errors = errorList;
                return false;
            }

            entry = new ManifestGroupEntry(name, required);
            errors = errorList;
            return true;
        }

        public JObject ToJson()
        {
            return new JObject { ["name"] = Name, ["required"] = Required };
        }
    }
}
