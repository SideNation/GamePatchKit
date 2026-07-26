using System;
using System.Collections.Generic;
using GamePatchKit.Core.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core
{
    // Shared wire mapping for the {kind:"none"} / {kind:"zstd",codecId:"zstd"} shape used by both
    // package-config.schema.json's compressionSetting and release-manifest.schema.json's compressionMetadata.
    public static class CompressionKindJson
    {
        private const string KindPropertyName = "kind";
        private const string CodecIdPropertyName = "codecId";
        private const string NoneKindValue = "none";
        private const string ZstdKindValue = "zstd";
        private const string ZstdCodecIdValue = CompressionCodecIds.Zstd;

        private static readonly HashSet<string> _noneProperties = new HashSet<string> { KindPropertyName };
        private static readonly HashSet<string> _zstdProperties = new HashSet<string> { KindPropertyName, CodecIdPropertyName };

        public static bool TryParse(JObject obj, out CompressionKind kind, out string errorCode)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            if (!JsonReadHelpers.TryGetRequiredString(obj, KindPropertyName, out string kindValue))
            {
                kind = CompressionKind.None;
                errorCode = CompressionErrorCodes.MissingKind;
                return false;
            }

            if (kindValue == NoneKindValue)
            {
                if (JsonReadHelpers.FindUnknownProperties(obj, _noneProperties).Count > 0)
                {
                    kind = CompressionKind.None;
                    errorCode = CompressionErrorCodes.UnknownProperty;
                    return false;
                }

                kind = CompressionKind.None;
                errorCode = string.Empty;
                return true;
            }

            if (kindValue == ZstdKindValue)
            {
                if (JsonReadHelpers.FindUnknownProperties(obj, _zstdProperties).Count > 0)
                {
                    kind = CompressionKind.None;
                    errorCode = CompressionErrorCodes.UnknownProperty;
                    return false;
                }

                if (!JsonReadHelpers.TryGetRequiredString(obj, CodecIdPropertyName, out string codecId))
                {
                    kind = CompressionKind.None;
                    errorCode = CompressionErrorCodes.MissingCodecId;
                    return false;
                }

                if (codecId != ZstdCodecIdValue)
                {
                    kind = CompressionKind.None;
                    errorCode = CompressionErrorCodes.InvalidCodecId;
                    return false;
                }

                kind = CompressionKind.Zstd;
                errorCode = string.Empty;
                return true;
            }

            kind = CompressionKind.None;
            errorCode = CompressionErrorCodes.UnknownKind;
            return false;
        }

        public static JObject ToJson(CompressionKind kind)
        {
            switch (kind)
            {
                case CompressionKind.None:
                    return new JObject { [KindPropertyName] = NoneKindValue };
                case CompressionKind.Zstd:
                    return new JObject { [KindPropertyName] = ZstdKindValue, [CodecIdPropertyName] = ZstdCodecIdValue };
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }
}
