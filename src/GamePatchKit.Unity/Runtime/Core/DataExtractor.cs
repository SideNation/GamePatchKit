#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ZstdSharp;

namespace GamePatchKit.Unity
{
    internal sealed class ExtractSummary
    {
        public ExtractSummary(int extractedCount, int removedCount)
        {
            ExtractedCount = extractedCount;
            RemovedCount = removedCount;
        }

        public int ExtractedCount { get; }

        public int RemovedCount { get; }
    }

    // 받은 산출물은 압축된 아카이브와 파일 객체라 게임이 그대로 읽을 수 없다. 매니페스트의 compression·offset·length대로
    // 원본 트리를 <root>/data 아래에 복원한다. CLI DataExtractor와 같은 규칙이다.
    internal static class DataExtractor
    {
        internal const string DATA_DIRECTORY_NAME = "data";
        private const int BufferSize = 64 * 1024;

        // known은 로컬에 남아 있는 매니페스트 전부다. 현재 세대와, 중단돼 남은 목표 세대들이 여기 들어간다. 트리의
        // 각 파일은 그중 하나의 해제 결과이므로, 전부가 목표와 같은 엔트리만 건드리지 않고 나머지는 다시 푼다.
        // 산출물 이름과 checksum이 불변이라 엔트리 정보가 같으면 해제한 내용도 같다. 취소는 파일과 버퍼 단위
        // 경계에서 반영한다.
        public static ExtractSummary Execute(
            string rootPath,
            PatchManifest target,
            IReadOnlyList<PatchManifest> known,
            CancellationToken cancellationToken)
        {
            string dataPath = Path.Combine(rootPath, DATA_DIRECTORY_NAME);
            HashSet<string> expectedPaths = CollectExpectedPaths(target);
            IReadOnlyList<Dictionary<string, string>> knownIdentities = CollectIdentities(known);

            // 지우는 것이 먼저다. 이전 세대에서 파일이던 경로가 이번 세대에 폴더가 되면 그 파일을 지워야 폴더를 만들 수 있다.
            int removedCount = Remove(dataPath, dataPath, expectedPaths, cancellationToken);
            int extractedCount = 0;

            foreach (ManifestGroup group in target.Groups.OrderBy(group => group.Id, StringComparer.Ordinal))
            {
                extractedCount += ExtractGroup(rootPath, dataPath, group, knownIdentities, cancellationToken);
            }

            return new ExtractSummary(extractedCount, removedCount);
        }

        // 다시 풀어야 하는 엔트리가 읽을 산출물만 고른다. 바뀌지 않은 엔트리만 담긴 아카이브는 받지 않는다.
        public static IReadOnlyCollection<string> CollectRequiredArtifacts(
            string rootPath,
            PatchManifest target,
            IReadOnlyList<PatchManifest> known)
        {
            string dataPath = Path.Combine(rootPath, DATA_DIRECTORY_NAME);
            IReadOnlyList<Dictionary<string, string>> knownIdentities = CollectIdentities(known);
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (ManifestGroup group in target.Groups)
            {
                foreach (ManifestEntry entry in group.Entries)
                {
                    if (!NeedsExtraction(dataPath, group, entry, knownIdentities))
                    {
                        continue;
                    }

                    names.Add(entry.Source == EntrySource.Archive ? group.Archive!.Name : entry.Name!);
                }
            }

            return names;
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
                        throw new PatchClientException($"서로 다른 그룹이 같은 데이터 경로를 가리킵니다: {relativePath}");
                    }
                }
            }

            return expectedPaths;
        }

        private static IReadOnlyList<Dictionary<string, string>> CollectIdentities(IReadOnlyList<PatchManifest> known)
        {
            var collected = new List<Dictionary<string, string>>(known.Count);

            foreach (PatchManifest manifest in known)
            {
                var identities = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (ManifestGroup group in manifest.Groups)
                {
                    foreach (ManifestEntry entry in group.Entries)
                    {
                        identities[GetEntryPath(group.Id, entry.Path)] = GetIdentity(group, entry);
                    }
                }

                collected.Add(identities);
            }

            return collected;
        }

        private static string GetIdentity(ManifestGroup group, ManifestEntry entry)
        {
            return entry.Source == EntrySource.Archive
                ? $"{entry.Source}|{group.Compression}|{group.Archive!.Checksum}|{entry.Offset}|{entry.Length}"
                : $"{entry.Source}|{group.Compression}|{entry.Checksum}|{entry.Size}";
        }

        private static int Remove(
            string dataPath,
            string directoryPath,
            HashSet<string> expectedPaths,
            CancellationToken cancellationToken)
        {
            if (!Directory.Exists(directoryPath))
            {
                return 0;
            }

            int removedCount = 0;

            foreach (string filePath in Directory.EnumerateFiles(directoryPath).ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

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
                if ((new DirectoryInfo(childPath).Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    Directory.Delete(childPath);
                    removedCount++;
                    continue;
                }

                removedCount += Remove(dataPath, childPath, expectedPaths, cancellationToken);

                if (!Directory.EnumerateFileSystemEntries(childPath).Any())
                {
                    Directory.Delete(childPath);
                }
            }

            return removedCount;
        }

        private static int ExtractGroup(
            string rootPath,
            string dataPath,
            ManifestGroup group,
            IReadOnlyList<Dictionary<string, string>> knownIdentities,
            CancellationToken cancellationToken)
        {
            ManifestEntry[] pendingEntries = group.Entries
                .Where(entry => NeedsExtraction(dataPath, group, entry, knownIdentities))
                .ToArray();
            ManifestEntry[] archiveEntries = pendingEntries
                .Where(entry => entry.Source == EntrySource.Archive)
                .OrderBy(entry => entry.Offset!.Value)
                .ToArray();
            byte[] buffer = new byte[BufferSize];

            if (archiveEntries.Length > 0)
            {
                ExtractArchiveEntries(rootPath, dataPath, group, archiveEntries, buffer, cancellationToken);
            }

            foreach (ManifestEntry entry in pendingEntries.Where(entry => entry.Source == EntrySource.File))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using (Stream stored = OpenStored(GetLocalPath(rootPath, entry.Name!), group.Compression))
                    {
                        Write(dataPath, group.Id, entry, destination => CopyAll(stored, destination, buffer, cancellationToken));
                    }
                }
                catch (ZstdException exception)
                {
                    throw Undecodable(entry.Name!, exception);
                }
            }

            return pendingEntries.Length;
        }

        // 아카이브는 통짜로 압축돼 있어 중간으로 건너뛸 수 없다. offset 순서로 한 번만 훑으면서 필요한 구간만 꺼낸다.
        private static void ExtractArchiveEntries(
            string rootPath,
            string dataPath,
            ManifestGroup group,
            IReadOnlyList<ManifestEntry> entries,
            byte[] buffer,
            CancellationToken cancellationToken)
        {
            try
            {
                using (Stream stored = OpenStored(GetLocalPath(rootPath, group.Archive!.Name), group.Compression))
                {
                    long position = 0;

                    foreach (ManifestEntry entry in entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        long offset = entry.Offset!.Value;
                        long length = entry.Length!.Value;
                        Skip(stored, offset - position, buffer, cancellationToken);
                        Write(dataPath, group.Id, entry, destination => Copy(stored, destination, length, buffer, cancellationToken));
                        position = offset + length;
                    }
                }
            }
            catch (ZstdException exception)
            {
                throw Undecodable(group.Archive!.Name, exception);
            }
        }

        private static bool NeedsExtraction(
            string dataPath,
            ManifestGroup group,
            ManifestEntry entry,
            IReadOnlyList<Dictionary<string, string>> knownIdentities)
        {
            // 아무 매니페스트도 없으면 트리의 내용을 보증할 근거가 없다. 전부 다시 푼다.
            if (knownIdentities.Count == 0)
            {
                return true;
            }

            string relativePath = GetEntryPath(group.Id, entry.Path);
            string targetIdentity = GetIdentity(group, entry);

            foreach (Dictionary<string, string> identities in knownIdentities)
            {
                if (!identities.TryGetValue(relativePath, out string? identity) || identity != targetIdentity)
                {
                    return true;
                }
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
                    throw new PatchClientException(
                        $"해제한 파일의 크기가 다릅니다: {relativePath} "
                        + $"(expected: {entry.Size}, actual: {writtenSize})");
                }

                // 최종 이름을 얻는 것은 크기가 맞는 파일뿐이다.
                FileMover.MoveReplacing(temporaryPath, targetPath);
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

            return new DecompressionStream(stored, leaveOpen: false);
        }

        private static PatchClientException Undecodable(string name, ZstdException exception)
        {
            return new PatchClientException($"산출물을 해제하지 못했습니다: {name} ({exception.Message})", exception);
        }

        // 원본이 먼저 끝나면 쓴 만큼만 돌려준다. 부족한 것은 호출자의 크기 검사에서 걸린다.
        private static long Copy(Stream source, Stream destination, long length, byte[] buffer, CancellationToken cancellationToken)
        {
            long remaining = length;

            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
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

        private static long CopyAll(Stream source, Stream destination, byte[] buffer, CancellationToken cancellationToken)
        {
            long writtenSize = 0;
            int read;

            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                destination.Write(buffer, 0, read);
                writtenSize = checked(writtenSize + read);
            }

            return writtenSize;
        }

        private static void Skip(Stream source, long count, byte[] buffer, CancellationToken cancellationToken)
        {
            long remaining = count;

            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
}
