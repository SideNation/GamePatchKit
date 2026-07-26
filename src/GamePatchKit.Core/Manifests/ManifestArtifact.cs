using System;
using System.Collections.Generic;
using System.Linq;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Manifests
{
    public abstract class ManifestArtifact
    {
        private const string Stage = "release-manifest";

        private static readonly HashSet<string> _fileProperties = new HashSet<string> { "kind", "compression", "payload" };
        private static readonly HashSet<string> _bundleProperties = new HashSet<string>
        {
            "kind", "group", "path", "size", "artifactHash", "compression", "entries",
        };

        private protected ManifestArtifact()
        {
        }

        public abstract JObject ToJson();

        // Sort/uniqueness key: for file artifacts this is the shared directory all of that artifact's
        // payload files live under (there is no single top-level path field for "parts" payloads), for
        // bundle artifacts it is the artifact's own recorded path.
        public abstract string ContentAddressedSortKey(string packageId);

        // The objects this artifact is actually stored as, in canonical order: one per whole payload, one per
        // part. Diff and download planning work on these rather than on the artifact as a unit, because a
        // multipart artifact can be partially present locally.
        public abstract IReadOnlyList<ArtifactPayloadObject> GetPayloadObjects();

        public static bool TryParse(JObject obj, out ManifestArtifact? artifact, out IReadOnlyList<GamePatchKitError> errors)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            if (!JsonReadHelpers.TryGetRequiredString(obj, "kind", out string kind))
            {
                artifact = null;
                errors = new List<GamePatchKitError> { new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Artifact 'kind' must be a string.") };
                return false;
            }

            switch (kind)
            {
                case "file":
                    return TryParseFileArtifact(obj, out artifact, out errors);
                case "bundle":
                    return TryParseBundleArtifact(obj, out artifact, out errors);
                default:
                    artifact = null;
                    errors = new List<GamePatchKitError> { new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, $"Artifact 'kind' must be 'file' or 'bundle', was '{kind}'.") };
                    return false;
            }
        }

        private static bool TryParseFileArtifact(JObject obj, out ManifestArtifact? artifact, out IReadOnlyList<GamePatchKitError> errors)
        {
            var errorList = new List<GamePatchKitError>();

            foreach (string unknown in JsonReadHelpers.FindUnknownProperties(obj, _fileProperties))
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.UnknownProperty, $"Unknown file-artifact property '{unknown}'."));
            }

            bool hasCompression = TryParseCompression(obj, errorList, out CompressionKind compression);

            if (!JsonReadHelpers.TryGetRequiredObject(obj, "payload", out JObject payloadObj))
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "File artifact 'payload' must be an object."));
                artifact = null;
                errors = errorList;
                return false;
            }

            if (!FilePayload.TryParse(payloadObj, out FilePayload? payload, out IReadOnlyList<GamePatchKitError> payloadErrors))
            {
                errorList.AddRange(payloadErrors);
                artifact = null;
                errors = errorList;
                return false;
            }

            if (!hasCompression || errorList.Count > 0)
            {
                artifact = null;
                errors = errorList;
                return false;
            }

            artifact = new FileArtifact(compression, payload!);
            errors = errorList;
            return true;
        }

        private static bool TryParseBundleArtifact(JObject obj, out ManifestArtifact? artifact, out IReadOnlyList<GamePatchKitError> errors)
        {
            var errorList = new List<GamePatchKitError>();

            foreach (string unknown in JsonReadHelpers.FindUnknownProperties(obj, _bundleProperties))
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.UnknownProperty, $"Unknown bundle-artifact property '{unknown}'."));
            }

            bool hasGroup = JsonReadHelpers.TryGetRequiredString(obj, "group", out string group) && KebabCaseId.IsValid(group);
            if (!hasGroup)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Bundle artifact 'group' must be lowercase kebab-case."));
            }

            bool hasPath = JsonReadHelpers.TryGetRequiredString(obj, "path", out string path);
            if (!hasPath)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Bundle artifact 'path' must be a string."));
            }

            bool hasSize = JsonReadHelpers.TryGetRequiredInteger(obj, "size", out long size) && size >= 0;
            if (!hasSize)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Bundle artifact 'size' must be a non-negative integer."));
            }

            bool hasHash = JsonReadHelpers.TryGetRequiredString(obj, "artifactHash", out string artifactHash) && Hex64.IsValid(artifactHash);
            if (!hasHash)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Bundle artifact 'artifactHash' must be lowercase hex64."));
            }

            bool hasCompression = TryParseCompression(obj, errorList, out CompressionKind compression);

            if (!JsonReadHelpers.TryGetRequiredArray(obj, "entries", out JArray entriesArray) || entriesArray.Count == 0)
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Bundle artifact 'entries' must be a non-empty array."));
                artifact = null;
                errors = errorList;
                return false;
            }

            var entries = new List<BundleEntry>();
            bool entriesOk = true;

            foreach (JToken item in entriesArray)
            {
                if (item.Type != JTokenType.Object)
                {
                    errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "Each bundle entry must be an object."));
                    entriesOk = false;
                    continue;
                }

                if (!BundleEntry.TryParse((JObject)item!, out BundleEntry? entry, out IReadOnlyList<GamePatchKitError> entryErrors))
                {
                    errorList.AddRange(entryErrors);
                    entriesOk = false;
                    continue;
                }

                entries.Add(entry!);
            }

            if (!hasGroup || !hasPath || !hasSize || !hasHash || !hasCompression || !entriesOk || errorList.Count > 0)
            {
                artifact = null;
                errors = errorList;
                return false;
            }

            artifact = new BundleArtifact(group, path, size, artifactHash, compression, entries);
            errors = errorList;
            return true;
        }

        private static bool TryParseCompression(JObject obj, List<GamePatchKitError> errorList, out CompressionKind compression)
        {
            compression = CompressionKind.None;

            if (!JsonReadHelpers.TryGetRequiredObject(obj, "compression", out JObject compressionObj))
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, "'compression' must be an object."));
                return false;
            }

            if (!CompressionKindJson.TryParse(compressionObj, out compression, out string errorCode))
            {
                errorList.Add(new GamePatchKitError(Stage, ManifestErrorCodes.InvalidField, $"'compression' is invalid: {errorCode}"));
                return false;
            }

            return true;
        }

        public sealed class FileArtifact : ManifestArtifact
        {
            public CompressionKind Compression { get; }

            public FilePayload Payload { get; }

            public FileArtifact(CompressionKind compression, FilePayload payload)
            {
                Compression = compression;
                Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            }

            public string PrimaryArtifactHash => Payload is FilePayload.Single single ? single.ArtifactHash : ((FilePayload.Parts)Payload).ArtifactHash;

            public override string ContentAddressedSortKey(string packageId)
            {
                return ContentAddressedPath.FileArtifactDirectory(packageId, PrimaryArtifactHash);
            }

            public override IReadOnlyList<ArtifactPayloadObject> GetPayloadObjects()
            {
                if (Payload is FilePayload.Single single)
                {
                    return new List<ArtifactPayloadObject> { new ArtifactPayloadObject(single.Path, single.Size, single.ArtifactHash) };
                }

                var parts = (FilePayload.Parts)Payload;

                return parts.PartList
                    .Select(part => new ArtifactPayloadObject(part.Path, part.Size, part.PartHash))
                    .ToList();
            }

            public override JObject ToJson()
            {
                return new JObject
                {
                    ["kind"] = "file",
                    ["compression"] = CompressionKindJson.ToJson(Compression),
                    ["payload"] = Payload.ToJson(),
                };
            }
        }

        public sealed class BundleArtifact : ManifestArtifact
        {
            public string Group { get; }

            public string Path { get; }

            public long Size { get; }

            public string ArtifactHash { get; }

            public CompressionKind Compression { get; }

            public IReadOnlyList<BundleEntry> Entries { get; }

            public BundleArtifact(string group, string path, long size, string artifactHash, CompressionKind compression, IReadOnlyList<BundleEntry> entries)
            {
                Group = group ?? throw new ArgumentNullException(nameof(group));
                Path = path ?? throw new ArgumentNullException(nameof(path));
                Size = size;
                ArtifactHash = artifactHash ?? throw new ArgumentNullException(nameof(artifactHash));
                Compression = compression;
                Entries = entries ?? throw new ArgumentNullException(nameof(entries));
            }

            public override string ContentAddressedSortKey(string packageId)
            {
                return Path;
            }

            public override IReadOnlyList<ArtifactPayloadObject> GetPayloadObjects()
            {
                return new List<ArtifactPayloadObject> { new ArtifactPayloadObject(Path, Size, ArtifactHash) };
            }

            public override JObject ToJson()
            {
                var entriesArray = new JArray(Entries.Select(entry => entry.ToJson()));

                return new JObject
                {
                    ["kind"] = "bundle",
                    ["group"] = Group,
                    ["path"] = Path,
                    ["size"] = Size,
                    ["artifactHash"] = ArtifactHash,
                    ["compression"] = CompressionKindJson.ToJson(Compression),
                    ["entries"] = entriesArray,
                };
            }
        }
    }
}
