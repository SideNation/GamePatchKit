using System.Text;
using GamePatchKit.Compression.NativeCompressions;
using GamePatchKit.Core;
using GamePatchKit.Core.Configuration;
using GamePatchKit.Core.Globbing;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Packager;

namespace GamePatchKit.IntegrationTests;

// Builds a real release through FilePackageBuilder and publishes it to a real directory, so the DotNet adapter
// scenarios in this project download and activate an actual publish tree instead of a hand-built approximation
// of one.
internal sealed class PublishedPackageFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"gamepatchkit-integration-{Guid.NewGuid():N}");

    public string PackageId { get; } = "integration-package";

    public string SourceRoot => Path.Combine(_root, "source");

    public string PublishRoot => Path.Combine(_root, "publish");

    public PublishedPackageFixture()
    {
        Directory.CreateDirectory(SourceRoot);
        Directory.CreateDirectory(PublishRoot);
    }

    public void WriteSource(string relativePath, byte[] content)
    {
        string path = Path.Combine(SourceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    public void WriteSource(string relativePath, string content)
    {
        WriteSource(relativePath, Encoding.UTF8.GetBytes(content));
    }

    public PackageConfigGroup Group(string name, bool required)
    {
        return new PackageConfigGroup(name, new[] { Pattern($"{name}/**/*") }, ArtifactMode.File, required, compression: null);
    }

    public Task<FinalizedManifest> PublishAsync(IReadOnlyList<PackageConfigGroup> groups)
    {
        return PublishAsync(groups, CompressionKind.None);
    }

    public async Task<FinalizedManifest> PublishAsync(IReadOnlyList<PackageConfigGroup> groups, CompressionKind compression)
    {
        var config = new PackageConfig(
            schemaVersion: 1,
            PackageId,
            SourceRoot,
            new[] { Pattern("**/*") },
            Array.Empty<GlobPattern>(),
            PackageConfig.DefaultMaxArtifactBytes,
            ArtifactMode.File,
            compression,
            groups);
        ICompressionCodec? zstdCodec = compression == CompressionKind.Zstd ? ZstdCompressionCodecFactory.Create() : null;

        FilePackageResult result = await new FilePackageBuilder(zstdCodec).BuildAsync(
            new FilePackageRequest(config, PublishRoot));
        return result.Release;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static GlobPattern Pattern(string source)
    {
        bool parsed = GlobPattern.TryParse(source, out GlobPattern? pattern, out string errorCode);
        if (!parsed)
        {
            throw new InvalidOperationException($"Invalid test glob pattern '{source}': {errorCode}");
        }

        return pattern!;
    }
}
