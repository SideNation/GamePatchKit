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

        Dictionary<string, ManifestGroup> previousGroups = previousManifest?.Groups.ToDictionary(
            group => group.Id,
            StringComparer.Ordinal)
            ?? new Dictionary<string, ManifestGroup>(StringComparer.Ordinal);
        HashSet<string> incrementalGroupIds = ValidateGroupVersions(configuration.Groups, previousGroups);
        bool hasIncrementalGroup = incrementalGroupIds.Count > 0;

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

        HashSet<string> changedPaths = hasIncrementalGroup
            ? repository.GetChangedPaths(previousManifest!.SourceCommit, currentCommit).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        ValidateInheritedArtifacts(
            outputPath,
            configuration.Groups,
            entriesByGroup,
            previousGroups,
            incrementalGroupIds,
            changedPaths);

        if (configuration.Groups.Any(group => group.Packing == PackingKind.File))
        {
            throw new BuildException("build 명령의 패치 생성은 아직 구현되지 않았습니다.");
        }

        var artifactWriter = new ArtifactWriter();
        ManifestGroup[] manifestGroups = configuration.Groups
            .OrderBy(group => group.Id, StringComparer.Ordinal)
            .Select(
                group => incrementalGroupIds.Contains(group.Id)
                    ? BuildIncrementalArchiveGroup(
                        outputPath,
                        group,
                        entriesByGroup[group.Id],
                        previousGroups[group.Id],
                        changedPaths,
                        artifactWriter)
                    : BuildArchiveGroup(outputPath, group, entriesByGroup[group.Id], artifactWriter))
            .ToArray();
        ManifestStore.WriteAtomically(
            outputPath,
            new PatchManifest
            {
                SchemaVersion = PatchManifest.CURRENT_SCHEMA_VERSION,
                SourcePath = repository.SourcePath,
                SourceCommit = currentCommit,
                Groups = manifestGroups
            });
    }

    private static ManifestGroup BuildArchiveGroup(
        string outputPath,
        GroupConfiguration group,
        IReadOnlyList<SourceEntry> entries,
        ArtifactWriter artifactWriter)
    {
        WrittenArchive written = artifactWriter.WriteArchive(outputPath, group, entries);
        string entryVersion = $"{group.Version}.0";

        return new ManifestGroup
        {
            Id = group.Id,
            Version = group.Version,
            Packing = group.Packing,
            Compression = group.Compression,
            Archive = new ManifestArchive
            {
                Name = written.Name,
                PayloadSize = written.PayloadSize,
                StoredSize = written.StoredSize,
                Checksum = written.Checksum
            },
            Entries = written.Layout
                .Select(
                    layout => new ManifestEntry
                    {
                        Path = layout.Path,
                        Version = entryVersion,
                        Size = layout.Length,
                        Source = EntrySource.Archive,
                        Offset = layout.Offset,
                        Length = layout.Length
                    })
                .ToArray()
        };
    }

    private static ManifestGroup BuildIncrementalArchiveGroup(
        string outputPath,
        GroupConfiguration group,
        IReadOnlyList<SourceEntry> entries,
        ManifestGroup previousGroup,
        IReadOnlySet<string> changedPaths,
        ArtifactWriter artifactWriter)
    {
        var previousEntries = previousGroup.Entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        var manifestEntries = new List<ManifestEntry>(entries.Count);

        foreach (SourceEntry entry in entries)
        {
            previousEntries.TryGetValue(entry.Path, out ManifestEntry? previousEntry);
            bool isChanged = changedPaths.Contains(GetSourceRelativePath(group.Id, entry.Path));

            if (previousEntry is not null && !isChanged)
            {
                manifestEntries.Add(previousEntry);
                continue;
            }

            int requestedRevision = 0;

            if (previousEntry is not null)
            {
                _ = ManifestStore.TryParseEntryVersion(previousEntry.Version, group.Version, out int previousRevision);

                if (previousRevision == int.MaxValue)
                {
                    throw new BuildException($"파일 '{entry.Path}'의 리비전을 더 늘릴 수 없습니다.");
                }

                requestedRevision = previousRevision + 1;
            }

            string requestedVersion = $"{group.Version}.{requestedRevision}";
            WrittenArtifact written = artifactWriter.WriteFile(outputPath, group, entry, requestedVersion);
            manifestEntries.Add(
                new ManifestEntry
                {
                    Path = entry.Path,
                    Version = written.Version,
                    Size = entry.Size,
                    Source = EntrySource.File,
                    Name = written.Name,
                    StoredSize = written.StoredSize,
                    Checksum = written.Checksum
                });
        }

        return new ManifestGroup
        {
            Id = group.Id,
            Version = group.Version,
            Packing = group.Packing,
            Compression = group.Compression,
            Archive = previousGroup.Archive,
            Entries = manifestEntries
        };
    }

    private static void ValidateInheritedArtifacts(
        string outputPath,
        IReadOnlyList<GroupConfiguration> groups,
        IReadOnlyDictionary<string, List<SourceEntry>> entriesByGroup,
        IReadOnlyDictionary<string, ManifestGroup> previousGroups,
        IReadOnlySet<string> incrementalGroupIds,
        IReadOnlySet<string> changedPaths)
    {
        foreach (GroupConfiguration group in groups.OrderBy(group => group.Id, StringComparer.Ordinal))
        {
            if (!incrementalGroupIds.Contains(group.Id))
            {
                continue;
            }

            ManifestGroup previousGroup = previousGroups[group.Id];

            if (previousGroup.Archive is not null)
            {
                ValidateInheritedArtifact(outputPath, previousGroup.Archive.Name, previousGroup.Archive.StoredSize);
            }

            var previousEntries = previousGroup.Entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);

            foreach (SourceEntry entry in entriesByGroup[group.Id])
            {
                if (changedPaths.Contains(GetSourceRelativePath(group.Id, entry.Path))
                    || !previousEntries.TryGetValue(entry.Path, out ManifestEntry? previousEntry)
                    || previousEntry.Source != EntrySource.File)
                {
                    continue;
                }

                ValidateInheritedArtifact(outputPath, previousEntry.Name!, previousEntry.StoredSize!.Value);
            }
        }
    }

    private static void ValidateInheritedArtifact(string outputPath, string name, long expectedSize)
    {
        string path = Path.Combine(outputPath, name.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(path))
        {
            throw new BuildException($"승계 산출물이 없습니다: {name}");
        }

        long actualSize = new FileInfo(path).Length;

        if (actualSize != expectedSize)
        {
            throw new BuildException(
                $"승계 산출물의 크기가 다릅니다: {name} (expected: {expectedSize}, actual: {actualSize})");
        }
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

    private static string GetSourceRelativePath(string groupId, string entryPath)
    {
        return $"{groupId}/{entryPath}";
    }

    private static HashSet<string> ValidateGroupVersions(
        IReadOnlyList<GroupConfiguration> groups,
        IReadOnlyDictionary<string, ManifestGroup> previousGroups)
    {
        var incrementalGroupIds = new HashSet<string>(StringComparer.Ordinal);

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
                if (group.Packing != previousGroup.Packing || group.Compression != previousGroup.Compression)
                {
                    throw new BuildException(
                        $"그룹 '{group.Id}'의 packing 또는 compression이 이전 성공 설정과 다릅니다. 그룹 버전을 올리세요.");
                }

                incrementalGroupIds.Add(group.Id);
            }
        }

        return incrementalGroupIds;
    }
}