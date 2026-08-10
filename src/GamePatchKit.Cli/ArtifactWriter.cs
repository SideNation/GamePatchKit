using System.Globalization;
using System.Security.Cryptography;
using NativeCompressions;

namespace GamePatchKit.Cli;

internal sealed record WrittenArtifact(
    string Name,
    string Version,
    long StoredSize,
    string Checksum,
    bool IsCreated);

internal sealed record ArchiveEntryLayout(string Path, long Offset, long Length);

internal sealed record WrittenArchive(
    string Name,
    long PayloadSize,
    long StoredSize,
    string Checksum,
    bool IsCreated,
    IReadOnlyList<ArchiveEntryLayout> Layout);

internal sealed record StoredArtifact(long StoredSize, string Checksum);

internal sealed class ArtifactWriter
{
    private const int BufferSize = 64 * 1024;
    private const int ZstandardCompressionLevel = 3;

    public static StoredArtifact ReadStored(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.SequentialScan);
        byte[] checksum = SHA256.HashData(stream);
        return new StoredArtifact(stream.Length, ToChecksum(checksum));
    }

    public WrittenArchive WriteArchive(
        string outputPath,
        GroupConfiguration group,
        IReadOnlyList<SourceEntry> entries)
    {
        string name = $"archives/{group.Id}/{group.Version}.gpka";
        string targetPath = GetOutputPath(outputPath, name);
        string targetDirectory = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(targetDirectory);
        string temporaryPath = CreateTemporaryPath(targetPath);
        var layout = new List<ArchiveEntryLayout>(entries.Count);
        long payloadSize = 0;

        try
        {
            StoredArtifact candidate = WriteCandidate(
                temporaryPath,
                group.Compression,
                destination =>
                {
                    byte[] buffer = new byte[BufferSize];

                    foreach (SourceEntry entry in entries.OrderBy(entry => entry.Path, StringComparer.Ordinal))
                    {
                        long offset = payloadSize;
                        long length = CopySource(entry.FullPath, destination, buffer);
                        payloadSize = checked(payloadSize + length);
                        layout.Add(new ArchiveEntryLayout(entry.Path, offset, length));
                    }
                });

            if (File.Exists(targetPath))
            {
                StoredArtifact existing = ReadStored(targetPath);

                if (existing != candidate)
                {
                    throw new BuildException(
                        $"그룹 버전 충돌: '{group.Id}' version {group.Version}의 기존 아카이브가 새 산출물과 다릅니다.");
                }

                return new WrittenArchive(
                    name,
                    payloadSize,
                    candidate.StoredSize,
                    candidate.Checksum,
                    IsCreated: false,
                    layout);
            }

            File.Move(temporaryPath, targetPath);
            return new WrittenArchive(
                name,
                payloadSize,
                candidate.StoredSize,
                candidate.Checksum,
                IsCreated: true,
                layout);
        }
        finally
        {
            DeleteTemporary(temporaryPath);
        }
    }

    public WrittenArtifact WriteFile(
        string outputPath,
        GroupConfiguration group,
        SourceEntry entry,
        string fileVersion)
    {
        if (!ManifestStore.TryParseEntryVersion(fileVersion, group.Version, out int requestedRevision))
        {
            throw new BuildException($"파일 '{entry.Path}'의 요청 버전이 올바르지 않습니다: {fileVersion}");
        }

        string requestedName = GetFileName(group, entry.Path, requestedRevision);
        string requestedPath = GetOutputPath(outputPath, requestedName);
        string targetDirectory = Path.GetDirectoryName(requestedPath)!;
        Directory.CreateDirectory(targetDirectory);
        string temporaryPath = CreateTemporaryPath(requestedPath);

        try
        {
            StoredArtifact candidate = WriteCandidate(
                temporaryPath,
                group.Compression,
                destination =>
                {
                    byte[] buffer = new byte[BufferSize];
                    _ = CopySource(entry.FullPath, destination, buffer);
                });
            (int Revision, string Path)? highest = FindHighestRevision(
                targetDirectory,
                entry.Path,
                group.Version,
                requestedRevision);

            if (highest is null)
            {
                File.Move(temporaryPath, requestedPath);
                return CreateWrittenFile(group, entry.Path, requestedRevision, candidate, isCreated: true);
            }

            StoredArtifact existing = ReadStored(highest.Value.Path);

            if (existing == candidate)
            {
                return CreateWrittenFile(group, entry.Path, highest.Value.Revision, candidate, isCreated: false);
            }

            if (highest.Value.Revision == int.MaxValue)
            {
                throw new BuildException($"파일 '{entry.Path}'의 리비전을 더 늘릴 수 없습니다.");
            }

            int nextRevision = highest.Value.Revision + 1;
            string nextName = GetFileName(group, entry.Path, nextRevision);
            File.Move(temporaryPath, GetOutputPath(outputPath, nextName));
            return CreateWrittenFile(group, entry.Path, nextRevision, candidate, isCreated: true);
        }
        finally
        {
            DeleteTemporary(temporaryPath);
        }
    }

    private static WrittenArtifact CreateWrittenFile(
        GroupConfiguration group,
        string entryPath,
        int revision,
        StoredArtifact stored,
        bool isCreated)
    {
        string version = $"{group.Version}.{revision}";
        return new WrittenArtifact(
            GetFileName(group, entryPath, revision),
            version,
            stored.StoredSize,
            stored.Checksum,
            isCreated);
    }

    private static string CreateTemporaryPath(string targetPath)
    {
        string directory = Path.GetDirectoryName(targetPath)!;
        string fileName = Path.GetFileName(targetPath);
        return Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");
    }

    private static void DeleteTemporary(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static (int Revision, string Path)? FindHighestRevision(
        string directory,
        string entryPath,
        int groupVersion,
        int requestedRevision)
    {
        string entryFileName = entryPath[(entryPath.LastIndexOf('/') + 1)..];
        string prefix = $"{entryFileName}.v{groupVersion}.";
        (int Revision, string Path)? highest = null;

        foreach (string path in Directory.EnumerateFiles(directory))
        {
            string fileName = Path.GetFileName(path);

            if (!fileName.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string revisionText = fileName[prefix.Length..];

            if (!int.TryParse(revisionText, NumberStyles.None, CultureInfo.InvariantCulture, out int revision)
                || revisionText != revision.ToString(CultureInfo.InvariantCulture)
                || revision < requestedRevision)
            {
                continue;
            }

            if (highest is null || revision > highest.Value.Revision)
            {
                highest = (revision, path);
            }
        }

        return highest;
    }

    private static string GetFileName(GroupConfiguration group, string entryPath, int revision)
    {
        return $"files/{group.Id}/{group.Version}/{entryPath}.v{group.Version}.{revision}";
    }

    private static string GetOutputPath(string outputPath, string name)
    {
        return Path.Combine(outputPath, name.Replace('/', Path.DirectorySeparatorChar));
    }

    private static long CopySource(string sourcePath, Stream destination, byte[] buffer)
    {
        using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.SequentialScan);
        long length = 0;
        int read;

        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            destination.Write(buffer, 0, read);
            length = checked(length + read);
        }

        return length;
    }

    private static StoredArtifact WriteCandidate(
        string temporaryPath,
        CompressionKind compression,
        Action<Stream> writePayload)
    {
        using var output = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.SequentialScan);
        using SHA256 sha256 = SHA256.Create();
        using var hashing = new CryptoStream(output, sha256, CryptoStreamMode.Write, leaveOpen: true);

        if (compression == CompressionKind.None)
        {
            writePayload(hashing);
        }
        else
        {
            ZstandardCompressionOptions options = CreateZstandardOptions();

            using var compressor = new ZstandardStream(hashing, in options, leaveOpen: true);
            writePayload(compressor);
        }

        hashing.FlushFinalBlock();
        byte[] checksum = sha256.Hash
            ?? throw new InvalidOperationException("SHA-256 계산이 완료되지 않았습니다.");
        return new StoredArtifact(output.Length, ToChecksum(checksum));
    }

    private static ZstandardCompressionOptions CreateZstandardOptions()
    {
        return new ZstandardCompressionOptions(ZstandardCompressionLevel)
        {
            ContentSizeFlag = false,
            ChecksumFlag = true,
            DictIDFlag = false,
            NbWorkers = 0
        };
    }

    private static string ToChecksum(byte[] checksum)
    {
        return Convert.ToHexString(checksum).ToLowerInvariant();
    }
}