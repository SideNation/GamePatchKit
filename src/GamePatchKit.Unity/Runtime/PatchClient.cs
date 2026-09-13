#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace GamePatchKit.Unity
{
    // 게임 서버가 알려준 releaseVersion의 세대를 공개 URL에서 받아 <rootPath>/data 아래에 원본 트리로 복원한다.
    // gpk sync와 같은 배치(manifest.json, archives/, files/, data/)를 만들고, 모든 산출물을 받아 검증하고 해제한
    // 뒤에만 manifest.json을 교체하므로 어느 지점에서 중단돼도 로컬 manifest.json이 가리키는 세대는 완전하다.
    // 네트워크는 Unity 메인 스레드의 UnityWebRequest가, 해시 검증과 해제는 백그라운드 스레드가 맡는다.
    public sealed class PatchClient
    {
        private const string ManifestErrorPrefix = "세대 매니페스트가 올바르지 않습니다.";
        private const string ArchivesDirectoryName = "archives";
        private const string FilesDirectoryName = "files";
        private const long NotFoundStatusCode = 404;

        private readonly string _baseUrl;
        private readonly string _rootPath;

        // baseUrl은 게시된 객체 이름을 그대로 뒤에 붙일 URL prefix다.
        // 예: https://<project-ref>.supabase.co/storage/v1/object/public/<bucket>/
        public PatchClient(string baseUrl, string rootPath)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("baseUrl은 http 또는 https 절대 URL이어야 합니다.", nameof(baseUrl));
            }

            _baseUrl = baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : baseUrl + "/";
            _rootPath = Path.GetFullPath(rootPath);
            DataPath = Path.Combine(_rootPath, DataExtractor.DATA_DIRECTORY_NAME);
        }

        // 복원된 원본 트리의 루트. 파일 경로는 <그룹 id>/<엔트리 path>다.
        public string DataPath { get; }

        // Unity 메인 스레드에서 호출한다. 같은 폴더에 대한 SyncAsync를 동시에 실행하지 않는다.
        public async Task<PatchSyncResult> SyncAsync(long releaseVersion, CancellationToken cancellationToken = default)
        {
            if (releaseVersion < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(releaseVersion), "0 이상이어야 합니다.");
            }

            // 취소 시 진행 중인 UnityWebRequest를 메인 스레드에서 중단하기 위해 캡처한다.
            SynchronizationContext context = SynchronizationContext.Current
                ?? throw new InvalidOperationException("SyncAsync는 Unity 메인 스레드에서 호출해야 합니다.");
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await SynchronizeAsync(context, releaseVersion, cancellationToken);
            }
            catch (IOException exception)
            {
                throw new PatchClientException($"로컬 파일 작업이 실패했습니다: {exception.Message}", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new PatchClientException($"로컬 파일 작업이 실패했습니다: {exception.Message}", exception);
            }
        }

        private async Task<PatchSyncResult> SynchronizeAsync(
            SynchronizationContext context,
            long releaseVersion,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(_rootPath);
            PatchManifest? localManifest = await Task.Run(() => ManifestStore.ReadLocal(_rootPath), cancellationToken);
            PendingScan pending = await Task.Run(() => ManifestStore.ScanPending(_rootPath), cancellationToken);

            // 중단된 목표가 남아 있지 않을 때만 지름길이다. 남아 있으면 트리가 두 세대로 섞여 있을 수 있다.
            if (localManifest is not null && localManifest.ReleaseVersion == releaseVersion && pending.IsEmpty)
            {
                await Task.Run(DeleteMirror);
                return new PatchSyncResult(releaseVersion, releaseVersion, true, 0, 0, 0, 0, 0);
            }

            string? pendingPath = null;
            PatchManifest targetManifest;

            if (localManifest is not null && localManifest.ReleaseVersion == releaseVersion)
            {
                targetManifest = localManifest;
            }
            else
            {
                PendingManifest? requested = pending.Valid.FirstOrDefault(
                    candidate => candidate.ReleaseVersion == releaseVersion);

                if (requested is not null)
                {
                    // 중단된 목표 세대를 다시 요청했다. 매니페스트가 이미 로컬에 있으므로 받지 않는다.
                    targetManifest = requested.Manifest;
                    pendingPath = requested.Path;
                }
                else
                {
                    byte[] manifestBytes = await DownloadBytesAsync(
                        context,
                        ManifestStore.GetManifestObjectPath(releaseVersion),
                        cancellationToken);
                    targetManifest = await Task.Run(
                        () => ManifestStore.ReadFromBytes(manifestBytes, ManifestErrorPrefix),
                        cancellationToken);

                    if (targetManifest.ReleaseVersion != releaseVersion)
                    {
                        throw new PatchClientException(
                            "세대 매니페스트의 releaseVersion이 요청한 값과 다릅니다. "
                            + $"(요청: {releaseVersion}, 매니페스트: {targetManifest.ReleaseVersion})");
                    }

                    // 산출물보다 먼저 <세대>.json으로 저장한다. 중간에 멈춰도 다음 실행이 이것으로 이어받는다.
                    pendingPath = ManifestStore.GetPendingPath(_rootPath, releaseVersion);
                    ManifestStore.WriteBytesAtomically(pendingPath, manifestBytes);
                }
            }

            // 트리의 파일이 어느 매니페스트의 해제 결과인지 말할 수 있어야 건너뛸 수 있다. 완료된 세대가 없거나
            // 해석 못 하는 표식이 있으면 트리의 출처를 보증할 수 없으므로 아무것도 건너뛰지 않고 전부 다시 푼다.
            var known = new List<PatchManifest>();

            if (localManifest is not null && pending.UnreadablePaths.Count == 0)
            {
                known.Add(localManifest);

                foreach (PendingManifest candidate in pending.Valid)
                {
                    known.Add(candidate.Manifest);
                }
            }

            ManifestArtifact[] artifacts = targetManifest.EnumerateArtifacts()
                .OrderBy(artifact => artifact.Name, StringComparer.Ordinal)
                .ToArray();
            ValidateRemotePaths(artifacts);
            IReadOnlyCollection<string> requiredNames = await Task.Run(
                () => DataExtractor.CollectRequiredArtifacts(_rootPath, targetManifest, known),
                cancellationToken);

            int downloadedCount = 0;
            long downloadedBytes = 0;
            int reusedCount = 0;

            foreach (ManifestArtifact artifact in artifacts)
            {
                // 다시 풀 엔트리가 없는 산출물은 받지 않는다. 미러를 지우므로 이것이 유일한 다운로드 기준이다.
                if (!requiredNames.Contains(artifact.Name))
                {
                    continue;
                }

                if (IsAlreadyStored(artifact))
                {
                    reusedCount++;
                    continue;
                }

                downloadedBytes = checked(downloadedBytes + await DownloadArtifactAsync(context, artifact, cancellationToken));
                downloadedCount++;
            }

            cancellationToken.ThrowIfCancellationRequested();
            ExtractSummary extracted = await Task.Run(
                () => DataExtractor.Execute(_rootPath, targetManifest, known, cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // 해제까지 끝난 뒤에만 세대를 전환한다. 목표 매니페스트를 한 번에 manifest.json으로 올린다.
            if (pendingPath is not null)
            {
                ManifestStore.CommitPending(_rootPath, pendingPath);
            }

            await Task.Run(() => CleanUp(pending, pendingPath));
            return new PatchSyncResult(
                releaseVersion,
                localManifest?.ReleaseVersion,
                false,
                downloadedCount,
                downloadedBytes,
                reusedCount,
                extracted.ExtractedCount,
                extracted.RemovedCount);
        }

        // 세대 전환이 끝난 뒤의 정리다. 실패해도 동기화를 실패로 만들지 않는다. 남은 파일은 다음 실행이 재사용하거나 지운다.
        private void CleanUp(PendingScan pending, string? committedPath)
        {
            foreach (string path in pending.AllPaths)
            {
                if (path == committedPath)
                {
                    continue;
                }

                Delete(() => File.Delete(path));
            }

            DeleteMirror();
        }

        // 압축 미러는 해제가 끝나면 쓸 일이 없다. data 트리만 남긴다.
        private void DeleteMirror()
        {
            Delete(() => Directory.Delete(Path.Combine(_rootPath, ArchivesDirectoryName), recursive: true));
            Delete(() => Directory.Delete(Path.Combine(_rootPath, FilesDirectoryName), recursive: true));
        }

        private static void Delete(Action delete)
        {
            try
            {
                delete();
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (FileNotFoundException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void ValidateRemotePaths(ManifestArtifact[] artifacts)
        {
            string[] invalidNames = artifacts
                .Where(artifact => !RelativePathValidator.IsRemoteObjectPath(artifact.Name))
                .Select(artifact => artifact.Name)
                .ToArray();

            if (invalidNames.Length > 0)
            {
                throw new PatchClientException($"산출물 경로가 올바르지 않습니다: {string.Join(", ", invalidNames)}");
            }
        }

        private static async Task SendAsync(
            SynchronizationContext context,
            UnityWebRequest request,
            string objectPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool isFinished = false;

            try
            {
                request.SendWebRequest().completed += _ => completion.TrySetResult(true);
            }
            catch (InvalidOperationException exception)
            {
                // 프로젝트의 Allow downloads over HTTP 설정이 Not allowed면 평문 http URL은 전송 전에 거부된다.
                // loopback은 예외지만 그 밖의 http 주소는 여기서 걸린다.
                throw new PatchClientException(
                    $"원격 요청을 보낼 수 없습니다: {objectPath} ({exception.Message})",
                    exception);
            }

            // Abort는 메인 스레드에서만 호출할 수 있으므로 취소 콜백은 캡처한 컨텍스트로 넘긴다. 완료 뒤에 도착한 취소는
            // request가 이미 dispose됐을 수 있어 건드리지 않는다.
            using (cancellationToken.Register(() => context.Post(_ => AbortIfPending(request, ref isFinished), null)))
            {
                await completion.Task;
            }

            isFinished = true;
            cancellationToken.ThrowIfCancellationRequested();

            switch (request.result)
            {
                case UnityWebRequest.Result.Success:
                    return;
                case UnityWebRequest.Result.ProtocolError:
                    if (request.responseCode == NotFoundStatusCode)
                    {
                        throw new PatchClientException(
                            $"게시된 객체가 없습니다: {objectPath}. 요청한 세대가 완전히 게시되지 않았습니다.");
                    }

                    throw new PatchClientException(
                        $"객체 다운로드가 실패했습니다: {objectPath} (statusCode={request.responseCode})");
                default:
                    throw new PatchClientException(
                        $"원격 요청이 실패했습니다: {objectPath} (result={request.result}, error={request.error})");
            }
        }

        private static void AbortIfPending(UnityWebRequest request, ref bool isFinished)
        {
            if (!isFinished && !request.isDone)
            {
                request.Abort();
            }
        }

        private static (long StoredSize, string Checksum) ReadStored(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(stream);
                var checksum = new StringBuilder(hash.Length * 2);

                foreach (byte value in hash)
                {
                    checksum.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                }

                return (stream.Length, checksum.ToString());
            }
        }

        // 이름에 세대가 들어간 불변 객체라 이름이 같으면 바이트도 같다. 크기만 확인하고 다르면 다시 받는다.
        private bool IsAlreadyStored(ManifestArtifact artifact)
        {
            var file = new FileInfo(GetLocalPath(artifact.Name));
            return file.Exists && file.Length == artifact.StoredSize;
        }

        private string GetLocalPath(string name)
        {
            return Path.Combine(_rootPath, name.Replace('/', Path.DirectorySeparatorChar));
        }

        private async Task<byte[]> DownloadBytesAsync(
            SynchronizationContext context,
            string objectPath,
            CancellationToken cancellationToken)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(_baseUrl + objectPath))
            {
                await SendAsync(context, request, objectPath, cancellationToken);
                return request.downloadHandler.data;
            }
        }

        private async Task DownloadToFileAsync(
            SynchronizationContext context,
            string objectPath,
            string filePath,
            CancellationToken cancellationToken)
        {
            // DownloadHandlerFile은 받은 바이트를 메모리에 올리지 않고 파일에 바로 쓴다. 파일 핸들은 request를
            // dispose할 때 닫히므로 해시 검증은 이 메서드가 끝난 뒤에 한다.
            using (var request = new UnityWebRequest(_baseUrl + objectPath, UnityWebRequest.kHttpVerbGET))
            {
                request.downloadHandler = new DownloadHandlerFile(filePath) { removeFileOnAbort = true };
                await SendAsync(context, request, objectPath, cancellationToken);
            }
        }

        private async Task<long> DownloadArtifactAsync(
            SynchronizationContext context,
            ManifestArtifact artifact,
            CancellationToken cancellationToken)
        {
            string targetPath = GetLocalPath(artifact.Name);
            string targetDirectory = Path.GetDirectoryName(targetPath)!;
            Directory.CreateDirectory(targetDirectory);
            string temporaryPath = Path.Combine(
                targetDirectory,
                $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                await DownloadToFileAsync(context, artifact.Name, temporaryPath, cancellationToken);
                (long storedSize, string checksum) = await Task.Run(() => ReadStored(temporaryPath), cancellationToken);

                if (storedSize != artifact.StoredSize)
                {
                    throw new PatchClientException(
                        $"받은 산출물의 크기가 다릅니다: {artifact.Name} "
                        + $"(expected: {artifact.StoredSize}, actual: {storedSize})");
                }

                if (checksum != artifact.Checksum)
                {
                    throw new PatchClientException(
                        $"받은 산출물의 checksum이 다릅니다: {artifact.Name} "
                        + $"(expected: {artifact.Checksum}, actual: {checksum})");
                }

                // 최종 이름을 얻는 것은 검증을 통과한 바이트뿐이다.
                FileMover.MoveReplacing(temporaryPath, targetPath);
                return storedSize;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }
}
