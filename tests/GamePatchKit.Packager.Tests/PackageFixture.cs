using GamePatchKit.Core;
using GamePatchKit.Core.Configuration;
using GamePatchKit.Core.Globbing;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Packager.Tests;

internal sealed class PackageFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"gamepatchkit-tests-{Guid.NewGuid():N}");

    public string PackageId { get; } = "test-package";

    public string SourceRoot => Path.Combine(_root, "source");

    public string OutputRoot => Path.Combine(_root, "output");

    public PackageFixture()
    {
        Directory.CreateDirectory(SourceRoot);
        Directory.CreateDirectory(OutputRoot);
    }

    public PackageConfig Config(
        CompressionKind compression,
        long maxArtifactBytes = PackageConfig.DefaultMaxArtifactBytes,
        IReadOnlyList<PackageConfigGroup>? groups = null,
        string includePattern = "**/*",
        ArtifactMode defaultArtifactMode = ArtifactMode.File)
    {
        return new PackageConfig(
            schemaVersion: 1,
            PackageId,
            SourceRoot,
            new[] { Pattern(includePattern) },
            Array.Empty<GlobPattern>(),
            maxArtifactBytes,
            defaultArtifactMode,
            compression,
            groups ?? new[] { Group("core", "**/*", ArtifactMode.File, required: true) });
    }

    public PackageConfigGroup Group(
        string name,
        string includePattern,
        ArtifactMode artifactMode,
        bool required,
        CompressionKind? compression = null)
    {
        return new PackageConfigGroup(
            name,
            new[] { Pattern(includePattern) },
            artifactMode,
            required,
            compression);
    }

    public void WriteSource(string relativePath, string content)
    {
        WriteSource(relativePath, System.Text.Encoding.UTF8.GetBytes(content));
    }

    public void WriteSource(string relativePath, byte[] content)
    {
        string path = SourcePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    public string SourcePath(string relativePath)
    {
        return Path.Combine(SourceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    public string OutputPath(string canonicalPath)
    {
        return Path.Combine(OutputRoot, canonicalPath.Replace('/', Path.DirectorySeparatorChar));
    }

    public PreviousRelease Previous(FilePackageResult result)
    {
        return Previous(result.Release);
    }

    public PreviousRelease Previous(FinalizedManifest release)
    {
        return new PreviousRelease(release.GetCanonicalBytes(), release.ManifestHash);
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
        Assert.True(parsed, errorCode);
        return pattern!;
    }
}