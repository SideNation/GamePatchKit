using GamePatchKit.Cli;

namespace GamePatchKit.Cli.Tests;

public sealed class TestBuildConfiguration
{
    [Fact]
    public void Load_MinimumYaml_UsesDefaults()
    {
        const string yaml = """
            groups:
              - id: data/maps
            """;

        BuildConfiguration result = LoadYaml(yaml);

        GroupConfiguration group = Assert.Single(result.Groups);
        Assert.Equal("data/maps", group.Id);
        Assert.Equal(0, group.Version);
        Assert.Equal(PackingKind.Group, group.Packing);
        Assert.Equal(CompressionKind.Zstd, group.Compression);
    }

    [Fact]
    public void Load_AllOptionsYaml_ReturnsConfiguredValues()
    {
        const string yaml = """
            groups:
              - id: data
                version: 3
                packing: file
                compression: none
            """;

        BuildConfiguration result = LoadYaml(yaml);

        GroupConfiguration group = Assert.Single(result.Groups);
        Assert.Equal("data", group.Id);
        Assert.Equal(3, group.Version);
        Assert.Equal(PackingKind.File, group.Packing);
        Assert.Equal(CompressionKind.None, group.Compression);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("/data")]
    [InlineData("C:/data")]
    [InlineData("../data")]
    [InlineData("data/../maps")]
    [InlineData("data//maps")]
    [InlineData("data/")]
    [InlineData("data\\maps")]
    public void Load_GroupIdIsNotNormalizedRelativePath_ThrowsBuildException(string groupId)
    {
        string yaml = $"groups:\n  - id: '{groupId}'\n";

        Assert.Throws<BuildException>(() => LoadYaml(yaml));
    }

    [Fact]
    public void Load_GroupIdIsDuplicated_ThrowsBuildException()
    {
        const string yaml = """
            groups:
              - id: data
              - id: data
            """;

        Assert.Throws<BuildException>(() => LoadYaml(yaml));
    }

    [Theory]
    [InlineData("packing", "archive")]
    [InlineData("packing", "Group")]
    [InlineData("compression", "gzip")]
    [InlineData("compression", "Zstd")]
    public void Load_EnumValueIsInvalid_ThrowsBuildException(string property, string value)
    {
        string yaml = $"groups:\n  - id: data\n    {property}: {value}\n";

        Assert.Throws<BuildException>(() => LoadYaml(yaml));
    }

    [Fact]
    public void Load_VersionIsNegative_ThrowsBuildException()
    {
        const string yaml = """
            groups:
              - id: data
                version: -1
            """;

        Assert.Throws<BuildException>(() => LoadYaml(yaml));
    }

    [Fact]
    public void Load_GroupsIsMissing_ThrowsBuildException()
    {
        Assert.Throws<BuildException>(() => LoadYaml("{}"));
    }

    private static BuildConfiguration LoadYaml(string yaml)
    {
        string path = Path.Combine(Path.GetTempPath(), $"gamepatchkit-{Guid.NewGuid():N}.yml");

        try
        {
            File.WriteAllText(path, yaml);
            return BuildConfigurationLoader.Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}