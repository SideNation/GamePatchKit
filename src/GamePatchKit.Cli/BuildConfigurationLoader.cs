using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GamePatchKit.Cli;

internal static class BuildConfigurationLoader
{
    public const string FILE_NAME = "gamepatchkit.yml";

    private const string GroupValue = "group";
    private const string FileValue = "file";
    private const string ZstdValue = "zstd";
    private const string NoneValue = "none";

    public static BuildConfiguration Load(string path)
    {
        RawBuildConfiguration? rawConfiguration;

        try
        {
            IDeserializer deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithDuplicateKeyChecking()
                .Build();
            rawConfiguration = deserializer.Deserialize<RawBuildConfiguration>(File.ReadAllText(path));
        }
        catch (YamlException exception)
        {
            throw new BuildException($"gamepatchkit.yml이 올바르지 않습니다. {exception.Message}");
        }

        if (rawConfiguration?.Groups is null)
        {
            throw new BuildException("gamepatchkit.yml이 올바르지 않습니다. groups가 필요합니다.");
        }

        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        var groups = new List<GroupConfiguration>(rawConfiguration.Groups.Count);

        for (int groupIndex = 0; groupIndex < rawConfiguration.Groups.Count; groupIndex++)
        {
            RawGroupConfiguration? rawGroup = rawConfiguration.Groups[groupIndex];

            if (rawGroup is null)
            {
                throw InvalidGroup(groupIndex, "그룹 값이 필요합니다.");
            }

            if (!RelativePathValidator.IsNormalized(rawGroup.Id, allowRepositoryRoot: false))
            {
                throw InvalidGroup(groupIndex, "id는 정규화된 상대 경로여야 합니다.", "id");
            }

            if (!groupIds.Add(rawGroup.Id!))
            {
                throw InvalidGroup(groupIndex, "id가 중복되었습니다.", "id");
            }

            if (rawGroup.Version < 0)
            {
                throw InvalidGroup(groupIndex, "version은 0 이상이어야 합니다.", "version");
            }

            groups.Add(
                new GroupConfiguration
                {
                    Id = rawGroup.Id!,
                    Version = rawGroup.Version,
                    Packing = ParsePacking(rawGroup.Packing, groupIndex),
                    Compression = ParseCompression(rawGroup.Compression, groupIndex)
                });
        }

        return new BuildConfiguration
        {
            Groups = groups
        };
    }

    private static PackingKind ParsePacking(string? value, int groupIndex)
    {
        return value switch
        {
            GroupValue => PackingKind.Group,
            FileValue => PackingKind.File,
            _ => throw InvalidGroup(groupIndex, "packing은 group 또는 file이어야 합니다.", "packing")
        };
    }

    private static CompressionKind ParseCompression(string? value, int groupIndex)
    {
        return value switch
        {
            ZstdValue => CompressionKind.Zstd,
            NoneValue => CompressionKind.None,
            _ => throw InvalidGroup(groupIndex, "compression은 zstd 또는 none이어야 합니다.", "compression")
        };
    }

    private static BuildException InvalidGroup(int groupIndex, string reason, string? property = null)
    {
        string path = property is null ? $"groups[{groupIndex}]" : $"groups[{groupIndex}].{property}";
        return new BuildException($"gamepatchkit.yml이 올바르지 않습니다. {path}: {reason}");
    }

    private sealed class RawBuildConfiguration
    {
        public List<RawGroupConfiguration?>? Groups { get; set; }
    }

    private sealed class RawGroupConfiguration
    {
        public string? Id { get; set; }
        public int Version { get; set; }
        public string Packing { get; set; } = GroupValue;
        public string Compression { get; set; } = ZstdValue;
    }
}