using Newtonsoft.Json;

namespace GamePatchKit.Cli;

internal sealed record ManifestArtifact(string Name, long StoredSize, string Checksum);

[JsonObject(MemberSerialization.OptIn)]
internal sealed class PatchManifest
{
    public const int CURRENT_SCHEMA_VERSION = 1;

    [JsonProperty("schemaVersion", Required = Required.Always, Order = 0)]
    public int SchemaVersion { get; init; } = CURRENT_SCHEMA_VERSION;

    [JsonProperty("releaseVersion", Required = Required.Always, Order = 1)]
    public long ReleaseVersion { get; init; }

    [JsonProperty("sourcePath", Required = Required.Always, Order = 2)]
    public string SourcePath { get; init; } = null!;

    [JsonProperty("sourceCommit", Required = Required.Always, Order = 3)]
    public string SourceCommit { get; init; } = null!;

    [JsonProperty("groups", Required = Required.Always, Order = 4)]
    public IReadOnlyList<ManifestGroup> Groups { get; init; } = null!;

    public IEnumerable<ManifestArtifact> EnumerateArtifacts()
    {
        foreach (ManifestGroup group in Groups)
        {
            if (group.Archive is not null)
            {
                yield return new ManifestArtifact(group.Archive.Name, group.Archive.StoredSize, group.Archive.Checksum);
            }

            foreach (ManifestEntry entry in group.Entries)
            {
                if (entry.Source == EntrySource.File)
                {
                    yield return new ManifestArtifact(entry.Name!, entry.StoredSize!.Value, entry.Checksum!);
                }
            }
        }
    }
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class ManifestGroup
{
    [JsonProperty("id", Required = Required.Always, Order = 0)]
    public string Id { get; init; } = null!;

    [JsonProperty("version", Required = Required.Always, Order = 1)]
    public int Version { get; init; }

    [JsonProperty("packing", Required = Required.Always, Order = 2)]
    public PackingKind Packing { get; init; }

    [JsonProperty("compression", Required = Required.Always, Order = 3)]
    public CompressionKind Compression { get; init; }

    [JsonProperty("archive", Order = 4, NullValueHandling = NullValueHandling.Ignore)]
    public ManifestArchive? Archive { get; init; }

    [JsonProperty("entries", Required = Required.Always, Order = 5)]
    public IReadOnlyList<ManifestEntry> Entries { get; init; } = null!;
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class ManifestArchive
{
    [JsonProperty("name", Required = Required.Always, Order = 0)]
    public string Name { get; init; } = null!;

    [JsonProperty("payloadSize", Required = Required.Always, Order = 1)]
    public long PayloadSize { get; init; }

    [JsonProperty("storedSize", Required = Required.Always, Order = 2)]
    public long StoredSize { get; init; }

    [JsonProperty("checksum", Required = Required.Always, Order = 3)]
    public string Checksum { get; init; } = null!;
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class ManifestEntry
{
    [JsonProperty("path", Required = Required.Always, Order = 0)]
    public string Path { get; init; } = null!;

    [JsonProperty("version", Required = Required.Always, Order = 1)]
    public string Version { get; init; } = null!;

    [JsonProperty("size", Required = Required.Always, Order = 2)]
    public long Size { get; init; }

    [JsonProperty("source", Required = Required.Always, Order = 3)]
    public EntrySource Source { get; init; }

    [JsonProperty("offset", Order = 4, NullValueHandling = NullValueHandling.Ignore)]
    public long? Offset { get; init; }

    [JsonProperty("length", Order = 5, NullValueHandling = NullValueHandling.Ignore)]
    public long? Length { get; init; }

    [JsonProperty("name", Order = 6, NullValueHandling = NullValueHandling.Ignore)]
    public string? Name { get; init; }

    [JsonProperty("storedSize", Order = 7, NullValueHandling = NullValueHandling.Ignore)]
    public long? StoredSize { get; init; }

    [JsonProperty("checksum", Order = 8, NullValueHandling = NullValueHandling.Ignore)]
    public string? Checksum { get; init; }
}