namespace GamePatchKit.Cli;

internal sealed class BuildConfiguration
{
    public IReadOnlyList<GroupConfiguration> Groups { get; init; } = null!;
}

internal sealed class GroupConfiguration
{
    public string Id { get; init; } = null!;
    public int Version { get; init; }
    public PackingKind Packing { get; init; }
    public CompressionKind Compression { get; init; }
}