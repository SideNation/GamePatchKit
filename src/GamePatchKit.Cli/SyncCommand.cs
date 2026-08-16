using System.Security.Cryptography;

namespace GamePatchKit.Cli;

internal enum SyncOutcome
{
    Synced,
    AlreadyUpToDate,
    SkippedBecauseLocked
}

internal sealed record SyncSummary(
    SyncOutcome Outcome,
    int? PointerVersion,
    int? PreviousVersion,
    int DownloadedCount,
    long DownloadedBytes,
    int ReusedCount);

// 스케줄러가 주기적으로 돌리는 것과 사람이 강제로 돌리는 것이 같은 멱등 실행이다. 포인터가 가리키는 세대와
// 로컬이 다르면 없는 산출물만 받고, 마지막에 manifest.json을 원자적으로 교체한다. 어느 지점에서 멈춰도
// 로컬 manifest.json이 가리키는 세대는 그대로 완전하다.
internal sealed class SyncCommand
{
    private const string LockFileName = ".gpk-sync.lock";
    private const string ManifestErrorPrefix = "세대 매니페스트가 올바르지 않습니다.";
    private const int BufferSize = 64 * 1024;

    private readonly ISyncRemote _remote;

    public SyncCommand(ISyncRemote remote)
    {
        _remote = remote;
    }

    public async Task<SyncSummary> ExecuteAsync(string outputPath)
    {
        Directory.CreateDirectory(outputPath);
        using FileStream? lockStream = TryAcquireLock(outputPath);

        if (lockStream is null)
        {
            return new SyncSummary(SyncOutcome.SkippedBecauseLocked, null, null, 0, 0, 0);
        }

        return await SynchronizeAsync(outputPath);
    }

    internal static string GetManifestObjectPath(int releaseVersion)
    {
        return $"manifests/{releaseVersion}.json";
    }

    private async Task<SyncSummary> SynchronizeAsync(string outputPath)
    {
        int pointerVersion = await _remote.GetReleaseVersionAsync();
        PatchManifest? localManifest = ManifestStore.ReadPrevious(outputPath);

        if (localManifest is not null && localManifest.ReleaseVersion == pointerVersion)
        {
            return new SyncSummary(SyncOutcome.AlreadyUpToDate, pointerVersion, pointerVersion, 0, 0, 0);
        }

        byte[] manifestBytes = await ReadObjectBytesAsync(GetManifestObjectPath(pointerVersion));
        PatchManifest targetManifest = ManifestStore.ReadFromBytes(manifestBytes, ManifestErrorPrefix);

        if (targetManifest.ReleaseVersion != pointerVersion)
        {
            throw new BuildException(
                "세대 매니페스트의 releaseVersion이 포인터와 다릅니다. "
                + $"(포인터: {pointerVersion}, 매니페스트: {targetManifest.ReleaseVersion})");
        }

        ManifestArtifact[] artifacts = targetManifest.EnumerateArtifacts()
            .OrderBy(artifact => artifact.Name, StringComparer.Ordinal)
            .ToArray();
        ValidateRemotePaths(artifacts);

        int downloadedCount = 0;
        long downloadedBytes = 0;
        int reusedCount = 0;

        foreach (ManifestArtifact artifact in artifacts)
        {
            if (IsAlreadyStored(outputPath, artifact))
            {
                reusedCount++;
                continue;
            }

            downloadedBytes = checked(downloadedBytes + await DownloadArtifactAsync(outputPath, artifact));
            downloadedCount++;
        }

        // 모든 산출물이 자리를 잡은 뒤에만 세대를 전환한다.
        ManifestStore.WriteBytesAtomically(outputPath, manifestBytes);
        return new SyncSummary(
            SyncOutcome.Synced,
            pointerVersion,
            localManifest?.ReleaseVersion,
            downloadedCount,
            downloadedBytes,
            reusedCount);
    }

    // 체크와 획득이 한 번에 일어나고 프로세스가 죽으면 OS가 해제하므로, 진행 중 표시 파일과 달리 경합이나
    // stale 상태가 없다. 락 파일은 지우지 않는다 - 삭제 자체가 새 경합이 된다.
    private static FileStream? TryAcquireLock(string outputPath)
    {
        try
        {
            return new FileStream(
                Path.Combine(outputPath, LockFileName),
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void ValidateRemotePaths(IReadOnlyList<ManifestArtifact> artifacts)
    {
        var problems = new List<string>();

        foreach (ManifestArtifact artifact in artifacts)
        {
            if (!RelativePathValidator.IsRemoteObjectPath(artifact.Name))
            {
                problems.Add($"산출물 경로가 올바르지 않습니다: {artifact.Name}");
            }
        }

        if (problems.Count > 0)
        {
            throw new BuildException(
                $"원격 경로가 올바르지 않습니다.{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
        }
    }

    // 이름에 세대가 들어간 불변 객체라 이름이 같으면 바이트도 같다. 크기만 확인하고 다르면 다시 받는다.
    private static bool IsAlreadyStored(string outputPath, ManifestArtifact artifact)
    {
        var file = new FileInfo(GetLocalPath(outputPath, artifact.Name));
        return file.Exists && file.Length == artifact.StoredSize;
    }

    private static string GetLocalPath(string outputPath, string name)
    {
        return Path.Combine(outputPath, name.Replace('/', Path.DirectorySeparatorChar));
    }

    private async Task<byte[]> ReadObjectBytesAsync(string objectPath)
    {
        await using Stream source = await _remote.OpenObjectAsync(objectPath);
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, BufferSize);
        return buffer.ToArray();
    }

    private async Task<long> DownloadArtifactAsync(string outputPath, ManifestArtifact artifact)
    {
        string targetPath = GetLocalPath(outputPath, artifact.Name);
        string targetDirectory = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(targetDirectory);
        string temporaryPath = Path.Combine(
            targetDirectory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            StoredArtifact downloaded = await WriteObjectAsync(temporaryPath, artifact.Name);

            if (downloaded.StoredSize != artifact.StoredSize)
            {
                throw new BuildException(
                    $"받은 산출물의 크기가 다릅니다: {artifact.Name} "
                    + $"(expected: {artifact.StoredSize}, actual: {downloaded.StoredSize})");
            }

            if (downloaded.Checksum != artifact.Checksum)
            {
                throw new BuildException(
                    $"받은 산출물의 checksum이 다릅니다: {artifact.Name} "
                    + $"(expected: {artifact.Checksum}, actual: {downloaded.Checksum})");
            }

            // 최종 이름을 얻는 것은 검증을 통과한 바이트뿐이다.
            File.Move(temporaryPath, targetPath, overwrite: true);
            return downloaded.StoredSize;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task<StoredArtifact> WriteObjectAsync(string temporaryPath, string objectPath)
    {
        await using var output = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.SequentialScan | FileOptions.Asynchronous);
        using SHA256 sha256 = SHA256.Create();
        using var hashing = new CryptoStream(output, sha256, CryptoStreamMode.Write, leaveOpen: true);
        long storedSize = 0;

        await using (Stream source = await _remote.OpenObjectAsync(objectPath))
        {
            byte[] buffer = new byte[BufferSize];
            int read;

            while ((read = await source.ReadAsync(buffer)) > 0)
            {
                await hashing.WriteAsync(buffer.AsMemory(0, read));
                storedSize = checked(storedSize + read);
            }
        }

        hashing.FlushFinalBlock();
        byte[] checksum = sha256.Hash
            ?? throw new InvalidOperationException("SHA-256 계산이 완료되지 않았습니다.");
        return new StoredArtifact(storedSize, Convert.ToHexString(checksum).ToLowerInvariant());
    }
}
