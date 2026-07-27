using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GamePatchKit.Core;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;
using GamePatchKit.Core.Paths;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Runtime
{
    public static class PackageStateSerializer
    {
        private const string Stage = "package-state";

        private static readonly UTF8Encoding _strictUtf8 =
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        private static readonly HashSet<string> _rootProperties =
            new HashSet<string> { "schemaVersion", "stateRevision", "packageId", "active", "groups" };

        private static readonly HashSet<string> _activeProperties =
            new HashSet<string> { "dataVersion", "manifestHash" };

        private static readonly HashSet<string> _groupProperties =
            new HashSet<string> { "name", "status", "verifiedManifestHash", "installationKey" };

        public static byte[] Serialize(PackageState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            return CanonicalJsonWriter.Write(ToJson(state));
        }

        public static bool TryDeserialize(
            byte[] canonicalBytes,
            out PackageState? state,
            out IReadOnlyList<GamePatchKitError> errors)
        {
            if (canonicalBytes == null)
            {
                throw new ArgumentNullException(nameof(canonicalBytes));
            }

            var errorList = new List<GamePatchKitError>();

            if (!TryReadObject(canonicalBytes, out JObject? root))
            {
                state = null;
                errors = Failure("PackageState must be one UTF-8 JSON object.");
                return false;
            }

            foreach (string property in JsonReadHelpers.FindUnknownProperties(root!, _rootProperties))
            {
                errorList.Add(Error($"Unknown PackageState property '{property}'."));
            }

            bool hasSchemaVersion =
                JsonReadHelpers.TryGetRequiredInteger(root!, "schemaVersion", out long schemaVersion)
                && schemaVersion == PackageState.CurrentSchemaVersion;
            bool hasRevision =
                JsonReadHelpers.TryGetRequiredInteger(root!, "stateRevision", out long stateRevision)
                && stateRevision >= 1;
            bool hasPackageId =
                JsonReadHelpers.TryGetRequiredString(root!, "packageId", out string packageId)
                && KebabCaseId.IsValid(packageId);
            bool hasActive = TryReadActive(root!, errorList, out PackageActiveState? active);
            bool hasGroups = TryReadGroups(root!, errorList, out List<PackageGroupState> groups);

            if (!hasSchemaVersion)
            {
                errorList.Add(Error($"'schemaVersion' must be {PackageState.CurrentSchemaVersion}."));
            }

            if (!hasRevision)
            {
                errorList.Add(Error("'stateRevision' must be a positive I-JSON safe integer."));
            }

            if (!hasPackageId)
            {
                errorList.Add(Error("'packageId' must be lowercase kebab-case."));
            }

            if (!hasSchemaVersion || !hasRevision || !hasPackageId || !hasActive || !hasGroups || errorList.Count > 0)
            {
                state = null;
                errors = errorList;
                return false;
            }

            var parsed = new PackageState(
                (int)schemaVersion,
                stateRevision,
                packageId,
                active!,
                groups);
            byte[] reserialized = Serialize(parsed);

            if (!canonicalBytes.SequenceEqual(reserialized))
            {
                state = null;
                errors = Failure("PackageState bytes are not canonical JSON or groups[] is not in canonical order.");
                return false;
            }

            state = parsed;
            errors = Array.Empty<GamePatchKitError>();
            return true;
        }

        private static JObject ToJson(PackageState state)
        {
            var groups = new JArray(
                state.Groups
                    .OrderBy(group => group.Name, Utf8OrdinalStringComparer.Instance)
                    .Select(ToJson));

            return new JObject
            {
                ["schemaVersion"] = state.SchemaVersion,
                ["stateRevision"] = state.StateRevision,
                ["packageId"] = state.PackageId,
                ["active"] = new JObject
                {
                    ["dataVersion"] = state.Active.DataVersion,
                    ["manifestHash"] = state.Active.ManifestHash,
                },
                ["groups"] = groups,
            };
        }

        private static JObject ToJson(PackageGroupState group)
        {
            var json = new JObject
            {
                ["name"] = group.Name,
                ["status"] = ToWireStatus(group.Status),
            };

            if (group.Status != PackageGroupStatus.NotInstalled)
            {
                json["verifiedManifestHash"] = group.VerifiedManifestHash;
                json["installationKey"] = group.InstallationKey;
            }

            return json;
        }

        private static bool TryReadObject(byte[] bytes, out JObject? root)
        {
            root = null;

            try
            {
                string json = _strictUtf8.GetString(bytes);
                using var stringReader = new StringReader(json);
                using var reader = new JsonTextReader(stringReader)
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Decimal,
                    SupportMultipleContent = false,
                };
                JToken token = JToken.ReadFrom(
                    reader,
                    new JsonLoadSettings
                    {
                        CommentHandling = CommentHandling.Ignore,
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    });

                if (token.Type != JTokenType.Object || reader.Read())
                {
                    return false;
                }

                root = (JObject)token;
                return true;
            }
            catch (Exception exception) when (
                exception is DecoderFallbackException
                || exception is JsonException
                || exception is InvalidOperationException)
            {
                return false;
            }
        }

        private static bool TryReadActive(
            JObject root,
            List<GamePatchKitError> errors,
            out PackageActiveState? active)
        {
            active = null;

            if (!JsonReadHelpers.TryGetRequiredObject(root, "active", out JObject activeJson))
            {
                errors.Add(Error("'active' must be an object."));
                return false;
            }

            foreach (string property in JsonReadHelpers.FindUnknownProperties(activeJson, _activeProperties))
            {
                errors.Add(Error($"Unknown active property '{property}'."));
            }

            bool hasDataVersion =
                JsonReadHelpers.TryGetRequiredString(activeJson, "dataVersion", out string dataVersion)
                && DataVersionFormat.IsValid(dataVersion);
            bool hasManifestHash =
                JsonReadHelpers.TryGetRequiredString(activeJson, "manifestHash", out string manifestHash)
                && Hex64.IsValid(manifestHash);

            if (!hasDataVersion)
            {
                errors.Add(Error("'active.dataVersion' is invalid."));
            }

            if (!hasManifestHash)
            {
                errors.Add(Error("'active.manifestHash' must be lowercase hex64."));
            }

            if (!hasDataVersion || !hasManifestHash)
            {
                return false;
            }

            active = new PackageActiveState(dataVersion, manifestHash);
            return true;
        }

        private static bool TryReadGroups(
            JObject root,
            List<GamePatchKitError> errors,
            out List<PackageGroupState> groups)
        {
            groups = new List<PackageGroupState>();

            if (!JsonReadHelpers.TryGetRequiredArray(root, "groups", out JArray groupArray))
            {
                errors.Add(Error("'groups' must be an array."));
                return false;
            }

            bool isValid = true;

            foreach (JToken token in groupArray)
            {
                if (token.Type != JTokenType.Object)
                {
                    errors.Add(Error("Every groups[] value must be an object."));
                    isValid = false;
                    continue;
                }

                if (!TryReadGroup((JObject)token, errors, out PackageGroupState? group))
                {
                    isValid = false;
                    continue;
                }

                groups.Add(group!);
            }

            return isValid;
        }

        private static bool TryReadGroup(
            JObject json,
            List<GamePatchKitError> errors,
            out PackageGroupState? group)
        {
            group = null;

            foreach (string property in JsonReadHelpers.FindUnknownProperties(json, _groupProperties))
            {
                errors.Add(Error($"Unknown group property '{property}'."));
            }

            bool hasName =
                JsonReadHelpers.TryGetRequiredString(json, "name", out string name)
                && KebabCaseId.IsValid(name);
            PackageGroupStatus status = PackageGroupStatus.NotInstalled;
            bool hasStatus =
                JsonReadHelpers.TryGetRequiredString(json, "status", out string statusText)
                && TryParseStatus(statusText, out status);

            if (!hasName)
            {
                errors.Add(Error("Group 'name' must be lowercase kebab-case."));
            }

            if (!hasStatus)
            {
                errors.Add(Error("Group 'status' must be 'ready', 'notInstalled', or 'stale'."));
            }

            if (!hasName || !hasStatus)
            {
                return false;
            }

            bool hasVerifiedHash = JsonReadHelpers.TryGetRequiredString(json, "verifiedManifestHash", out string verifiedHash);
            bool hasInstallationKey = JsonReadHelpers.TryGetRequiredString(json, "installationKey", out string installationKey);

            if (status == PackageGroupStatus.NotInstalled)
            {
                if (hasVerifiedHash || hasInstallationKey)
                {
                    errors.Add(Error($"notInstalled group '{name}' must not have verification fields."));
                    return false;
                }

                group = new PackageGroupState(name, status);
                return true;
            }

            if (!hasVerifiedHash || !Hex64.IsValid(verifiedHash) || !hasInstallationKey || string.IsNullOrWhiteSpace(installationKey))
            {
                errors.Add(Error($"Group '{name}' requires a valid verifiedManifestHash and installationKey."));
                return false;
            }

            group = new PackageGroupState(name, status, verifiedHash, installationKey);
            return true;
        }

        private static string ToWireStatus(PackageGroupStatus status)
        {
            switch (status)
            {
                case PackageGroupStatus.Ready:
                    return "ready";
                case PackageGroupStatus.NotInstalled:
                    return "notInstalled";
                case PackageGroupStatus.Stale:
                    return "stale";
                default:
                    throw new ArgumentOutOfRangeException(nameof(status));
            }
        }

        private static bool TryParseStatus(string value, out PackageGroupStatus status)
        {
            switch (value)
            {
                case "ready":
                    status = PackageGroupStatus.Ready;
                    return true;
                case "notInstalled":
                    status = PackageGroupStatus.NotInstalled;
                    return true;
                case "stale":
                    status = PackageGroupStatus.Stale;
                    return true;
                default:
                    status = PackageGroupStatus.NotInstalled;
                    return false;
            }
        }

        private static IReadOnlyList<GamePatchKitError> Failure(string message)
        {
            return new[] { Error(message) };
        }

        private static GamePatchKitError Error(string message)
        {
            return new GamePatchKitError(Stage, RuntimeErrorCodes.StateInvalid, message);
        }
    }
}
