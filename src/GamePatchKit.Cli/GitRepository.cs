using System.Diagnostics;

namespace GamePatchKit.Cli;

internal sealed class GitRepository
{
    private readonly string _sourcePath;

    public string RepositoryRoot { get; private set; } = null!;
    public string SourcePath { get; private set; } = null!;
    public string SourcePrefix { get; private set; } = null!;

    public GitRepository(string sourcePath)
    {
        _sourcePath = sourcePath;
    }

    public void EnsureConfigurationTracked()
    {
        string configurationPath = $"{SourcePrefix}{BuildConfigurationLoader.FILE_NAME}";
        (int exitCode, _, _) = RunGit(RepositoryRoot, "ls-files", "--error-unmatch", "--", configurationPath);

        if (exitCode != 0)
        {
            throw new BuildException("gamepatchkit.yml은 Git 추적 대상이어야 합니다.");
        }
    }

    public void EnsureSourceIsInRepository()
    {
        const string failureMessage = "--source는 Git 저장소의 최상위 폴더이거나 그 하위 폴더여야 합니다.";
        string repositoryRoot = RunRequired(_sourcePath, failureMessage, "rev-parse", "--show-toplevel");
        string sourcePrefix = RunRequired(_sourcePath, failureMessage, "rev-parse", "--show-prefix");
        RepositoryRoot = Path.GetFullPath(repositoryRoot.TrimEnd('\r', '\n'));
        SourcePrefix = sourcePrefix.TrimEnd('\r', '\n');
        SourcePath = SourcePrefix.Length == 0 ? "." : SourcePrefix.TrimEnd('/');

        if (!RelativePathValidator.IsNormalized(SourcePath, allowRepositoryRoot: true))
        {
            throw new BuildException("--source의 저장소 기준 경로는 정규화된 상대 경로여야 합니다.");
        }
    }

    public void EnsureTrackedSourceClean()
    {
        string output = RunRequired(
            RepositoryRoot,
            "Git 상태를 확인하지 못했습니다.",
            "status",
            "--porcelain=v1",
            "-z",
            "--untracked-files=no",
            "--no-renames",
            "--",
            GetSourcePathspec());

        if (output.Length > 0)
        {
            throw new BuildException("--source 아래에 커밋되지 않은 추적 파일이 있습니다.");
        }
    }

    public IReadOnlyList<string> GetChangedPaths(string previousCommit, string currentCommit)
    {
        string output = RunRequired(
            RepositoryRoot,
            "Git 변경 경로를 확인하지 못했습니다.",
            "diff",
            "--name-only",
            "--no-renames",
            "-z",
            previousCommit,
            currentCommit,
            "--",
            GetSourcePathspec());
        return ParseSourcePaths(output);
    }

    public string GetHeadCommit()
    {
        string output = RunRequired(RepositoryRoot, "Git HEAD를 확인하지 못했습니다.", "rev-parse", "HEAD");
        return output.TrimEnd('\r', '\n');
    }

    public IReadOnlyList<string> GetTrackedPaths()
    {
        string output = RunRequired(
            RepositoryRoot,
            "Git 추적 파일을 확인하지 못했습니다.",
            "ls-files",
            "-z",
            "--",
            GetSourcePathspec());
        return ParseSourcePaths(output);
    }

    public bool HasCommit(string commit)
    {
        (int exitCode, _, _) = RunGit(RepositoryRoot, "cat-file", "-e", $"{commit}^{{commit}}");
        return exitCode == 0;
    }

    private string GetSourcePathspec()
    {
        return SourcePrefix.Length == 0 ? "." : SourcePrefix;
    }

    private IReadOnlyList<string> ParseSourcePaths(string output)
    {
        return output
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(ToSourceRelativePath)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private string ToSourceRelativePath(string repositoryRelativePath)
    {
        if (SourcePrefix.Length == 0)
        {
            return repositoryRelativePath;
        }

        if (!repositoryRelativePath.StartsWith(SourcePrefix, StringComparison.Ordinal))
        {
            throw new BuildException("Git이 --source 바깥의 경로를 반환했습니다.");
        }

        return repositoryRelativePath[SourcePrefix.Length..];
    }

    private static (int ExitCode, string StandardOutput, string StandardError) RunGit(string directory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(directory);

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new BuildException("git 프로세스를 시작하지 못했습니다.");
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        string standardOutput = standardOutputTask.GetAwaiter().GetResult();
        string standardError = standardErrorTask.GetAwaiter().GetResult();
        return (process.ExitCode, standardOutput, standardError);
    }

    private static string RunRequired(string directory, string failureMessage, params string[] arguments)
    {
        (int exitCode, string standardOutput, string standardError) = RunGit(directory, arguments);

        if (exitCode == 0)
        {
            return standardOutput;
        }

        string detail = standardError.Trim();
        throw new BuildException(string.IsNullOrEmpty(detail) ? failureMessage : $"{failureMessage} {detail}");
    }
}