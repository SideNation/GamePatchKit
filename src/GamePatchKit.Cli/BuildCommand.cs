namespace GamePatchKit.Cli;

internal sealed class BuildCommand
{
    public BuildSummary Execute(BuildArguments arguments)
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

        var artifactWriter = new ArtifactWriter();
        var manifestGroups = new List<ManifestGroup>(configuration.Groups.Count);
        var groupSummaries = new List<GroupBuildSummary>(configuration.Groups.Count);
        var fileRevisionAdjustments = new List<FileRevisionAdjustment>();

        foreach (GroupConfiguration group in configuration.Groups.OrderBy(group => group.Id, StringComparer.Ordinal))
        {
            (ManifestGroup Manifest, GroupBuildSummary Summary) result = incrementalGroupIds.Contains(group.Id)
                ? BuildIncrementalGroup(
                    outputPath,
                    group,
                    entriesByGroup[group.Id],
                    previousGroups[group.Id],
                    changedPaths,
                    artifactWriter,
                    fileRevisionAdjustments)
                : group.Packing == PackingKind.Group
                    ? BuildArchiveGroup(outputPath, group, entriesByGroup[group.Id], artifactWriter)
                    : BuildFileGroup(
                        outputPath,
                        group,
                        entriesByGroup[group.Id],
                        artifactWriter,
                        fileRevisionAdjustments);
            manifestGroups.Add(result.Manifest);
            groupSummaries.Add(result.Summary);
        }

        var candidateManifest = new PatchManifest
        {
            SchemaVersion = PatchManifest.CURRENT_SCHEMA_VERSION,
            SourcePath = repository.SourcePath,
            SourceCommit = currentCommit,
            Groups = manifestGroups
        };
        long releaseVersion = ResolveReleaseVersion(previousManifest, candidateManifest);
        ManifestStore.WriteAtomically(
            outputPath,
            new PatchManifest
            {
                SchemaVersion = candidateManifest.SchemaVersion,
                ReleaseVersion = releaseVersion,
                SourcePath = candidateManifest.SourcePath,
                SourceCommit = candidateManifest.SourceCommit,
                Groups = candidateManifest.Groups
            });
        return new BuildSummary(groupSummaries, fileRevisionAdjustments);
    }

    private static long ResolveReleaseVersion(PatchManifest? previousManifest, PatchManifest candidateManifest)
    {
        if (previousManifest is null)
        {
            return 0;
        }

        if (ManifestStore.HasSameReleaseContent(previousManifest, candidateManifest))
        {
            return previousManifest.ReleaseVersion;
        }

        if (previousManifest.ReleaseVersion == long.MaxValue)
        {
            throw new BuildException("releaseVersion을 더 늘릴 수 없습니다.");
        }

        return previousManifest.ReleaseVersion + 1;
    }

    private static (ManifestGroup Manifest, GroupBuildSummary Summary) BuildArchiveGroup(
        string outputPath,
        GroupConfiguration group,
        IReadOnlyList<SourceEntry> entries,
        ArtifactWriter artifactWriter)
    {
        WrittenArchive written = artifactWriter.WriteArchive(outputPath, group, entries);
        string entryVersion = $"{group.Version}.0";

        var manifest = new ManifestGroup
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
        var summary = new GroupBuildSummary(
            group.Id,
            group.Version,
            manifest.Entries.Count,
            written.IsCreated,
            FileObjectCount: 0,
            written.IsCreated ? written.StoredSize : 0);
        return (manifest, summary);
    }

    private static (ManifestGroup Manifest, GroupBuildSummary Summary) BuildFileGroup(
        string outputPath,
        GroupConfiguration group,
        IReadOnlyList<SourceEntry> entries,
        ArtifactWriter artifactWriter,
        ICollection<FileRevisionAdjustment> fileRevisionAdjustments)
    {
        string requestedVersion = $"{group.Version}.0";
        var manifestEntries = new List<ManifestEntry>(entries.Count);
        int fileObjectCount = 0;
        long writtenBytes = 0;

        foreach (SourceEntry entry in entries)
        {
            WrittenArtifact written = artifactWriter.WriteFile(outputPath, group, entry, requestedVersion);
            manifestEntries.Add(
                TrackWrittenFile(
                    group,
                    entry,
                    requestedVersion,
                    written,
                    fileRevisionAdjustments,
                    ref fileObjectCount,
                    ref writtenBytes));
        }

        var manifest = new ManifestGroup
        {
            Id = group.Id,
            Version = group.Version,
            Packing = group.Packing,
            Compression = group.Compression,
            Entries = manifestEntries
        };
        var summary = new GroupBuildSummary(
            group.Id,
            group.Version,
            manifestEntries.Count,
            IsArchiveCreated: false,
            fileObjectCount,
            writtenBytes);
        return (manifest, summary);
    }

    private static (ManifestGroup Manifest, GroupBuildSummary Summary) BuildIncrementalGroup(
        string outputPath,
        GroupConfiguration group,
        IReadOnlyList<SourceEntry> entries,
        ManifestGroup previousGroup,
        IReadOnlySet<string> changedPaths,
        ArtifactWriter artifactWriter,
        ICollection<FileRevisionAdjustment> fileRevisionAdjustments)
    {
        var previousEntries = previousGroup.Entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        var manifestEntries = new List<ManifestEntry>(entries.Count);
        int fileObjectCount = 0;
        long writtenBytes = 0;

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
                TrackWrittenFile(
                    group,
                    entry,
                    requestedVersion,
                    written,
                    fileRevisionAdjustments,
                    ref fileObjectCount,
                    ref writtenBytes));
        }

        var manifest = new ManifestGroup
        {
            Id = group.Id,
            Version = group.Version,
            Packing = group.Packing,
            Compression = group.Compression,
            Archive = previousGroup.Archive,
            Entries = manifestEntries
        };
        var summary = new GroupBuildSummary(
            group.Id,
            group.Version,
            manifestEntries.Count,
            IsArchiveCreated: false,
            fileObjectCount,
            writtenBytes);
        return (manifest, summary);
    }

    private static ManifestEntry TrackWrittenFile(
        GroupConfiguration group,
        SourceEntry entry,
        string requestedVersion,
        WrittenArtifact written,
        ICollection<FileRevisionAdjustment> fileRevisionAdjustments,
        ref int fileObjectCount,
        ref long writtenBytes)
    {
        if (written.IsCreated)
        {
            fileObjectCount++;
            writtenBytes = checked(writtenBytes + written.StoredSize);
        }

        if (!string.Equals(requestedVersion, written.Version, StringComparison.Ordinal))
        {
            fileRevisionAdjustments.Add(
                new FileRevisionAdjustment(group.Id, entry.Path, requestedVersion, written.Version));
        }

        return new ManifestEntry
        {
            Path = entry.Path,
            Version = written.Version,
            Size = entry.Size,
            Source = EntrySource.File,
            Name = written.Name,
            StoredSize = written.StoredSize,
            Checksum = written.Checksum
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