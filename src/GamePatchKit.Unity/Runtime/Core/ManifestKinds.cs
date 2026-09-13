#nullable enable
using System;
using Newtonsoft.Json;

namespace GamePatchKit.Unity
{
    internal enum PackingKind
    {
        Group,
        File
    }

    internal enum CompressionKind
    {
        Zstd,
        None
    }

    internal enum EntrySource
    {
        Archive,
        File
    }

    // CLI의 LowercaseEnumConverter와 같은 값 집합이다. 매니페스트 필드 이름과 값은 CLI가 고정하므로 여기서도
    // 이름 자동 추론에 기대지 않는다.
    internal sealed class LowercaseEnumConverter : JsonConverter
    {
        private const string GroupValue = "group";
        private const string FileValue = "file";
        private const string ZstdValue = "zstd";
        private const string NoneValue = "none";
        private const string ArchiveValue = "archive";

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(PackingKind)
                || objectType == typeof(CompressionKind)
                || objectType == typeof(EntrySource);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType != JsonToken.String || !(reader.Value is string value))
            {
                throw new JsonSerializationException($"{objectType.Name} 값은 소문자 문자열이어야 합니다.");
            }

            if (objectType == typeof(PackingKind))
            {
                switch (value)
                {
                    case GroupValue:
                        return PackingKind.Group;
                    case FileValue:
                        return PackingKind.File;
                    default:
                        throw new JsonSerializationException($"올바르지 않은 packing 값입니다: {value}");
                }
            }

            if (objectType == typeof(CompressionKind))
            {
                switch (value)
                {
                    case ZstdValue:
                        return CompressionKind.Zstd;
                    case NoneValue:
                        return CompressionKind.None;
                    default:
                        throw new JsonSerializationException($"올바르지 않은 compression 값입니다: {value}");
                }
            }

            switch (value)
            {
                case ArchiveValue:
                    return EntrySource.Archive;
                case FileValue:
                    return EntrySource.File;
                default:
                    throw new JsonSerializationException($"올바르지 않은 source 값입니다: {value}");
            }
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            switch (value)
            {
                case PackingKind.Group:
                    writer.WriteValue(GroupValue);
                    break;
                case PackingKind.File:
                    writer.WriteValue(FileValue);
                    break;
                case CompressionKind.Zstd:
                    writer.WriteValue(ZstdValue);
                    break;
                case CompressionKind.None:
                    writer.WriteValue(NoneValue);
                    break;
                case EntrySource.Archive:
                    writer.WriteValue(ArchiveValue);
                    break;
                case EntrySource.File:
                    writer.WriteValue(FileValue);
                    break;
                default:
                    throw new JsonSerializationException("지원하지 않는 enum 값입니다.");
            }
        }
    }
}
