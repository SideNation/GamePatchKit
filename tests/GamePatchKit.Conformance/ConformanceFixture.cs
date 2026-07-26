using GamePatchKit.Compression.NativeCompressions;
using GamePatchKit.Core;
using GamePatchKit.Core.Configuration;
using GamePatchKit.Core.Globbing;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Packager;

namespace GamePatchKit.Conformance;

// Builds real releases through FilePackageBuilder and publishes them to a real directory, so conformance
// scenarios exercise an actual publish tree - file, bundle, multipart-file and zstd combinations included -
// instead of a hand-built approximation of one. Every adapter subclass under test reads from the same
// PublishRoot, one built once per fixture instance and republished as scenarios need new releases.
public sealed class ConformanceFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"gamepatchkit-conformance-{Guid.NewGuid():N}");

    public string PackageId { get; } = "conformance-package";

    public string SourceRoot => Path.Combine(_root, "source");

    public string PublishRoot => Path.Combine(_root, "publish");

    public ConformanceFixture()
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
        WriteSource(relativePath, System.Text.Encoding.UTF8.GetBytes(content));
    }

    public void DeleteSource(string relativePath)
    {
        string path = Path.Combine(SourceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Delete(path);
    }

    public PackageConfigGroup Group(string name, bool required, ArtifactMode mode = ArtifactMode.File)
    {
        return new PackageConfigGroup(name, new[] { Pattern($"{name}/**/*") }, mode, required, compression: null);
    }

    public Task<FinalizedManifest> PublishAsync(IReadOnlyList<PackageConfigGroup> groups)
    {
        return PublishAsync(groups, CompressionKind.None, PackageConfig.DefaultMaxArtifactBytes);
    }

    public async Task<FinalizedManifest> PublishAsync(
        IReadOnlyList<PackageConfigGroup> groups,
        CompressionKind compression,
        long maxArtifactBytes = PackageConfig.DefaultMaxArtifactBytes)
    {
        var config = new PackageConfig(
            schemaVersion: 1,
            PackageId,
            SourceRoot,
            new[] { Pattern("**/*") },
            Array.Empty<GlobPattern>(),
            maxArtifactBytes,
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
            throw new InvalidOperationException($"Invalid conformance fixture glob pattern '{source}': {errorCode}");
        }

        return pattern!;
    }
}
