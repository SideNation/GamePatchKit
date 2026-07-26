using System.Security.Cryptography;
using GamePatchKit.Core;
using GamePatchKit.Core.Configuration;
using GamePatchKit.Core.Globbing;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Packager.Tests;

public class TestFilePackageBuilder
{
    [Fact]
    public async Task BuildAsync_SameInputTwice_ProducesIdenticalManifestAndArtifacts()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/config.json", "configuration");
        PackageConfig config = fixture.Config(compression: CompressionKind.None);
        var builder = new FilePackageBuilder();

        FilePackageResult first = await builder.BuildAsync(new FilePackageRequest(config, fixture.OutputRoot));
        FilePackageResult second = await builder.BuildAsync(new FilePackageRequest(config, fixture.OutputRoot));

        Assert.Equal(first.Release.ManifestHash, second.Release.ManifestHash);
        Assert.Equal(first.Release.GetCanonicalBytes(), second.Release.GetCanonicalBytes());
        ManifestArtifact.FileArtifact artifact = Assert.IsType<ManifestArtifact.FileArtifact>(Assert.Single(second.Release.Manifest.Artifacts));
        var payload = Assert.IsType<FilePayload.Single>(artifact.Payload);
        Assert.Equal(File.ReadAllBytes(fixture.SourcePath("data/config.json")), File.ReadAllBytes(fixture.OutputPath(payload.Path)));
        Assert.Equal(0, second.Report.CreatedFileArtifactCount);
        Assert.Equal(1, second.Report.ReusedFileArtifactCount);
    }

    [Fact]
    public async Task BuildAsync_NoDeclaredGroups_UsesRequiredDefaultFileGroup()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/config.json", "configuration");
        PackageConfig config = fixture.Config(
            compression: CompressionKind.None,
            groups: Array.Empty<PackageConfigGroup>());

        FilePackageResult result = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(config, fixture.OutputRoot));

        ManifestGroupEntry group = Assert.Single(result.Release.Manifest.Groups);
        Assert.Equal("default", group.Name);
        Assert.True(group.Required);
        Assert.Equal("default", Assert.Single(result.Release.Manifest.Files).Group);
        Assert.IsType<ManifestArtifact.FileArtifact>(Assert.Single(result.Release.Manifest.Artifacts));
    }

    [Fact]
    public async Task BuildAsync_TwoFilesWithSameBytes_SharesOneCreatedArtifact()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/first.json", "shared");
        fixture.WriteSource("data/second.json", "shared");

        FilePackageResult result = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(
                fixture.Config(compression: CompressionKind.None),
                fixture.OutputRoot));

        Assert.Single(result.Release.Manifest.Artifacts);
        Assert.Equal(2, result.Release.Manifest.Files.Count);
        Assert.Equal(1, result.Report.CreatedFileArtifactCount);
        Assert.Equal(0, result.Report.ReusedFileArtifactCount);
    }

    [Fact]
    public async Task BuildAsync_PayloadExceedsLimit_CreatesOrderedBoundedParts()
    {
        using var fixture = new PackageFixture();
        byte[] content = Enumerable.Range(0, 25).Select(value => (byte)value).ToArray();
        fixture.WriteSource("data/big.bin", content);
        PackageConfig config = fixture.Config(compression: CompressionKind.None, maxArtifactBytes: 10);

        FilePackageResult result = await new FilePackageBuilder().BuildAsync(new FilePackageRequest(config, fixture.OutputRoot));

        ManifestArtifact.FileArtifact artifact = Assert.IsType<ManifestArtifact.FileArtifact>(Assert.Single(result.Release.Manifest.Artifacts));
        var payload = Assert.IsType<FilePayload.Parts>(artifact.Payload);
        Assert.Equal(new long[] { 0, 1, 2 }, payload.PartList.Select(part => part.Index));
        Assert.Equal(new long[] { 10, 10, 5 }, payload.PartList.Select(part => part.Size));
        Assert.All(payload.PartList, part => Assert.True(part.Size <= config.MaxArtifactBytes));

        using var joined = new MemoryStream();
        foreach (FilePart part in payload.PartList)
        {
            await using FileStream stream = File.OpenRead(fixture.OutputPath(part.Path));
            await stream.CopyToAsync(joined);
        }

        Assert.Equal(content, joined.ToArray());
    }

    [Fact]
    public async Task BuildAsync_ZstdAndCompressedManifest_VerifiesRoundTrip()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/config.json", new string('x', 64 * 1024));
        PackageConfig config = fixture.Config(compression: CompressionKind.Zstd, maxArtifactBytes: 128);
        var builder = new FilePackageBuilder();

        FilePackageResult result = await builder.BuildAsync(
            new FilePackageRequest(config, fixture.OutputRoot, writeCompressedManifest: true));

        ManifestArtifact.FileArtifact artifact = Assert.IsType<ManifestArtifact.FileArtifact>(Assert.Single(result.Release.Manifest.Artifacts));
        Assert.Equal(CompressionKind.Zstd, artifact.Compression);
        Assert.All(artifact.GetPayloadObjects(), item => Assert.True(item.Size <= config.MaxArtifactBytes));
        await PackagePayloadVerifier.VerifyAsync(fixture.OutputRoot, result.Release.Manifest);
        Assert.True(File.Exists(Path.Combine(
            fixture.OutputRoot,
            config.PackageId,
            "manifests",
            result.Release.ManifestHash,
            "manifest.json.zst")));
    }

    [Fact]
    public async Task BuildAsync_CompressionPolicyOnlyChanges_ReusesManifestAndExistingCompression()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/config.json", "configuration");
        FilePackageResult first = await new FilePackageBuilder(zstdCodec: null).BuildAsync(
            new FilePackageRequest(fixture.Config(compression: CompressionKind.None), fixture.OutputRoot));

        FilePackageResult second = await new FilePackageBuilder(zstdCodec: null).BuildAsync(
            new FilePackageRequest(
                fixture.Config(compression: CompressionKind.Zstd),
                fixture.OutputRoot,
                fixture.Previous(first)));

        Assert.True(second.ReusedManifest);
        Assert.Equal(first.Release.ManifestHash, second.Release.ManifestHash);
        ManifestArtifact.FileArtifact artifact = Assert.IsType<ManifestArtifact.FileArtifact>(Assert.Single(second.Release.Manifest.Artifacts));
        Assert.Equal(CompressionKind.None, artifact.Compression);
        Assert.Equal(0, second.Report.CreatedFileArtifactCount);
        Assert.Equal(1, second.Report.ReusedFileArtifactCount);
    }

    [Fact]
    public async Task BuildAsync_DeletedFile_RemovesFinalEntryWithoutCreatingArtifact()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/keep.json", "keep");
        fixture.WriteSource("data/delete.json", "delete");
        PackageConfig config = fixture.Config(compression: CompressionKind.None);
        var builder = new FilePackageBuilder();
        FilePackageResult first = await builder.BuildAsync(new FilePackageRequest(config, fixture.OutputRoot));
        File.Delete(fixture.SourcePath("data/delete.json"));

        FilePackageResult second = await builder.BuildAsync(
            new FilePackageRequest(config, fixture.OutputRoot, fixture.Previous(first)));

        ManifestFileEntry remaining = Assert.Single(second.Release.Manifest.Files);
        Assert.Equal("data/keep.json", remaining.Path);
        Assert.Equal(new[] { "data/delete.json" }, second.Report.DeletedFiles);
        Assert.NotEqual(first.Release.DataVersion, second.Release.DataVersion);
        Assert.Equal(0, second.Report.CreatedFileArtifactCount);
    }

    [Fact]
    public async Task BuildAsync_GroupMove_ReusesFileArtifactAndChangesLogicalVersion()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/config.json", "configuration");
        var builder = new FilePackageBuilder();
        PackageConfig firstConfig = fixture.Config(
            compression: CompressionKind.None,
            groups: new[] { fixture.Group("alpha", "data/**/*", ArtifactMode.File, required: true) });
        FilePackageResult first = await builder.BuildAsync(new FilePackageRequest(firstConfig, fixture.OutputRoot));
        PackageConfig secondConfig = fixture.Config(
            compression: CompressionKind.Zstd,
            groups: new[] { fixture.Group("beta", "data/**/*", ArtifactMode.Bundle, required: false) });

        FilePackageResult second = await builder.BuildAsync(
            new FilePackageRequest(secondConfig, fixture.OutputRoot, fixture.Previous(first)));

        Assert.Equal("beta", Assert.Single(second.Release.Manifest.Files).Group);
        Assert.Equal(new[] { "data/config.json" }, second.Report.MovedGroupFiles);
        Assert.NotEqual(first.Release.DataVersion, second.Release.DataVersion);
        Assert.Equal(first.Release.Manifest.Artifacts[0].ToJson(), second.Release.Manifest.Artifacts[0].ToJson());
        Assert.Equal(1, second.Report.ReusedFileArtifactCount);
    }

    [Fact]
    public async Task BuildAsync_UnchangedBundleGroup_ReusesWholeBundle()
    {
        using var fixture = new PackageFixture();
        byte[] sourceBytes = "map-data"u8.ToArray();
        fixture.WriteSource("maps/level.bin", sourceBytes);
        FinalizedManifest previous = fixture.CreateBundleRelease("maps/level.bin", sourceBytes, required: false);
        PackageConfig config = fixture.Config(
            compression: CompressionKind.Zstd,
            groups: new[] { fixture.Group("maps", "maps/**/*", ArtifactMode.Bundle, required: false) });

        FilePackageResult result = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(config, fixture.OutputRoot, fixture.Previous(previous)));

        Assert.True(result.ReusedManifest);
        Assert.IsType<ManifestArtifact.BundleArtifact>(Assert.Single(result.Release.Manifest.Artifacts));
        Assert.Equal(1, result.Report.ReusedBundleArtifactCount);
        Assert.Equal(0, result.Report.CreatedFileArtifactCount);
    }

    [Fact]
    public async Task BuildAsync_BundleToFileMode_CreatesFileOverride()
    {
        using var fixture = new PackageFixture();
        byte[] sourceBytes = "map-data"u8.ToArray();
        fixture.WriteSource("maps/level.bin", sourceBytes);
        FinalizedManifest previous = fixture.CreateBundleRelease("maps/level.bin", sourceBytes, required: false);
        PackageConfig config = fixture.Config(
            compression: CompressionKind.None,
            groups: new[] { fixture.Group("maps", "maps/**/*", ArtifactMode.File, required: false) });

        FilePackageResult result = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(config, fixture.OutputRoot, fixture.Previous(previous)));

        ManifestArtifact.FileArtifact artifact = Assert.IsType<ManifestArtifact.FileArtifact>(Assert.Single(result.Release.Manifest.Artifacts));
        Assert.IsType<FileSource.FileReference>(Assert.Single(result.Release.Manifest.Files).Source);
        Assert.Equal(Sha256(sourceBytes), artifact.PrimaryArtifactHash);
        Assert.Equal(1, result.Report.CreatedFileArtifactCount);
        Assert.Equal(previous.CompactVersion, result.Release.CompactVersion);
    }

    [Fact]
    public async Task BuildAsync_NewFileInBundleMode_CreatesFileOverrideUntilCompact()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("maps/level.bin", "map-data");
        PackageConfig config = fixture.Config(
            compression: CompressionKind.None,
            groups: new[] { fixture.Group("maps", "maps/**/*", ArtifactMode.Bundle, required: false) });

        FilePackageResult result = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(config, fixture.OutputRoot));

        Assert.IsType<ManifestArtifact.FileArtifact>(Assert.Single(result.Release.Manifest.Artifacts));
        Assert.IsType<FileSource.FileReference>(Assert.Single(result.Release.Manifest.Files).Source);
    }

    [Fact]
    public async Task BuildAsync_ChangedEntryInPreviousBundle_FallsBackEntireBundleToFileOverrides()
    {
        using var fixture = new PackageFixture();
        byte[] firstBytes = "first-map"u8.ToArray();
        byte[] secondBytes = "second-map"u8.ToArray();
        fixture.WriteSource("maps/first.bin", firstBytes);
        fixture.WriteSource("maps/second.bin", secondBytes);
        FinalizedManifest previous = fixture.CreateBundleRelease(
            required: false,
            ("maps/first.bin", firstBytes),
            ("maps/second.bin", secondBytes));
        fixture.WriteSource("maps/second.bin", "changed-map");
        PackageConfig config = fixture.Config(
            compression: CompressionKind.None,
            groups: new[] { fixture.Group("maps", "maps/**/*", ArtifactMode.Bundle, required: false) });

        FilePackageResult result = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(config, fixture.OutputRoot, fixture.Previous(previous)));

        Assert.Equal(2, result.Release.Manifest.Artifacts.Count);
        Assert.All(result.Release.Manifest.Artifacts, artifact => Assert.IsType<ManifestArtifact.FileArtifact>(artifact));
        Assert.All(result.Release.Manifest.Files, file => Assert.IsType<FileSource.FileReference>(file.Source));
        Assert.Equal(2, result.Report.CreatedFileArtifactCount);
        Assert.Equal(0, result.Report.ReusedBundleArtifactCount);
    }

    [Fact]
    public async Task BuildAsync_CorruptedPreviousArtifact_RejectsReuse()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/config.json", "configuration");
        PackageConfig config = fixture.Config(compression: CompressionKind.None);
        var builder = new FilePackageBuilder();
        FilePackageResult first = await builder.BuildAsync(new FilePackageRequest(config, fixture.OutputRoot));
        ManifestArtifact.FileArtifact artifact = Assert.IsType<ManifestArtifact.FileArtifact>(Assert.Single(first.Release.Manifest.Artifacts));
        var payload = Assert.IsType<FilePayload.Single>(artifact.Payload);
        File.WriteAllBytes(fixture.OutputPath(payload.Path), new byte[payload.Size]);

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => builder.BuildAsync(new FilePackageRequest(config, fixture.OutputRoot, fixture.Previous(first))));

        Assert.Contains(exception.Errors, error => error.Code == PackageErrorCodes.ArtifactCorrupted);
    }

    [Fact]
    public async Task BuildAsync_CorruptedPreviousPart_RejectsReuse()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/big.bin", Enumerable.Range(0, 25).Select(value => (byte)value).ToArray());
        PackageConfig config = fixture.Config(compression: CompressionKind.None, maxArtifactBytes: 10);
        var builder = new FilePackageBuilder();
        FilePackageResult first = await builder.BuildAsync(new FilePackageRequest(config, fixture.OutputRoot));
        ManifestArtifact.FileArtifact artifact = Assert.IsType<ManifestArtifact.FileArtifact>(Assert.Single(first.Release.Manifest.Artifacts));
        var payload = Assert.IsType<FilePayload.Parts>(artifact.Payload);
        FilePart firstPart = payload.PartList[0];
        File.WriteAllBytes(fixture.OutputPath(firstPart.Path), new byte[firstPart.Size]);

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => builder.BuildAsync(new FilePackageRequest(config, fixture.OutputRoot, fixture.Previous(first))));

        Assert.Contains(exception.Errors, error => error.Code == PackageErrorCodes.ArtifactCorrupted);
    }

    [Fact]
    public async Task VerifyAsync_PartsDoNotMatchParentArtifactHash_RejectsManifest()
    {
        using var fixture = new PackageFixture();
        byte[] content = Enumerable.Range(0, 25).Select(value => (byte)value).ToArray();
        fixture.WriteSource("data/big.bin", content);
        PackageConfig config = fixture.Config(compression: CompressionKind.None, maxArtifactBytes: 10);
        FilePackageResult valid = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(config, fixture.OutputRoot));
        ManifestArtifact.FileArtifact validArtifact = Assert.IsType<ManifestArtifact.FileArtifact>(
            Assert.Single(valid.Release.Manifest.Artifacts));
        var validPayload = Assert.IsType<FilePayload.Parts>(validArtifact.Payload);
        string wrongArtifactHash = new string('f', 64);
        var wrongParts = new List<FilePart>();

        foreach (FilePart part in validPayload.PartList)
        {
            string wrongPath = ContentAddressedPath.FilePartPath(fixture.PackageId, wrongArtifactHash, part.Index);
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.OutputPath(wrongPath))!);
            File.Copy(fixture.OutputPath(part.Path), fixture.OutputPath(wrongPath));
            wrongParts.Add(new FilePart(part.Index, wrongPath, part.Size, part.PartHash));
        }

        var wrongArtifact = new ManifestArtifact.FileArtifact(
            CompressionKind.None,
            new FilePayload.Parts(validPayload.Size, wrongArtifactHash, wrongParts));
        ManifestFileEntry validFile = Assert.Single(valid.Release.Manifest.Files);
        var wrongFile = new ManifestFileEntry(
            validFile.Path,
            validFile.Group,
            validFile.Size,
            validFile.FileHash,
            new FileSource.FileReference(wrongArtifactHash));
        var wrongManifest = new ReleaseManifest(
            1,
            fixture.PackageId,
            valid.Release.DataVersion,
            0,
            valid.Release.Manifest.Groups,
            new ManifestArtifact[] { wrongArtifact },
            new[] { wrongFile });

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => PackagePayloadVerifier.VerifyAsync(fixture.OutputRoot, wrongManifest));

        Assert.Contains(exception.Errors, error => error.Code == PackageErrorCodes.ArtifactCorrupted);
    }

    [Fact]
    public async Task BuildAsync_PreviousManifestHashMismatch_RejectsBeforeReuse()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/config.json", "configuration");
        PackageConfig config = fixture.Config(compression: CompressionKind.None);
        var builder = new FilePackageBuilder();
        FilePackageResult first = await builder.BuildAsync(new FilePackageRequest(config, fixture.OutputRoot));
        var invalidPrevious = new PreviousRelease(first.Release.GetCanonicalBytes(), new string('0', 64));

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => builder.BuildAsync(new FilePackageRequest(config, fixture.OutputRoot, invalidPrevious)));

        Assert.Contains(exception.Errors, error => error.Code == ManifestErrorCodes.ManifestHashMismatch);
    }

    [Fact]
    public async Task BuildAsync_ExistingHashPathHasDifferentBytes_RejectsCollision()
    {
        using var fixture = new PackageFixture();
        byte[] sourceBytes = "configuration"u8.ToArray();
        fixture.WriteSource("data/config.json", sourceBytes);
        string artifactHash = Sha256(sourceBytes);
        string collisionPath = fixture.OutputPath(
            ContentAddressedPath.FileSinglePayloadPath(fixture.PackageId, artifactHash, CompressionKind.None));
        Directory.CreateDirectory(Path.GetDirectoryName(collisionPath)!);
        File.WriteAllBytes(collisionPath, new byte[sourceBytes.Length]);

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => new FilePackageBuilder().BuildAsync(
                new FilePackageRequest(fixture.Config(compression: CompressionKind.None), fixture.OutputRoot)));

        Assert.Contains(exception.Errors, error => error.Code == PackageErrorCodes.ArtifactCorrupted);
        Assert.Equal(new byte[sourceBytes.Length], File.ReadAllBytes(collisionPath));
    }

    [Fact]
    public async Task BuildAsync_ExcludedSymlinkStillFails()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/config.json", "configuration");
        string targetPath = fixture.SourcePath("outside.txt");
        File.WriteAllText(targetPath, "target");
        string linkPath = fixture.SourcePath("ignored-link");
        File.CreateSymbolicLink(linkPath, targetPath);

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => new FilePackageBuilder().BuildAsync(
                new FilePackageRequest(
                    fixture.Config(compression: CompressionKind.None, includePattern: "**/*.json"),
                    fixture.OutputRoot)));

        Assert.Contains(exception.Errors, error => error.Code == PackageErrorCodes.UnsupportedEntry);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("delete")]
    [InlineData("replace")]
    [InlineData("change")]
    public async Task BuildAsync_SourceChangesBeforeManifestCommit_FailsWithoutPublishing(string mutation)
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/config.json", "configuration");
        PackageConfig config = fixture.Config(compression: CompressionKind.None);
        var builder = new FilePackageBuilder(
            zstdCodec: null,
            beforeFinalSourceVerification: _ =>
            {
                switch (mutation)
                {
                    case "add":
                        fixture.WriteSource("data/added.json", "added");
                        break;
                    case "delete":
                        File.Delete(fixture.SourcePath("data/config.json"));
                        break;
                    case "replace":
                        File.Delete(fixture.SourcePath("data/config.json"));
                        fixture.WriteSource("data/config.json", "configuration");
                        break;
                    case "change":
                        fixture.WriteSource("data/config.json", "configuratioN");
                        break;
                }

                return Task.CompletedTask;
            });

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => builder.BuildAsync(new FilePackageRequest(config, fixture.OutputRoot)));

        Assert.Contains(exception.Errors, error => error.Code == PackageErrorCodes.SourceChanged);
        Assert.Empty(Directory.GetFileSystemEntries(fixture.OutputRoot));
    }

    [Fact]
    public async Task BuildAsync_OutputInsideSource_RejectsBeforeWriting()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/config.json", "configuration");
        string nestedOutput = fixture.SourcePath("output");

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => new FilePackageBuilder().BuildAsync(
                new FilePackageRequest(fixture.Config(compression: CompressionKind.None), nestedOutput)));

        Assert.Contains(exception.Errors, error => error.Code == PackageErrorCodes.InvalidConfiguration);
        Assert.False(Directory.Exists(nestedOutput));
    }

    [Fact]
    public async Task BuildAsync_OutputRootIsSymlink_RejectsWithoutWritingThroughLink()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/config.json", "configuration");
        string linkedDirectory = Path.Combine(Path.GetDirectoryName(fixture.OutputRoot)!, "linked-output");
        Directory.CreateDirectory(linkedDirectory);
        Directory.Delete(fixture.OutputRoot);
        Directory.CreateSymbolicLink(fixture.OutputRoot, linkedDirectory);

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => new FilePackageBuilder().BuildAsync(
                new FilePackageRequest(
                    fixture.Config(compression: CompressionKind.None),
                    fixture.OutputRoot)));

        Assert.Contains(exception.Errors, error => error.Code == PackageErrorCodes.ImmutablePathConflict);
        Assert.Empty(Directory.GetFileSystemEntries(linkedDirectory));
    }

    private static string Sha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed class PackageFixture : IDisposable
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
            string includePattern = "**/*")
        {
            return new PackageConfig(
                schemaVersion: 1,
                PackageId,
                SourceRoot,
                new[] { Pattern(includePattern) },
                Array.Empty<GlobPattern>(),
                maxArtifactBytes,
                ArtifactMode.File,
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

        public FinalizedManifest CreateBundleRelease(string relativePath, byte[] sourceBytes, bool required)
        {
            return CreateBundleRelease(required, (relativePath, sourceBytes));
        }

        public FinalizedManifest CreateBundleRelease(bool required, params (string RelativePath, byte[] SourceBytes)[] files)
        {
            byte[] bundleBytes = "bundle-payload"u8.ToArray();
            string bundleHash = Sha256(bundleBytes);
            string bundlePath = ContentAddressedPath.BundleArtifactPath(PackageId, "maps", bundleHash, CompressionKind.None);
            string nativeBundlePath = OutputPath(bundlePath);
            Directory.CreateDirectory(Path.GetDirectoryName(nativeBundlePath)!);
            File.WriteAllBytes(nativeBundlePath, bundleBytes);

            var artifact = new ManifestArtifact.BundleArtifact(
                "maps",
                bundlePath,
                bundleBytes.Length,
                bundleHash,
                CompressionKind.None,
                files.Select(file => new BundleEntry(file.RelativePath)).ToList());
            List<ManifestFileEntry> manifestFiles = files
                .Select(file => new ManifestFileEntry(
                    file.RelativePath,
                    "maps",
                    file.SourceBytes.Length,
                    Sha256(file.SourceBytes),
                    new FileSource.BundleEntryReference(bundleHash, file.RelativePath)))
                .ToList();
            var draft = new ReleaseManifest(
                1,
                PackageId,
                DataVersionFormat.Prefix + new string('0', 64),
                0,
                new[] { new ManifestGroupEntry("maps", required) },
                new ManifestArtifact[] { artifact },
                manifestFiles);
            return ReleaseIdentity.Finalize(draft, 0);
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
}
