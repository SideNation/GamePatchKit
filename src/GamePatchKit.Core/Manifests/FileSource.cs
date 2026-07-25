using System;
using System.Collections.Generic;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Manifests
{
    public abstract class FileSource
    {
        private const string Stage = "release-manifest";

        private static readonly HashSet<string> _fileProperties = new HashSet<string> { "kind", "artifactHash" };
        private static readonly HashSet<string> _bundleEntryProperties = new HashSet<string> { "kind", "artifactHash", "entryPath" };

        private protected FileSource()
        {
        }

        public abstract JObject ToJson();

        public static bool TryParse(JObject obj, out FileSource? source, out IReadOnlyList<GamePatchKitError> errors)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            if (!JsonReadHelpers.TryGetRequiredString(obj, "kind", out string kind))
            {
                source = null;
                errors = new List<GamePatchKitError> { new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "File source 'kind' must be a string.") };
                return false;
            }

            switch (kind)
            {
                case "file":
                    return TryParseFileReference(obj, out source, out errors);
                case "bundleEntry":
                    return TryParseBundleEntryReference(obj, out source, out errors);
                default:
                    source = null;
                    errors = new List<GamePatchKitError> { new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, $"File source 'kind' must be 'file' or 'bundleEntry', was '{kind}'.") };
                    return false;
            }
        }

        private static bool TryParseFileReference(JObject obj, out FileSource? source, out IReadOnlyList<GamePatchKitError> errors)
        {
            var errorList = new List<GamePatchKitError>();

            foreach (string unknown in JsonReadHelpers.FindUnknownProperties(obj, _fileProperties))
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.UnknownProperty, $"Unknown file-source property '{unknown}'."));
            }

            bool hasHash = JsonReadHelpers.TryGetRequiredString(obj, "artifactHash", out string artifactHash) && Hex64.IsValid(artifactHash);
            if (!hasHash)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "File-source 'artifactHash' must be lowercase hex64."));
            }

            if (!hasHash || errorList.Count > 0)
            {
                source = null;
                errors = errorList;
                return false;
            }

            source = new FileReference(artifactHash);
            errors = errorList;
            return true;
        }

        private static bool TryParseBundleEntryReference(JObject obj, out FileSource? source, out IReadOnlyList<GamePatchKitError> errors)
        {
            var errorList = new List<GamePatchKitError>();

            foreach (string unknown in JsonReadHelpers.FindUnknownProperties(obj, _bundleEntryProperties))
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.UnknownProperty, $"Unknown bundle-entry-source property '{unknown}'."));
            }

            bool hasHash = JsonReadHelpers.TryGetRequiredString(obj, "artifactHash", out string artifactHash) && Hex64.IsValid(artifactHash);
            if (!hasHash)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Bundle-entry-source 'artifactHash' must be lowercase hex64."));
            }

            bool hasEntryPath = JsonReadHelpers.TryGetRequiredString(obj, "entryPath", out string entryPath);
            if (!hasEntryPath)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Bundle-entry-source 'entryPath' must be a string."));
            }

            if (!hasHash || !hasEntryPath || errorList.Count > 0)
            {
                source = null;
                errors = errorList;
                return false;
            }

            source = new BundleEntryReference(artifactHash, entryPath);
            errors = errorList;
            return true;
        }

        public sealed class FileReference : FileSource
        {
            public string ArtifactHash { get; }

            public FileReference(string artifactHash)
            {
                ArtifactHash = artifactHash ?? throw new ArgumentNullException(nameof(artifactHash));
            }

            public override JObject ToJson()
            {
                return new JObject { ["kind"] = "file", ["artifactHash"] = ArtifactHash };
            }
        }

        public sealed class BundleEntryReference : FileSource
        {
            public string ArtifactHash { get; }

            public string EntryPath { get; }

            public BundleEntryReference(string artifactHash, string entryPath)
            {
                ArtifactHash = artifactHash ?? throw new ArgumentNullException(nameof(artifactHash));
                EntryPath = entryPath ?? throw new ArgumentNullException(nameof(entryPath));
            }

            public override JObject ToJson()
            {
                return new JObject { ["kind"] = "bundleEntry", ["artifactHash"] = ArtifactHash, ["entryPath"] = EntryPath };
            }
        }
    }
}
