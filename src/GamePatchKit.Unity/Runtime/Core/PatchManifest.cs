#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;

namespace GamePatchKit.Unity
{
    internal sealed class ManifestArtifact
    {
        public ManifestArtifact(string name, long storedSize, string checksum)
        {
            Name = name;
            StoredSize = storedSize;
            Checksum = checksum;
        }

        public string Name { get; }

        public long StoredSize { get; }

        public string Checksum { get; }
    }

    // CLI의 PatchManifest와 같은 필드·이름·순서를 읽는 소비 측 모델이다. 필드 이름은 [JsonProperty]로 고정한다.
    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class PatchManifest
    {
        public const int CURRENT_SCHEMA_VERSION = 1;

        [JsonProperty("schemaVersion", Required = Required.Always, Order = 0)]
        public int SchemaVersion { get; set; } = CURRENT_SCHEMA_VERSION;

        [JsonProperty("releaseVersion", Required = Required.Always, Order = 1)]
        public long ReleaseVersion { get; set; }

        [JsonProperty("sourcePath", Required = Required.Always, Order = 2)]
        public string SourcePath { get; set; } = null!;

        [JsonProperty("sourceCommit", Required = Required.Always, Order = 3)]
        public string SourceCommit { get; set; } = null!;

        [JsonProperty("groups", Required = Required.Always, Order = 4)]
        public IReadOnlyList<ManifestGroup> Groups { get; set; } = null!;

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
        public string Id { get; set; } = null!;

        [JsonProperty("version", Required = Required.Always, Order = 1)]
        public int Version { get; set; }

        [JsonProperty("packing", Required = Required.Always, Order = 2)]
        public PackingKind Packing { get; set; }

        [JsonProperty("compression", Required = Required.Always, Order = 3)]
        public CompressionKind Compression { get; set; }

        [JsonProperty("archive", Order = 4, NullValueHandling = NullValueHandling.Ignore)]
        public ManifestArchive? Archive { get; set; }

        [JsonProperty("entries", Required = Required.Always, Order = 5)]
        public IReadOnlyList<ManifestEntry> Entries { get; set; } = null!;
    }

    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class ManifestArchive
    {
        [JsonProperty("name", Required = Required.Always, Order = 0)]
        public string Name { get; set; } = null!;

        [JsonProperty("payloadSize", Required = Required.Always, Order = 1)]
        public long PayloadSize { get; set; }

        [JsonProperty("storedSize", Required = Required.Always, Order = 2)]
        public long StoredSize { get; set; }

        [JsonProperty("checksum", Required = Required.Always, Order = 3)]
        public string Checksum { get; set; } = null!;
    }

    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class ManifestEntry
    {
        [JsonProperty("path", Required = Required.Always, Order = 0)]
        public string Path { get; set; } = null!;

        [JsonProperty("version", Required = Required.Always, Order = 1)]
        public string Version { get; set; } = null!;

        [JsonProperty("size", Required = Required.Always, Order = 2)]
        public long Size { get; set; }

        [JsonProperty("source", Required = Required.Always, Order = 3)]
        public EntrySource Source { get; set; }

        [JsonProperty("offset", Order = 4, NullValueHandling = NullValueHandling.Ignore)]
        public long? Offset { get; set; }

        [JsonProperty("length", Order = 5, NullValueHandling = NullValueHandling.Ignore)]
        public long? Length { get; set; }

        [JsonProperty("name", Order = 6, NullValueHandling = NullValueHandling.Ignore)]
        public string? Name { get; set; }

        [JsonProperty("storedSize", Order = 7, NullValueHandling = NullValueHandling.Ignore)]
        public long? StoredSize { get; set; }

        [JsonProperty("checksum", Order = 8, NullValueHandling = NullValueHandling.Ignore)]
        public string? Checksum { get; set; }
    }
}
