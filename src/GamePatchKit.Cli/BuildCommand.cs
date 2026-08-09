using System.Diagnostics;

namespace GamePatchKit.Cli;

internal sealed class BuildCommand
{
    public void Execute(BuildArguments arguments)
    {
        string sourcePath = ResolvePath(arguments.SourcePath);
        string outputPath = ResolvePath(arguments.OutputPath);
        string repositoryRoot = ResolvePath(GetRepositoryRoot(sourcePath));

        if (IsInsideOrEqual(repositoryRoot, outputPath))
        {
            throw new BuildException("--output은 데이터 루트가 속한 Git 저장소 바깥에 있어야 합니다.");
        }

        PatchManifest? previousManifest = ManifestStore.ReadPrevious(outputPath);
        string currentSourcePath = GetManifestSourcePath(repositoryRoot, sourcePath);

        if (previousManifest is not null
            && !string.Equals(previousManifest.SourcePath, currentSourcePath, StringComparison.Ordinal))
        {
            throw new BuildException("데이터 루트가 이전 빌드와 다릅니다.");
        }

        throw new BuildException("build 명령의 패치 생성은 아직 구현되지 않았습니다.");
    }

    private static string GetRepositoryRoot(string sourcePath)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add("rev-parse");
        startInfo.ArgumentList.Add("--show-toplevel");

        using Process process = Process.Start(startInfo)
            ?? throw new BuildException("git 프로세스를 시작하지 못했습니다.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            string detail = standardError.Trim();
            string message = "--source는 Git 저장소의 최상위 폴더이거나 그 하위 폴더여야 합니다.";
            throw new BuildException(string.IsNullOrEmpty(detail) ? message : $"{message} {detail}");
        }

        return standardOutput.TrimEnd('\r', '\n');
    }

    private static string ResolvePath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string rootPath = Path.GetPathRoot(fullPath)
            ?? throw new BuildException("경로의 최상위 폴더를 계산하지 못했습니다.");
        string resolvedPath = rootPath;
        string relativePath = fullPath[rootPath.Length..];
        char[] separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        string[] pathSegments = relativePath.Split(separators, StringSplitOptions.RemoveEmptyEntries);

        foreach (string pathSegment in pathSegments)
        {
            string candidatePath = Path.Combine(resolvedPath, pathSegment);

            if (Directory.Exists(candidatePath))
            {
                var directory = new DirectoryInfo(candidatePath);
                resolvedPath = directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? candidatePath;
                continue;
            }

            resolvedPath = candidatePath;
        }

        return Path.GetFullPath(resolvedPath);
    }

    private static bool IsInsideOrEqual(string parentPath, string candidatePath)
    {
        string relativePath = Path.GetRelativePath(parentPath, candidatePath);

        if (relativePath == ".")
        {
            return true;
        }

        string parentPrefix = $"..{Path.DirectorySeparatorChar}";
        return relativePath != ".."
            && !relativePath.StartsWith(parentPrefix, StringComparison.Ordinal)
            && !Path.IsPathRooted(relativePath);
    }

    private static string GetManifestSourcePath(string repositoryRoot, string sourcePath)
    {
        string relativePath = Path.GetRelativePath(repositoryRoot, sourcePath);

        if (relativePath == ".")
        {
            return relativePath;
        }

        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }
}