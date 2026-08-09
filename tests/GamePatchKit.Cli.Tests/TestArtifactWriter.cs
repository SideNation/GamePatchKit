using System.IO.Compression;
using System.Security.Cryptography;
using GamePatchKit.Cli;
using NativeCompressions;

namespace GamePatchKit.Cli.Tests;

public sealed class TestArtifactWriter
{
    [Fact]
    public void WriteArchive_None_WritesSortedPayloadAndActualLayout()
    {
        using var environment = new ArtifactTestEnvironment();
        byte[] firstBytes = "first"u8.ToArray();
        byte[] secondBytes = "second-value"u8.ToArray();
        string firstPath = environment.WriteSource("first.bin", firstBytes);
        string secondPath = environment.WriteSource("second.bin", secondBytes);
        var entries = new[]
        {
            new SourceEntry("second.bin", secondPath, 1),
            new SourceEntry("first.bin", firstPath, 100)
        };

        WrittenArchive result = new ArtifactWriter().WriteArchive(
            environment.OutputPath,
            CreateGroup(PackingKind.Group, CompressionKind.None),
            entries);

        byte[] expectedPayload = [.. firstBytes, .. secondBytes];
        string artifactPath = environment.GetOutputPath(result.Name);
        Assert.Equal("archives/content/3.gpka", result.Name);
        Assert.Equal(expectedPayload.Length, result.PayloadSize);
        Assert.True(result.IsCreated);
        Assert.Equal(expectedPayload, File.ReadAllBytes(artifactPath));
        Assert.Collection(
            result.Layout,
            layout => Assert.Equal(new ArchiveEntryLayout("first.bin", 0, firstBytes.Length), layout),
            layout => Assert.Equal(
                new ArchiveEntryLayout("second.bin", firstBytes.Length, secondBytes.Length),
                layout));
        AssertStoredMatches(artifactPath, result.StoredSize, result.Checksum);
    }

    [Fact]
    public void WriteArchive_Zstd_WritesDecodablePayloadAndReusesSameCandidate()
    {
        using var environment = new ArtifactTestEnvironment();
        byte[] firstBytes = Enumerable.Repeat((byte)'a', 70_000).ToArray();
        byte[] secondBytes = Enumerable.Repeat((byte)'b', 70_000).ToArray();
        string firstPath = environment.WriteSource("a.bin", firstBytes);
        string secondPath = environment.WriteSource("b.bin", secondBytes);
        var entries = new[]
        {
            new SourceEntry("a.bin", firstPath, firstBytes.Length),
            new SourceEntry("b.bin", secondPath, secondBytes.Length)
        };
        var writer = new ArtifactWriter();
        GroupConfiguration group = CreateGroup(PackingKind.Group, CompressionKind.Zstd);

        WrittenArchive first = writer.WriteArchive(environment.OutputPath, group, entries);
        string artifactPath = environment.GetOutputPath(first.Name);
        byte[] storedBytes = File.ReadAllBytes(artifactPath);
        DateTime unchangedTimestamp = new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(artifactPath, unchangedTimestamp);
        DateTime recordedTimestamp = File.GetLastWriteTimeUtc(artifactPath);

        WrittenArchive second = writer.WriteArchive(environment.OutputPath, group, entries);

        Assert.True(first.IsCreated);
        Assert.False(second.IsCreated);
        Assert.Equal(first.Name, second.Name);
        Assert.Equal(first.PayloadSize, second.PayloadSize);
        Assert.Equal(first.StoredSize, second.StoredSize);
        Assert.Equal(first.Checksum, second.Checksum);
        Assert.Equal(first.Layout.ToArray(), second.Layout.ToArray());
        Assert.Equal(storedBytes, File.ReadAllBytes(artifactPath));
        Assert.Equal(recordedTimestamp, File.GetLastWriteTimeUtc(artifactPath));
        Assert.Equal([.. firstBytes, .. secondBytes], Decompress(artifactPath));
        AssertStoredMatches(artifactPath, first.StoredSize, first.Checksum);
        Assert.DoesNotContain(
            Directory.EnumerateFiles(Path.GetDirectoryName(artifactPath)!),
            path => Path.GetFileName(path).EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void WriteArchive_DifferentCandidate_ThrowsAndPreservesExistingArtifact()
    {
        using var environment = new ArtifactTestEnvironment();
        string sourcePath = environment.WriteSource("file.bin", "original"u8.ToArray());
        var entry = new SourceEntry("file.bin", sourcePath, 8);
        var writer = new ArtifactWriter();
        GroupConfiguration group = CreateGroup(PackingKind.Group, CompressionKind.None);
        WrittenArchive first = writer.WriteArchive(environment.OutputPath, group, [entry]);
        string artifactPath = environment.GetOutputPath(first.Name);
        byte[] originalArtifact = File.ReadAllBytes(artifactPath);
        File.WriteAllBytes(sourcePath, "changed"u8.ToArray());

        BuildException exception = Assert.Throws<BuildException>(() =>
        {
            writer.WriteArchive(environment.OutputPath, group, [entry]);
        });

        Assert.Contains("그룹 버전 충돌", exception.Message, StringComparison.Ordinal);
        Assert.Equal(originalArtifact, File.ReadAllBytes(artifactPath));
        Assert.Equal(
            new[] { Path.GetFileName(artifactPath) },
            Directory.EnumerateFiles(Path.GetDirectoryName(artifactPath)!).Select(Path.GetFileName));
    }

    [Fact]
    public void WriteFile_DifferentCandidates_UsesNumericNextRevisionAndPreservesExistingArtifacts()
    {
        using var environment = new ArtifactTestEnvironment();
        string sourcePath = environment.WriteSource("file.bin", "revision-nine"u8.ToArray());
        var entry = new SourceEntry("nested/file.bin", sourcePath, 13);
        var writer = new ArtifactWriter();
        GroupConfiguration group = CreateGroup(PackingKind.File, CompressionKind.None);
        string lowerRevisionPath = environment.GetOutputPath("files/content/3/nested/file.bin.v3.8");
        Directory.CreateDirectory(Path.GetDirectoryName(lowerRevisionPath)!);
        File.WriteAllBytes(lowerRevisionPath, "revision-nine"u8.ToArray());

        WrittenArtifact revisionNine = writer.WriteFile(environment.OutputPath, group, entry, "3.9");
        string revisionNinePath = environment.GetOutputPath(revisionNine.Name);
        byte[] revisionNineBytes = File.ReadAllBytes(revisionNinePath);
        string familyPath = Path.GetDirectoryName(revisionNinePath)!;
        File.WriteAllBytes(Path.Combine(familyPath, "file.bin.v3.010"), "ignored"u8.ToArray());
        File.WriteAllBytes(Path.Combine(familyPath, "file.bin.v3.invalid"), "ignored"u8.ToArray());
        File.WriteAllBytes(Path.Combine(familyPath, ".file.bin.v3.999.tmp"), "ignored"u8.ToArray());
        File.WriteAllBytes(sourcePath, "revision-ten"u8.ToArray());

        WrittenArtifact revisionTen = writer.WriteFile(environment.OutputPath, group, entry, "3.9");
        byte[] revisionTenBytes = File.ReadAllBytes(environment.GetOutputPath(revisionTen.Name));
        WrittenArtifact reusedTen = writer.WriteFile(environment.OutputPath, group, entry, "3.9");
        File.WriteAllBytes(sourcePath, "revision-eleven"u8.ToArray());
        WrittenArtifact revisionEleven = writer.WriteFile(environment.OutputPath, group, entry, "3.9");

        Assert.Equal("3.9", revisionNine.Version);
        Assert.Equal("files/content/3/nested/file.bin.v3.9", revisionNine.Name);
        Assert.True(revisionNine.IsCreated);
        Assert.Equal("revision-nine"u8.ToArray(), File.ReadAllBytes(lowerRevisionPath));
        Assert.Equal("3.10", revisionTen.Version);
        Assert.True(revisionTen.IsCreated);
        Assert.Equal(revisionTen with { IsCreated = false }, reusedTen);
        Assert.Equal("3.11", revisionEleven.Version);
        Assert.True(revisionEleven.IsCreated);
        Assert.Equal(revisionNineBytes, File.ReadAllBytes(revisionNinePath));
        Assert.Equal(revisionTenBytes, File.ReadAllBytes(environment.GetOutputPath(revisionTen.Name)));
        Assert.Equal("revision-eleven"u8.ToArray(), File.ReadAllBytes(environment.GetOutputPath(revisionEleven.Name)));
        AssertStoredMatches(revisionTen.Name, environment, revisionTen.StoredSize, revisionTen.Checksum);
    }

    [Fact]
    public void WriteFile_Zstd_WritesDecodableOriginalBytes()
    {
        using var environment = new ArtifactTestEnvironment();
        byte[] sourceBytes = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        string sourcePath = environment.WriteSource("file.bin", sourceBytes);
        var entry = new SourceEntry("file.bin", sourcePath, sourceBytes.Length);

        WrittenArtifact result = new ArtifactWriter().WriteFile(
            environment.OutputPath,
            CreateGroup(PackingKind.File, CompressionKind.Zstd),
            entry,
            "3.0");

        string artifactPath = environment.GetOutputPath(result.Name);
        Assert.Equal(sourceBytes, Decompress(artifactPath));
        AssertStoredMatches(artifactPath, result.StoredSize, result.Checksum);
    }

    private static void AssertStoredMatches(string path, long storedSize, string checksum)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string independentChecksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        StoredArtifact read = ArtifactWriter.ReadStored(path);

        Assert.Equal(bytes.LongLength, storedSize);
        Assert.Equal(independentChecksum, checksum);
        Assert.Equal(new StoredArtifact(storedSize, checksum), read);
    }

    private static void AssertStoredMatches(
        string name,
        ArtifactTestEnvironment environment,
        long storedSize,
        string checksum)
    {
        AssertStoredMatches(environment.GetOutputPath(name), storedSize, checksum);
    }

    private static GroupConfiguration CreateGroup(PackingKind packing, CompressionKind compression)
    {
        return new GroupConfiguration
        {
            Id = "content",
            Version = 3,
            Packing = packing,
            Compression = compression
        };
    }

    private static byte[] Decompress(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var decompression = new ZstandardStream(input, CompressionMode.Decompress, leaveOpen: false);
        using var output = new MemoryStream();
        decompression.CopyTo(output);
        return output.ToArray();
    }

    private sealed class ArtifactTestEnvironment : IDisposable
    {
        private readonly string _rootPath = Path.Combine(Path.GetTempPath(), $"GamePatchKit-{Guid.NewGuid():N}");

        public string OutputPath => Path.Combine(_rootPath, "output");

        public ArtifactTestEnvironment()
        {
            Directory.CreateDirectory(_rootPath);
        }

        public void Dispose()
        {
            Directory.Delete(_rootPath, recursive: true);
        }

        public string GetOutputPath(string relativePath)
        {
            return Path.Combine(OutputPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        public string WriteSource(string name, byte[] bytes)
        {
            string path = Path.Combine(_rootPath, "source", name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return path;
        }
    }
}