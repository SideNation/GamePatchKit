using System.Text;
using GamePatchKit.Cli;

namespace GamePatchKit.Cli.Tests;

public sealed class TestVerifyCommand
{
    private readonly VerifyCommand _sut = new();

    [Fact]
    public void Execute_IntactOutputOutsideGitWithoutConfiguration_IgnoresUnreferencedFileAndSucceedsWithoutChanges()
    {
        using var environment = new VerifyTestEnvironment();
        string unreferencedPath = Path.Combine(environment.OutputPath, "unreferenced.bin");
        File.WriteAllBytes(unreferencedPath, "unreferenced"u8.ToArray());
        byte[] manifestBefore = File.ReadAllBytes(environment.ManifestPath);
        byte[] archiveBefore = File.ReadAllBytes(environment.GetArtifactPath(VerifyTestEnvironment.ARCHIVE_NAME));
        byte[] fileBefore = File.ReadAllBytes(environment.GetArtifactPath(VerifyTestEnvironment.FILE_NAME));
        byte[] unreferencedBefore = File.ReadAllBytes(unreferencedPath);
        using var standardOutput = new StringWriter();
        using var error = new StringWriter();

        IReadOnlyList<ArtifactMismatch> mismatches = _sut.Execute(new VerifyArguments(environment.OutputPath));
        int exitCode = Program.Run(
            new[] { "verify", "--output", environment.OutputPath },
            standardOutput,
            error);

        Assert.Empty(mismatches);
        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Equal(string.Empty, error.ToString());
        Assert.False(Directory.Exists(Path.Combine(environment.RootPath, ".git")));
        Assert.False(File.Exists(Path.Combine(environment.RootPath, BuildConfigurationLoader.FILE_NAME)));
        Assert.Equal(manifestBefore, File.ReadAllBytes(environment.ManifestPath));
        Assert.Equal(archiveBefore, File.ReadAllBytes(environment.GetArtifactPath(VerifyTestEnvironment.ARCHIVE_NAME)));
        Assert.Equal(fileBefore, File.ReadAllBytes(environment.GetArtifactPath(VerifyTestEnvironment.FILE_NAME)));
        Assert.Equal(unreferencedBefore, File.ReadAllBytes(unreferencedPath));
    }

    [Theory]
    [InlineData(VerifyTestEnvironment.ARCHIVE_NAME, "missing", "없습니다")]
    [InlineData(VerifyTestEnvironment.ARCHIVE_NAME, "truncated", "크기가 다릅니다")]
    [InlineData(VerifyTestEnvironment.ARCHIVE_NAME, "corrupted", "checksum이 다릅니다")]
    [InlineData(VerifyTestEnvironment.FILE_NAME, "missing", "없습니다")]
    [InlineData(VerifyTestEnvironment.FILE_NAME, "truncated", "크기가 다릅니다")]
    [InlineData(VerifyTestEnvironment.FILE_NAME, "corrupted", "checksum이 다릅니다")]
    public void Execute_ArtifactIsDamaged_ReturnsPathAndReason(
        string artifactName,
        string damageKind,
        string expectedReason)
    {
        using var environment = new VerifyTestEnvironment();
        environment.Damage(artifactName, damageKind);
        byte[] manifestBefore = File.ReadAllBytes(environment.ManifestPath);
        byte[]? artifactBefore = File.Exists(environment.GetArtifactPath(artifactName))
            ? File.ReadAllBytes(environment.GetArtifactPath(artifactName))
            : null;

        ArtifactMismatch mismatch = Assert.Single(
            _sut.Execute(new VerifyArguments(environment.OutputPath)));

        Assert.Equal(artifactName, mismatch.Name);
        Assert.Contains(expectedReason, mismatch.Reason, StringComparison.Ordinal);
        Assert.Equal(manifestBefore, File.ReadAllBytes(environment.ManifestPath));

        if (artifactBefore is null)
        {
            Assert.False(File.Exists(environment.GetArtifactPath(artifactName)));
        }
        else
        {
            Assert.Equal(artifactBefore, File.ReadAllBytes(environment.GetArtifactPath(artifactName)));
        }
    }

    [Fact]
    public void Run_MultipleMismatches_WritesEveryMismatchAndReturnsNonZeroExitCode()
    {
        using var environment = new VerifyTestEnvironment();
        environment.Damage(VerifyTestEnvironment.ARCHIVE_NAME, "missing");
        environment.Damage(VerifyTestEnvironment.FILE_NAME, "corrupted");
        using var standardOutput = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Program.Run(
            new[] { "verify", "--output", environment.OutputPath },
            standardOutput,
            error);

        string[] lines = error.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEqual(0, exitCode);
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Equal(2, lines.Length);
        Assert.Contains(VerifyTestEnvironment.ARCHIVE_NAME, lines[0], StringComparison.Ordinal);
        Assert.Contains("없습니다", lines[0], StringComparison.Ordinal);
        Assert.Contains(VerifyTestEnvironment.FILE_NAME, lines[1], StringComparison.Ordinal);
        Assert.Contains("checksum이 다릅니다", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_InvalidManifest_ThrowsBeforeCheckingArtifacts()
    {
        using var environment = new VerifyTestEnvironment();
        string invalidManifest = File.ReadAllText(environment.ManifestPath)
            .Replace(environment.FileChecksum, "invalid", StringComparison.Ordinal);
        File.WriteAllText(
            environment.ManifestPath,
            invalidManifest,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Delete(environment.GetArtifactPath(VerifyTestEnvironment.ARCHIVE_NAME));

        BuildException exception = Assert.Throws<BuildException>(
            () => _sut.Execute(new VerifyArguments(environment.OutputPath)));

        Assert.Contains("이전 매니페스트가 올바르지 않습니다", exception.Message, StringComparison.Ordinal);
        Assert.Contains("checksum", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(VerifyTestEnvironment.ARCHIVE_NAME, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ManifestIsMissing_ThrowsBuildException()
    {
        string outputPath = Path.Combine(Path.GetTempPath(), $"GamePatchKit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputPath);

        try
        {
            BuildException exception = Assert.Throws<BuildException>(
                () => _sut.Execute(new VerifyArguments(outputPath)));

            Assert.Contains("manifest.json", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(outputPath, recursive: true);
        }
    }

    [Fact]
    public void Execute_BuildAcceptedSameSizeCorruption_ReturnsChecksumMismatch()
    {
        using var testRepository = new GitTestRepository();
        testRepository.WriteFile(
            "data/gamepatchkit.yml",
            """
            groups:
              - id: group
                version: 1
                packing: file
                compression: none
            """);
        testRepository.WriteFile("data/group/file.txt", "original");
        testRepository.CommitAll("initial");
        string sourcePath = testRepository.GetRepositoryPath("data");
        string outputPath = testRepository.GetExternalPath("patches");
        var buildCommand = new BuildCommand();
        buildCommand.Execute(new BuildArguments(sourcePath, outputPath));
        ManifestGroup group = Assert.Single(ManifestStore.ReadPrevious(outputPath)!.Groups);
        string artifactName = Assert.Single(group.Entries).Name!;
        string artifactPath = Path.Combine(
            outputPath,
            artifactName.Replace('/', Path.DirectorySeparatorChar));
        byte[] corruptedBytes = File.ReadAllBytes(artifactPath);
        corruptedBytes[0] ^= 0xff;
        File.WriteAllBytes(artifactPath, corruptedBytes);

        buildCommand.Execute(new BuildArguments(sourcePath, outputPath));
        ArtifactMismatch mismatch = Assert.Single(_sut.Execute(new VerifyArguments(outputPath)));

        Assert.Equal(artifactName, mismatch.Name);
        Assert.Contains("checksum이 다릅니다", mismatch.Reason, StringComparison.Ordinal);
    }

    private sealed class VerifyTestEnvironment : IDisposable
    {
        public const string ARCHIVE_NAME = "archives/content/1.gpka";
        public const string FILE_NAME = "files/content/1/overlay.bin.v1.1";

        private readonly string _testPath;

        public string FileChecksum { get; }
        public string ManifestPath => Path.Combine(OutputPath, "manifest.json");
        public string OutputPath { get; }
        public string RootPath => _testPath;

        public VerifyTestEnvironment()
        {
            _testPath = Path.Combine(Path.GetTempPath(), $"GamePatchKit-{Guid.NewGuid():N}");
            OutputPath = Path.Combine(_testPath, "patches");
            StoredArtifact archive = WriteArtifact(ARCHIVE_NAME, "archive-payload"u8.ToArray());
            StoredArtifact file = WriteArtifact(FILE_NAME, "file-payload"u8.ToArray());
            FileChecksum = file.Checksum;
            ManifestStore.WriteAtomically(
                OutputPath,
                new PatchManifest
                {
                    SchemaVersion = PatchManifest.CURRENT_SCHEMA_VERSION,
                    SourcePath = "data",
                    SourceCommit = new string('a', 40),
                    Groups = new[]
                    {
                        new ManifestGroup
                        {
                            Id = "content",
                            Version = 1,
                            Packing = PackingKind.Group,
                            Compression = CompressionKind.None,
                            Archive = new ManifestArchive
                            {
                                Name = ARCHIVE_NAME,
                                PayloadSize = 15,
                                StoredSize = archive.StoredSize,
                                Checksum = archive.Checksum
                            },
                            Entries = new[]
                            {
                                new ManifestEntry
                                {
                                    Path = "base.bin",
                                    Version = "1.0",
                                    Size = 15,
                                    Source = EntrySource.Archive,
                                    Offset = 0,
                                    Length = 15
                                },
                                new ManifestEntry
                                {
                                    Path = "overlay.bin",
                                    Version = "1.1",
                                    Size = 12,
                                    Source = EntrySource.File,
                                    Name = FILE_NAME,
                                    StoredSize = file.StoredSize,
                                    Checksum = file.Checksum
                                }
                            }
                        }
                    }
                });
        }

        public void Dispose()
        {
            Directory.Delete(_testPath, recursive: true);
        }

        public void Damage(string artifactName, string damageKind)
        {
            string artifactPath = GetArtifactPath(artifactName);

            if (damageKind == "missing")
            {
                File.Delete(artifactPath);
                return;
            }

            if (damageKind == "truncated")
            {
                File.WriteAllBytes(artifactPath, [0]);
                return;
            }

            byte[] bytes = File.ReadAllBytes(artifactPath);
            bytes[0] ^= 0xff;
            File.WriteAllBytes(artifactPath, bytes);
        }

        public string GetArtifactPath(string name)
        {
            return Path.Combine(OutputPath, name.Replace('/', Path.DirectorySeparatorChar));
        }

        private StoredArtifact WriteArtifact(string name, byte[] bytes)
        {
            string path = GetArtifactPath(name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return ArtifactWriter.ReadStored(path);
        }
    }
}