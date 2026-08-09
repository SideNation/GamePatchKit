namespace GamePatchKit.Cli;

internal sealed class BuildCommand
{
    public void Execute(BuildArguments arguments)
    {
        string sourcePath = ResolvePath(arguments.SourcePath);
        string outputPath = ResolvePath(arguments.OutputPath);
        var repository = new GitRepository(sourcePath);
        repository.EnsureSourceIsInRepository();
        string repositoryRoot = ResolvePath(repository.RepositoryRoot);

        if (IsInsideOrEqual(repositoryRoot, outputPath))
        {
            throw new BuildException("--output은 데이터 루트가 속한 Git 저장소 바깥에 있어야 합니다.");
        }

        repository.EnsureTrackedSourceClean();
        string currentCommit = repository.GetHeadCommit();
        repository.EnsureConfigurationTracked();
        BuildConfiguration configuration = BuildConfigurationLoader.Load(Path.Combine(sourcePath, BuildConfigurationLoader.FILE_NAME));
        PatchManifest? previousManifest = ManifestStore.ReadPrevious(outputPath);

        if (previousManifest is not null
            && !string.Equals(previousManifest.SourcePath, repository.SourcePath, StringComparison.Ordinal))
        {
            throw new BuildException("데이터 루트가 이전 빌드와 다릅니다.");
        }

        bool hasIncrementalGroup = ValidateGroupVersions(configuration.Groups, previousManifest);

        if (hasIncrementalGroup && !repository.HasCommit(previousManifest!.SourceCommit))
        {
            throw new BuildException("이전 상태가 없습니다.");
        }

        Dictionary<string, List<SourceEntry>> entriesByGroup = AssignTrackedEntries(
            sourcePath,
            configuration.Groups,
            repository.GetTrackedPaths());

        foreach (GroupConfiguration group in configuration.Groups.OrderBy(group => group.Id, StringComparer.Ordinal))
        {
            if (entriesByGroup[group.Id].Count == 0)
            {
                throw new BuildException($"그룹 '{group.Id}'에 Git 추적 엔트리가 없습니다. yaml에서 그룹을 제거하세요.");
            }
        }

        if (hasIncrementalGroup)
        {
            _ = repository.GetChangedPaths(previousManifest!.SourceCommit, currentCommit);
        }

        throw new BuildException("build 명령의 패치 생성은 아직 구현되지 않았습니다.");
    }

    private static Dictionary<string, List<SourceEntry>> AssignTrackedEntries(
        string sourcePath,
        IReadOnlyList<GroupConfiguration> groups,
        IReadOnlyList<string> trackedPaths)
    {
        var entriesByGroup = groups.ToDictionary(
            group => group.Id,
            _ => new List<SourceEntry>(),
            StringComparer.Ordinal);
        GroupConfiguration[] groupsByDepth = groups
            .OrderByDescending(group => group.Id.Count(character => character == '/'))
            .ThenBy(group => group.Id, StringComparer.Ordinal)
            .ToArray();

        foreach (string trackedPath in trackedPaths)
        {
            GroupConfiguration? owner = groupsByDepth.FirstOrDefault(group => IsInsideGroup(group.Id, trackedPath));

            if (owner is null)
            {
                continue;
            }

            string fullPath = Path.Combine(sourcePath, trackedPath.Replace('/', Path.DirectorySeparatorChar));
            var file = new FileInfo(fullPath);

            if (!file.Exists || file.LinkTarget is not null)
            {
                continue;
            }

            string entryPath = trackedPath[(owner.Id.Length + 1)..];

            if (!RelativePathValidator.IsNormalized(entryPath, allowRepositoryRoot: false))
            {
                throw new BuildException($"Git 추적 경로가 올바르지 않습니다: {trackedPath}");
            }

            entriesByGroup[owner.Id].Add(new SourceEntry(entryPath, file.FullName, file.Length));
        }

        foreach (List<SourceEntry> entries in entriesByGroup.Values)
        {
            entries.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        }

        return entriesByGroup;
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

    private static bool IsInsideGroup(string groupId, string path)
    {
        return path.StartsWith($"{groupId}/", StringComparison.Ordinal);
    }

    private static bool ValidateGroupVersions(
        IReadOnlyList<GroupConfiguration> groups,
        PatchManifest? previousManifest)
    {
        if (previousManifest is null)
        {
            return false;
        }

        var previousGroups = previousManifest.Groups.ToDictionary(group => group.Id, StringComparer.Ordinal);
        bool hasIncrementalGroup = false;

        foreach (GroupConfiguration group in groups)
        {
            if (!previousGroups.TryGetValue(group.Id, out ManifestGroup? previousGroup))
            {
                continue;
            }

            if (group.Version < previousGroup.Version)
            {
                throw new BuildException(
                    $"그룹 '{group.Id}'의 version이 이전 성공 버전 {previousGroup.Version}보다 작습니다. "
                    + "이전 성공 버전보다 큰 값을 사용하세요.");
            }

            if (group.Version == previousGroup.Version)
            {
                hasIncrementalGroup = true;
            }
        }

        return hasIncrementalGroup;
    }
}