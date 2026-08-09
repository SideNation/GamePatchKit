using System.Diagnostics;
using GamePatchKit.Cli;

namespace GamePatchKit.Cli.Tests;

public sealed class TestGitRepository
{
    [Fact]
    public void EnsureSourceIsInRepository_RootAndSubdirectory_ProvidesRepositoryPaths()
    {
        using var testRepository = new GitTestRepository();
        testRepository.WriteFile("data/file.txt", "data");
        testRepository.CommitAll("initial");

        var rootRepository = new GitRepository(testRepository.RepositoryPath);
        rootRepository.EnsureSourceIsInRepository();
        var childRepository = new GitRepository(testRepository.GetRepositoryPath("data"));
        childRepository.EnsureSourceIsInRepository();

        Assert.Equal(".", rootRepository.SourcePath);
        Assert.Empty(rootRepository.SourcePrefix);
        Assert.Equal("data", childRepository.SourcePath);
        Assert.Equal("data/", childRepository.SourcePrefix);
        Assert.Equal(rootRepository.RepositoryRoot, childRepository.RepositoryRoot);
    }

    [Fact]
    public void EnsureSourceIsInRepository_SourcePathCannotBeNormalized_ThrowsBuildException()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var testRepository = new GitTestRepository();
        testRepository.WriteFile("data\\root/file.txt", "data");
        testRepository.CommitAll("initial");
        var repository = new GitRepository(testRepository.GetRepositoryPath("data\\root"));

        BuildException exception = Assert.Throws<BuildException>(repository.EnsureSourceIsInRepository);

        Assert.Contains("정규화된 상대 경로", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetPaths_CommitsContainModifyAddDeleteAndRename_ReturnsSourceRelativePaths()
    {
        using var testRepository = new GitTestRepository();
        testRepository.WriteFile("data/gamepatchkit.yml", "groups: []");
        testRepository.WriteFile("data/a.txt", "before");
        testRepository.WriteFile("data/delete.txt", "delete");
        testRepository.WriteFile("data/old name.txt", "rename");
        testRepository.WriteFile("data/한글 파일.txt", "before");
        testRepository.WriteFile("outside.txt", "before");
        string previousCommit = testRepository.CommitAll("initial");
        testRepository.WriteFile("data/a.txt", "after");
        testRepository.DeleteFile("data/delete.txt");
        testRepository.RunGit("mv", "data/old name.txt", "data/new name.txt");
        testRepository.WriteFile("data/added.txt", "added");
        testRepository.WriteFile("data/한글 파일.txt", "after");
        testRepository.WriteFile("outside.txt", "after");
        string currentCommit = testRepository.CommitAll("changed");
        var repository = new GitRepository(testRepository.GetRepositoryPath("data"));
        repository.EnsureSourceIsInRepository();

        IReadOnlyList<string> trackedPaths = repository.GetTrackedPaths();
        IReadOnlyList<string> changedPaths = repository.GetChangedPaths(previousCommit, currentCommit);

        Assert.Equal(
            Sort("a.txt", "added.txt", "gamepatchkit.yml", "new name.txt", "한글 파일.txt"),
            trackedPaths);
        Assert.Equal(
            Sort("a.txt", "added.txt", "delete.txt", "new name.txt", "old name.txt", "한글 파일.txt"),
            changedPaths);
        Assert.Equal(currentCommit, repository.GetHeadCommit());
        Assert.True(repository.HasCommit(previousCommit));
        Assert.False(repository.HasCommit(new string('f', 40)));
    }

    [Fact]
    public void GetTrackedPaths_SourceContainsPathspecMagic_UsesLiteralSourcePath()
    {
        using var testRepository = new GitTestRepository();
        testRepository.WriteFile("data*/inside.txt", "inside");
        testRepository.WriteFile("data-other/outside.txt", "outside");
        testRepository.CommitAll("initial");
        var repository = new GitRepository(testRepository.GetRepositoryPath("data*"));
        repository.EnsureSourceIsInRepository();

        IReadOnlyList<string> trackedPaths = repository.GetTrackedPaths();

        Assert.Equal(new[] { "inside.txt" }, trackedPaths);
    }

    [Fact]
    public void EnsureTrackedSourceClean_UntrackedAndOutsideChangesAreIgnoredButTrackedSourceChangeThrows()
    {
        using var testRepository = new GitTestRepository();
        testRepository.WriteFile("data/gamepatchkit.yml", "groups: []");
        testRepository.WriteFile("data/tracked.txt", "before");
        testRepository.WriteFile("outside.txt", "before");
        testRepository.CommitAll("initial");
        var repository = new GitRepository(testRepository.GetRepositoryPath("data"));
        repository.EnsureSourceIsInRepository();
        testRepository.WriteFile("data/untracked.txt", "untracked");
        testRepository.WriteFile("outside.txt", "after");

        repository.EnsureTrackedSourceClean();

        testRepository.WriteFile("data/tracked.txt", "after");
        BuildException exception = Assert.Throws<BuildException>(repository.EnsureTrackedSourceClean);
        Assert.Contains("커밋되지 않은 추적 파일", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureConfigurationTracked_ConfigurationIsUntracked_ThrowsBuildException()
    {
        using var testRepository = new GitTestRepository();
        testRepository.WriteFile("data/tracked.txt", "tracked");
        testRepository.CommitAll("initial");
        testRepository.WriteFile("data/gamepatchkit.yml", "invalid: [");
        var repository = new GitRepository(testRepository.GetRepositoryPath("data"));
        repository.EnsureSourceIsInRepository();

        BuildException exception = Assert.Throws<BuildException>(repository.EnsureConfigurationTracked);

        Assert.Contains("gamepatchkit.yml", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Git 추적", exception.Message, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> Sort(params string[] paths)
    {
        return paths.OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }
}

internal sealed class GitTestRepository : IDisposable
{
    private readonly string _testPath;

    public string RepositoryPath { get; }

    public GitTestRepository()
    {
        _testPath = Path.Combine(Path.GetTempPath(), $"GamePatchKit-{Guid.NewGuid():N}");
        RepositoryPath = Path.Combine(_testPath, "repository");
        Directory.CreateDirectory(RepositoryPath);
        RunGit("init", "--quiet");
        RunGit("config", "user.email", "test@example.com");
        RunGit("config", "user.name", "GamePatchKit Tests");
        RunGit("config", "core.autocrlf", "false");
    }

    public string CommitAll(string message)
    {
        RunGit("add", "-A");
        RunGit("commit", "--quiet", "-m", message);
        return RunGit("rev-parse", "HEAD").TrimEnd('\r', '\n');
    }

    public void DeleteFile(string relativePath)
    {
        File.Delete(GetRepositoryPath(relativePath));
    }

    public void Dispose()
    {
        Directory.Delete(_testPath, recursive: true);
    }

    public string GetExternalPath(string relativePath)
    {
        return Path.Combine(_testPath, relativePath);
    }

    public string GetRepositoryPath(string relativePath)
    {
        return Path.Combine(RepositoryPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    public string RunGit(params string[] arguments)
    {
        return RunGitAt(RepositoryPath, arguments);
    }

    public void WriteFile(string relativePath, string contents)
    {
        string path = GetRepositoryPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    public static string RunGitAt(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("git 프로세스를 시작하지 못했습니다.");
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        string standardOutput = standardOutputTask.GetAwaiter().GetResult();
        string standardError = standardErrorTask.GetAwaiter().GetResult();

        Assert.True(process.ExitCode == 0, standardError);
        return standardOutput;
    }
}