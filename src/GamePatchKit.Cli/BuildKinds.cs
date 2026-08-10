using Newtonsoft.Json;

namespace GamePatchKit.Cli;

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
        if (reader.TokenType != JsonToken.String || reader.Value is not string value)
        {
            throw new JsonSerializationException($"{objectType.Name} 값은 소문자 문자열이어야 합니다.");
        }

        if (objectType == typeof(PackingKind))
        {
            return value switch
            {
                GroupValue => PackingKind.Group,
                FileValue => PackingKind.File,
                _ => throw new JsonSerializationException($"올바르지 않은 packing 값입니다: {value}")
            };
        }

        if (objectType == typeof(CompressionKind))
        {
            return value switch
            {
                ZstdValue => CompressionKind.Zstd,
                NoneValue => CompressionKind.None,
                _ => throw new JsonSerializationException($"올바르지 않은 compression 값입니다: {value}")
            };
        }

        return value switch
        {
            ArchiveValue => EntrySource.Archive,
            FileValue => EntrySource.File,
            _ => throw new JsonSerializationException($"올바르지 않은 source 값입니다: {value}")
        };
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        string serialized = value switch
        {
            PackingKind.Group => GroupValue,
            PackingKind.File => FileValue,
            CompressionKind.Zstd => ZstdValue,
            CompressionKind.None => NoneValue,
            EntrySource.Archive => ArchiveValue,
            EntrySource.File => FileValue,
            _ => throw new JsonSerializationException("지원하지 않는 enum 값입니다.")
        };
        writer.WriteValue(serialized);
    }
}