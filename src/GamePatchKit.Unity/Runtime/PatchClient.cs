#nullable enable
using System;
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

            if (localManifest is not null && localManifest.ReleaseVersion == releaseVersion)
            {
                return new PatchSyncResult(releaseVersion, releaseVersion, true, 0, 0, 0, 0, 0);
            }

            byte[] manifestBytes = await DownloadBytesAsync(
                context,
                ManifestStore.GetManifestObjectPath(releaseVersion),
                cancellationToken);
            PatchManifest targetManifest = await Task.Run(
                () => ManifestStore.ReadFromBytes(manifestBytes, ManifestErrorPrefix),
                cancellationToken);

            if (targetManifest.ReleaseVersion != releaseVersion)
            {
                throw new PatchClientException(
                    "세대 매니페스트의 releaseVersion이 요청한 값과 다릅니다. "
                    + $"(요청: {releaseVersion}, 매니페스트: {targetManifest.ReleaseVersion})");
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
                if (IsAlreadyStored(artifact))
                {
                    reusedCount++;
                    continue;
                }

                downloadedBytes = checked(downloadedBytes + await DownloadArtifactAsync(context, artifact, cancellationToken));
                downloadedCount++;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 여기서부터 data 트리를 바꾼다. 이전 세대 표식을 먼저 지워, 중간에 멈춘 트리를 다음 동기화가 이전 세대로
            // 오인하지 않게 한다. 산출물은 그대로 남으므로 다시 받지 않고 전체를 다시 푼다.
            ManifestStore.DeleteLocal(_rootPath);
            ExtractSummary extracted = await Task.Run(
                () => DataExtractor.Execute(_rootPath, targetManifest, localManifest, cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // 모든 산출물이 자리를 잡고 해제까지 끝난 뒤에만 세대를 전환한다.
            ManifestStore.WriteBytesAtomically(_rootPath, manifestBytes);
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
