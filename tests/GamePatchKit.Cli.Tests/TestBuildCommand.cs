using System.Diagnostics;
using GamePatchKit.Cli;

namespace GamePatchKit.Cli.Tests;

public sealed class TestBuildCommand
{
    private readonly BuildCommand _sut = new();

    [Fact]
    public void Execute_OutputIsInsideRepository_ThrowsWithoutCreatingOutput()
    {
        string repositoryRoot = Path.Combine(Path.GetTempPath(), $"GamePatchKit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryRoot);

        try
        {
            RunGit(repositoryRoot, "init", "--quiet");
            string sourcePath = Path.Combine(repositoryRoot, "data");
            string outputPath = Path.Combine(repositoryRoot, "patches");
            Directory.CreateDirectory(sourcePath);

            BuildException exception = Assert.Throws<BuildException>(
                () => _sut.Execute(new BuildArguments(sourcePath, outputPath)));

            Assert.Contains("Git 저장소 바깥", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(outputPath));
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public void Execute_SourceIsSymbolicLinkAndOutputIsOutsideRepository_DoesNotReportRepositoryOutput()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string testRoot = Path.Combine(Path.GetTempPath(), $"GamePatchKit-{Guid.NewGuid():N}");
        string repositoryRoot = Path.Combine(testRoot, "repository");
        string sourceTargetPath = Path.Combine(repositoryRoot, "data");
        string sourceLinkPath = Path.Combine(testRoot, "source-link");
        string outputPath = Path.Combine(testRoot, "patches");
        Directory.CreateDirectory(sourceTargetPath);

        try
        {
            RunGit(repositoryRoot, "init", "--quiet");
            Directory.CreateSymbolicLink(sourceLinkPath, sourceTargetPath);

            BuildException exception = Assert.Throws<BuildException>(
                () => _sut.Execute(new BuildArguments(sourceLinkPath, outputPath)));

            Assert.DoesNotContain("Git 저장소 바깥", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(outputPath));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public void Execute_PreviousManifestHasDifferentSourcePath_ThrowsWithoutChangingManifest()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), $"GamePatchKit-{Guid.NewGuid():N}");
        string repositoryRoot = Path.Combine(testRoot, "repository");
        string sourcePath = Path.Combine(repositoryRoot, "data");
        string outputPath = Path.Combine(testRoot, "patches");
        Directory.CreateDirectory(sourcePath);

        try
        {
            RunGit(repositoryRoot, "init", "--quiet");
            ManifestStore.WriteAtomically(
                outputPath,
                new PatchManifest
                {
                    SchemaVersion = 1,
                    SourcePath = "other",
                    SourceCommit = "abc123",
                    Groups = Array.Empty<ManifestGroup>()
                });
            string manifestPath = Path.Combine(outputPath, "manifest.json");
            byte[] before = File.ReadAllBytes(manifestPath);

            BuildException exception = Assert.Throws<BuildException>(
                () => _sut.Execute(new BuildArguments(sourcePath, outputPath)));

            Assert.Contains("데이터 루트가 이전 빌드와 다릅니다", exception.Message, StringComparison.Ordinal);
            Assert.Equal(before, File.ReadAllBytes(manifestPath));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("git 프로세스를 시작하지 못했습니다.");
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, standardError);
    }
}