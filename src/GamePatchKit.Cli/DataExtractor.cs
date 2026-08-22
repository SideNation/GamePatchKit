using System.IO.Compression;
using NativeCompressions;

namespace GamePatchKit.Cli;

internal sealed record ExtractSummary(int ExtractedCount, int RemovedCount);

// 미러가 받는 산출물은 압축된 아카이브와 파일 객체라 다른 서버가 그대로 읽을 수 없다. 매니페스트의
// compression·offset·length대로 원본 트리를 <output>/data 아래에 복원해 바로 읽을 수 있게 만든다.
internal static class DataExtractor
{
    internal const string DATA_DIRECTORY_NAME = "data";
    private const int BufferSize = 64 * 1024;

    // previous는 로컬이 지금 갖고 있는 세대다. 산출물 이름과 checksum이 불변이라 엔트리 정보가 같으면
    // 해제한 내용도 같으므로 바뀐 엔트리만 다시 푼다.
    public static ExtractSummary Execute(string outputPath, PatchManifest target, PatchManifest? previous)
    {
        string dataPath = Path.Combine(outputPath, DATA_DIRECTORY_NAME);
        HashSet<string> expectedPaths = CollectExpectedPaths(target);
        Dictionary<string, string> previousIdentities = CollectIdentities(previous);

        // 지우는 것이 먼저다. 이전 세대에서 파일이던 경로가 이번 세대에 폴더가 되면 그 파일을 지워야
        // 폴더를 만들 수 있다.
        int removedCount = Remove(dataPath, dataPath, expectedPaths);
        int extractedCount = 0;

        foreach (ManifestGroup group in target.Groups.OrderBy(group => group.Id, StringComparer.Ordinal))
        {
            extractedCount += ExtractGroup(outputPath, dataPath, group, previousIdentities);
        }

        return new ExtractSummary(extractedCount, removedCount);
    }

    private static HashSet<string> CollectExpectedPaths(PatchManifest manifest)
    {
        var expectedPaths = new HashSet<string>(StringComparer.Ordinal);

        foreach (ManifestGroup group in manifest.Groups)
        {
            foreach (ManifestEntry entry in group.Entries)
            {
                string relativePath = GetEntryPath(group.Id, entry.Path);

                if (!expectedPaths.Add(relativePath))
                {
                    throw new BuildException($"서로 다른 그룹이 같은 데이터 경로를 가리킵니다: {relativePath}");
                }
            }
        }

        return expectedPaths;
    }

    private static Dictionary<string, string> CollectIdentities(PatchManifest? manifest)
    {
        var identities = new Dictionary<string, string>(StringComparer.Ordinal);

        if (manifest is null)
        {
            return identities;
        }

        foreach (ManifestGroup group in manifest.Groups)
        {
            foreach (ManifestEntry entry in group.Entries)
            {
                identities[GetEntryPath(group.Id, entry.Path)] = GetIdentity(group, entry);
            }
        }

        return identities;
    }

    private static string GetIdentity(ManifestGroup group, ManifestEntry entry)
    {
        return entry.Source == EntrySource.Archive
            ? $"{entry.Source}|{group.Compression}|{group.Archive!.Checksum}|{entry.Offset}|{entry.Length}"
            : $"{entry.Source}|{group.Compression}|{entry.Checksum}|{entry.Size}";
    }

    private static int Remove(string dataPath, string directoryPath, IReadOnlySet<string> expectedPaths)
    {
        if (!Directory.Exists(directoryPath))
        {
            return 0;
        }

        int removedCount = 0;

        foreach (string filePath in Directory.EnumerateFiles(directoryPath).ToArray())
        {
            if (expectedPaths.Contains(ToDataRelativePath(dataPath, filePath)))
            {
                continue;
            }

            File.Delete(filePath);
            removedCount++;
        }

        foreach (string childPath in Directory.EnumerateDirectories(directoryPath).ToArray())
        {
            // 심볼릭 링크는 따라 들어가지 않는다. 링크만 지우면 대상 폴더는 건드리지 않는다.
            if (new DirectoryInfo(childPath).LinkTarget is not null)
            {
                Directory.Delete(childPath);
                removedCount++;
                continue;
            }

            removedCount += Remove(dataPath, childPath, expectedPaths);

            if (!Directory.EnumerateFileSystemEntries(childPath).Any())
            {
                Directory.Delete(childPath);
            }
        }

        return removedCount;
    }

    private static int ExtractGroup(
        string outputPath,
        string dataPath,
        ManifestGroup group,
        IReadOnlyDictionary<string, string> previousIdentities)
    {
        ManifestEntry[] pendingEntries = group.Entries
            .Where(entry => NeedsExtraction(dataPath, group, entry, previousIdentities))
            .ToArray();
        ManifestEntry[] archiveEntries = pendingEntries
            .Where(entry => entry.Source == EntrySource.Archive)
            .OrderBy(entry => entry.Offset!.Value)
            .ToArray();
        byte[] buffer = new byte[BufferSize];

        if (archiveEntries.Length > 0)
        {
            ExtractArchiveEntries(outputPath, dataPath, group, archiveEntries, buffer);
        }

        foreach (ManifestEntry entry in pendingEntries.Where(entry => entry.Source == EntrySource.File))
        {
            using Stream stored = OpenStored(GetLocalPath(outputPath, entry.Name!), group.Compression);
            Write(dataPath, group.Id, entry, destination => CopyAll(stored, destination, buffer));
        }

        return pendingEntries.Length;
    }

    // 아카이브는 통짜로 압축돼 있어 중간으로 건너뛸 수 없다. offset 순서로 한 번만 훑으면서 필요한
    // 구간만 꺼낸다.
    private static void ExtractArchiveEntries(
        string outputPath,
        string dataPath,
        ManifestGroup group,
        IReadOnlyList<ManifestEntry> entries,
        byte[] buffer)
    {
        using Stream stored = OpenStored(GetLocalPath(outputPath, group.Archive!.Name), group.Compression);
        long position = 0;

        foreach (ManifestEntry entry in entries)
        {
            long offset = entry.Offset!.Value;
            long length = entry.Length!.Value;
            Skip(stored, offset - position, buffer);
            Write(dataPath, group.Id, entry, destination => Copy(stored, destination, length, buffer));
            position = offset + length;
        }
    }

    private static bool NeedsExtraction(
        string dataPath,
        ManifestGroup group,
        ManifestEntry entry,
        IReadOnlyDictionary<string, string> previousIdentities)
    {
        string relativePath = GetEntryPath(group.Id, entry.Path);

        if (!previousIdentities.TryGetValue(relativePath, out string? identity)
            || identity != GetIdentity(group, entry))
        {
            return true;
        }

        // 같은 엔트리라도 트리에서 사라졌거나 크기가 다르면 다시 푼다.
        var file = new FileInfo(GetLocalPath(dataPath, relativePath));
        return !file.Exists || file.Length != entry.Size;
    }

    private static void Write(string dataPath, string groupId, ManifestEntry entry, Func<Stream, long> writePayload)
    {
        string relativePath = GetEntryPath(groupId, entry.Path);
        string targetPath = GetLocalPath(dataPath, relativePath);
        string targetDirectory = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(targetDirectory);
        string temporaryPath = Path.Combine(
            targetDirectory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            long writtenSize;

            using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.SequentialScan))
            {
                writtenSize = writePayload(output);
            }

            if (writtenSize != entry.Size)
            {
                throw new BuildException(
                    $"해제한 파일의 크기가 다릅니다: {relativePath} "
                    + $"(expected: {entry.Size}, actual: {writtenSize})");
            }

            // 최종 이름을 얻는 것은 크기가 맞는 파일뿐이다.
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static Stream OpenStored(string storedPath, CompressionKind compression)
    {
        var stored = new FileStream(
            storedPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.SequentialScan);

        if (compression == CompressionKind.None)
        {
            return stored;
        }

        return new ZstandardStream(stored, CompressionMode.Decompress, leaveOpen: false);
    }

    // 원본이 먼저 끝나면 쓴 만큼만 돌려준다. 부족한 것은 호출자의 크기 검사에서 걸린다.
    private static long Copy(Stream source, Stream destination, long length, byte[] buffer)
    {
        long remaining = length;

        while (remaining > 0)
        {
            int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));

            if (read == 0)
            {
                break;
            }

            destination.Write(buffer, 0, read);
            remaining -= read;
        }

        return length - remaining;
    }

    private static long CopyAll(Stream source, Stream destination, byte[] buffer)
    {
        long writtenSize = 0;
        int read;

        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            destination.Write(buffer, 0, read);
            writtenSize = checked(writtenSize + read);
        }

        return writtenSize;
    }

    private static void Skip(Stream source, long count, byte[] buffer)
    {
        long remaining = count;

        while (remaining > 0)
        {
            int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));

            if (read == 0)
            {
                return;
            }

            remaining -= read;
        }
    }

    private static string GetEntryPath(string groupId, string entryPath)
    {
        return $"{groupId}/{entryPath}";
    }

    private static string GetLocalPath(string rootPath, string relativePath)
    {
        return Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string ToDataRelativePath(string dataPath, string fullPath)
    {
        return Path.GetRelativePath(dataPath, fullPath).Replace(Path.DirectorySeparatorChar, '/');
    }
}
