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
        private const int ProgressPollIntervalMilliseconds = 100;

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
        // 실행 중에는 rootPath를 이 클라이언트가 독점한다. 받을 목록과 총량을 다운로드 전에 확정하므로,
        // 실행 중 다른 프로세스가 산출물을 만들어 넣어도 그 파일은 재사용되지 않고 다시 받는다.
        public Task<PatchSyncResult> SyncAsync(long releaseVersion, CancellationToken cancellationToken = default)
        {
            return SyncCoreAsync(releaseVersion, progress: null, cancellationToken);
        }

        // 진행 상황을 보고하는 오버로드다. Extracting 단계의 Report는 백그라운드 스레드에서 호출될 수 있으므로
        // Unity 객체를 만지려면 Progress<T>를 넘겨 메인 스레드로 받는다.
        public Task<PatchSyncResult> SyncAsync(
            long releaseVersion,
            IProgress<PatchSyncProgress> progress,
            CancellationToken cancellationToken = default)
        {
            return SyncCoreAsync(releaseVersion, progress, cancellationToken);
        }

        // 산출물을 받기 전에 받을 개수와 전송 바이트를 알려준다. 받을 것이 0개면 호출자는 고지를 생략한다.
        // 디스크를 바꾸지 않는다. 루트를 만들지 않고, 진행 표식을 쓰지 않고, 미러도 지우지 않는다.
        // 그래야 사용자가 고지를 거절했을 때 폴더가 호출 전과 같은 상태로 남는다.
        // 같은 루트에 대한 SyncAsync와 동시에 호출하지 않는다.
        public async Task<PatchSyncPlan> PlanAsync(long releaseVersion, CancellationToken cancellationToken = default)
        {
            if (releaseVersion < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(releaseVersion), "0 이상이어야 합니다.");
            }

            SynchronizationContext context = SynchronizationContext.Current
                ?? throw new InvalidOperationException("PlanAsync는 Unity 메인 스레드에서 호출해야 합니다.");
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                PatchManifest? localManifest = await Task.Run(
                    () => ManifestStore.ReadLocal(_rootPath), cancellationToken);
                PendingScan pending = await Task.Run(
                    () => ManifestStore.ScanPending(_rootPath), cancellationToken);

                // SyncAsync의 지름길과 같은 조건이다. 여기서는 미러를 지우지 않고 받을 것이 없다고만 답한다.
                if (localManifest is not null && localManifest.ReleaseVersion == releaseVersion && pending.IsEmpty)
                {
                    return new PatchSyncPlan(0, 0);
                }

                (PatchManifest targetManifest, _, _) = await ResolveTargetManifestAsync(
                    context, releaseVersion, localManifest, pending, progress: null, cancellationToken);
                List<PatchManifest> known = BuildKnownManifests(localManifest, pending);
                (ManifestArtifact[] toDownload, _, long downloadBytes, _, _) =
                    await PlanSyncAsync(targetManifest, known, cancellationToken);

                return new PatchSyncPlan(toDownload.Length, downloadBytes);
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

        // 저장 폴더의 상태를 네트워크 없이 읽는다. 조회는 부작용이 없어야 하므로 루트를 만들지 않는다.
        // baseUrl을 알기 전에도 물어볼 수 있어야 해서 static이다. 손상된 manifest.json은 복구 흐름으로 보낼
        // 정상 결과이므로 예외가 아니라 IsManifestCorrupted로 돌려주고, 로컬 I/O 오류만 던진다.
        //
        // 같은 루트에 대한 SyncAsync와 동시에 호출하지 않는다. 표식 스캔과 매니페스트 읽기는 두 번의 파일
        // 접근이라 그 사이에 세대가 바뀌면 서로 맞지 않는 스냅샷이 나온다. 예컨대 표식이 없을 때 스캔한 뒤
        // 다른 동기화가 표식을 쓰고 data를 갱신하기 시작하면, 이어진 읽기는 이전 완료 세대를 돌려주어
        // 섞이는 중인 트리를 완전한 것으로 보이게 한다. 읽는 순서를 바꿔도 반대 방향의 어긋남이 생기므로
        // 이것은 순서가 아니라 호출 계약으로 막는다.
        public static PatchLocalState ReadLocalState(string rootPath)
        {
            try
            {
                // GetFullPath도 래퍼 안에 둔다. PathTooLongException이 IOException 계열이라
                // 밖에 두면 이 메서드의 I/O 오류만 PatchClientException이라는 계약이 깨진다.
                string fullPath = Path.GetFullPath(rootPath);

                // ScanPending은 루트가 없으면 빈 결과를 돌려주고, 해석 못 하는 표식도 IsEmpty에 포함한다.
                // 표식을 읽을 수 없어도 트리가 섞여 있다는 증거이므로 SyncAsync와 같은 기준으로 센다.
                bool hasPendingGeneration = !ManifestStore.ScanPending(fullPath).IsEmpty;

                try
                {
                    PatchManifest? localManifest = ManifestStore.ReadLocal(fullPath);
                    return new PatchLocalState(
                        localManifest?.ReleaseVersion,
                        hasPendingGeneration,
                        isManifestCorrupted: false);
                }
                catch (PatchClientException)
                {
                    // ReadLocal은 파싱·검증 실패만 이 예외로 던진다. 파일이 없으면 null을 돌려준다.
                    return new PatchLocalState(null, hasPendingGeneration, isManifestCorrupted: true);
                }
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

        private async Task<PatchSyncResult> SyncCoreAsync(
            long releaseVersion,
            IProgress<PatchSyncProgress>? progress,
            CancellationToken cancellationToken)
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
                return await SynchronizeAsync(context, releaseVersion, progress, cancellationToken);
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
            IProgress<PatchSyncProgress>? progress,
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

            (PatchManifest targetManifest, string? pendingPath, byte[]? fetchedManifestBytes) =
                await ResolveTargetManifestAsync(
                    context, releaseVersion, localManifest, pending, progress, cancellationToken);

            if (fetchedManifestBytes is not null)
            {
                // 산출물보다 먼저 <세대>.json으로 저장한다. 중간에 멈춰도 다음 실행이 이것으로 이어받는다.
                // 조회만 하는 PlanAsync는 이 기록을 하지 않으므로 헬퍼가 아니라 여기서 쓴다.
                pendingPath = ManifestStore.GetPendingPath(_rootPath, releaseVersion);
                ManifestStore.WriteBytesAtomically(pendingPath, fetchedManifestBytes);
            }

            List<PatchManifest> known = BuildKnownManifests(localManifest, pending);
            (ManifestArtifact[] toDownload, int reusedCount, long downloadTotalBytes,
                int extractTotalCount, long extractTotalBytes) =
                    await PlanSyncAsync(targetManifest, known, cancellationToken);
            long downloadedBytes = 0;

            // 받을 것이 없으면 이 단계를 보고하지 않는다. 할 일이 없는 단계는 100%에 닿을 수 없어
            // 0%만 한 번 내보내게 되고, 호출자는 그것을 가짜 진행률로 그리게 된다.
            if (toDownload.Length > 0)
            {
                Report(progress, PatchPhase.Downloading, 0, toDownload.Length, 0, downloadTotalBytes);
            }

            for (int index = 0; index < toDownload.Length; index++)
            {
                ManifestArtifact artifact = toDownload[index];
                // for의 index는 반복마다 같은 변수라 클로저가 마지막 값을 보게 된다. 복사해서 넘긴다.
                long completedBytes = downloadedBytes;
                int completedCount = index;
                Action<long>? onBytesReceived = null;

                if (progress is not null)
                {
                    // 전송 인코딩에 따라 수신 바이트가 storedSize를 넘을 수 있어 비율이 1을 넘지 않도록 자른다.
                    onBytesReceived = received => Report(
                        progress,
                        PatchPhase.Downloading,
                        completedCount,
                        toDownload.Length,
                        completedBytes + Math.Min(received, artifact.StoredSize),
                        downloadTotalBytes);
                }

                downloadedBytes = checked(downloadedBytes
                    + await DownloadArtifactAsync(context, artifact, onBytesReceived, cancellationToken));
                Report(
                    progress,
                    PatchPhase.Downloading,
                    index + 1,
                    toDownload.Length,
                    downloadedBytes,
                    downloadTotalBytes);
            }

            cancellationToken.ThrowIfCancellationRequested();
            // 다운로드와 같은 이유로, 다시 풀 엔트리가 없으면 이 단계도 보고하지 않는다.
            if (extractTotalCount > 0)
            {
                Report(progress, PatchPhase.Extracting, 0, extractTotalCount, 0, extractTotalBytes);
            }

            int extractedCount = 0;
            long extractedBytes = 0;
            Action<long>? onEntryExtracted = null;

            if (progress is not null)
            {
                // Execute는 단일 스레드로 순회하므로 이 두 값에 경쟁이 없다.
                onEntryExtracted = size =>
                {
                    extractedCount++;
                    extractedBytes = checked(extractedBytes + size);
                    Report(
                        progress,
                        PatchPhase.Extracting,
                        extractedCount,
                        extractTotalCount,
                        extractedBytes,
                        extractTotalBytes);
                };
            }

            ExtractSummary extracted = await Task.Run(
                () => DataExtractor.Execute(_rootPath, targetManifest, known, cancellationToken, onEntryExtracted),
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
                toDownload.Length,
                downloadedBytes,
                reusedCount,
                extracted.ExtractedCount,
                extracted.RemovedCount);
        }

        // 목표 세대 매니페스트를 정한다. 로컬이나 남은 표식에서 재사용할 수 있으면 받지 않는다.
        // 원격에서 받은 경우에만 FetchedBytes가 채워진다. 그 바이트를 진행 표식으로 쓸지는 호출자가 정한다.
        private async Task<(PatchManifest Target, string? PendingPath, byte[]? FetchedBytes)> ResolveTargetManifestAsync(
            SynchronizationContext context,
            long releaseVersion,
            PatchManifest? localManifest,
            PendingScan pending,
            IProgress<PatchSyncProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (localManifest is not null && localManifest.ReleaseVersion == releaseVersion)
            {
                return (localManifest, null, null);
            }

            PendingManifest? requested = pending.Valid.FirstOrDefault(
                candidate => candidate.ReleaseVersion == releaseVersion);

            if (requested is not null)
            {
                // 중단된 목표 세대를 다시 요청했다. 매니페스트가 이미 로컬에 있으므로 받지 않는다.
                return (requested.Manifest, requested.Path, null);
            }

            // 매니페스트 크기는 받기 전에 알 수 없다. 이 단계의 수치는 모두 0으로 두고 호출자가
            // 퍼센트 대신 불확정 표시를 쓰게 한다.
            Report(progress, PatchPhase.FetchingManifest, 0, 0, 0, 0);
            byte[] manifestBytes = await DownloadBytesAsync(
                context,
                ManifestStore.GetManifestObjectPath(releaseVersion),
                cancellationToken);
            PatchManifest fetched = await Task.Run(
                () => ManifestStore.ReadFromBytes(manifestBytes, ManifestErrorPrefix),
                cancellationToken);

            if (fetched.ReleaseVersion != releaseVersion)
            {
                throw new PatchClientException(
                    "세대 매니페스트의 releaseVersion이 요청한 값과 다릅니다. "
                    + $"(요청: {releaseVersion}, 매니페스트: {fetched.ReleaseVersion})");
            }

            return (fetched, null, manifestBytes);
        }

        // 트리의 파일이 어느 매니페스트의 해제 결과인지 말할 수 있어야 건너뛸 수 있다. 완료된 세대가 없거나
        // 해석 못 하는 표식이 있으면 트리의 출처를 보증할 수 없으므로 아무것도 건너뛰지 않고 전부 다시 푼다.
        private static List<PatchManifest> BuildKnownManifests(PatchManifest? localManifest, PendingScan pending)
        {
            var known = new List<PatchManifest>();

            if (localManifest is not null && pending.UnreadablePaths.Count == 0)
            {
                known.Add(localManifest);

                foreach (PendingManifest candidate in pending.Valid)
                {
                    known.Add(candidate.Manifest);
                }
            }

            return known;
        }

        // 받을 산출물과 해제 대상을 한 번에 산출한다. 로컬 판정만 하므로 네트워크를 쓰지 않고 디스크도 바꾸지 않는다.
        // 다시 풀 엔트리가 없는 산출물은 받지 않는다. 미러를 지우므로 이것이 유일한 다운로드 기준이다.
        // 받을 목록을 루프 앞에서 확정해야 총량이 정확해진다. IsAlreadyStored는 로컬 stat만 하고 산출물
        // 이름은 매니페스트 검증이 유일함을 보장하므로, 루프 안에서 하던 판정을 앞으로 옮겨도 받는 대상과
        // 순서가 달라지지 않는다.
        private async Task<(ManifestArtifact[] ToDownload, int ReusedCount, long DownloadBytes,
            int ExtractCount, long ExtractBytes)> PlanSyncAsync(
            PatchManifest target,
            IReadOnlyList<PatchManifest> known,
            CancellationToken cancellationToken)
        {
            ManifestArtifact[] artifacts = target.EnumerateArtifacts()
                .OrderBy(artifact => artifact.Name, StringComparer.Ordinal)
                .ToArray();
            ValidateRemotePaths(artifacts);
            (IReadOnlyCollection<string> requiredNames, int extractCount, long extractBytes) =
                await Task.Run(
                    () => DataExtractor.CollectRequiredArtifacts(_rootPath, target, known),
                    cancellationToken);

            ManifestArtifact[] required = artifacts
                .Where(artifact => requiredNames.Contains(artifact.Name))
                .ToArray();
            ManifestArtifact[] toDownload = required
                .Where(artifact => !IsAlreadyStored(artifact))
                .ToArray();

            return (toDownload, required.Length - toDownload.Length,
                toDownload.Sum(artifact => artifact.StoredSize), extractCount, extractBytes);
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

        private static void Report(
            IProgress<PatchSyncProgress>? progress,
            PatchPhase phase,
            int completedCount,
            int totalCount,
            long completedBytes,
            long totalBytes)
        {
            progress?.Report(new PatchSyncProgress(phase, completedCount, totalCount, completedBytes, totalBytes));
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
            Action<long>? onBytesReceived,
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
                if (onBytesReceived is null)
                {
                    await completion.Task;
                }
                else
                {
                    // 받는 동안 수신 바이트를 주기적으로 읽는다. UnityWebRequest는 메인 스레드에서만 읽을 수 있고
                    // 캡처된 컨텍스트가 메인 스레드라 Task.Delay 뒤의 재개도 메인 스레드다.
                    // Task.Delay에 토큰을 넘기지 않는다. 취소는 위 Register가 Abort로 처리하며, 여기서 함께
                    // 던지면 밖으로 나가는 예외 종류가 두 경로 사이에서 흔들린다.
                    while (!completion.Task.IsCompleted)
                    {
                        await Task.WhenAny(completion.Task, Task.Delay(ProgressPollIntervalMilliseconds));

                        // downloadedBytes는 ulong이다. long으로 먼저 캐스팅하면 상한을 넘을 때 음수가 되어
                        // 진행률이 뒤로 간다. 좁히기 전에 ulong 상태로 자른다.
                        ulong received = request.downloadedBytes;
                        onBytesReceived(received > long.MaxValue ? long.MaxValue : (long)received);
                    }
                }
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
                await SendAsync(context, request, objectPath, onBytesReceived: null, cancellationToken);
                return request.downloadHandler.data;
            }
        }

        private async Task DownloadToFileAsync(
            SynchronizationContext context,
            string objectPath,
            string filePath,
            Action<long>? onBytesReceived,
            CancellationToken cancellationToken)
        {
            // DownloadHandlerFile은 받은 바이트를 메모리에 올리지 않고 파일에 바로 쓴다. 파일 핸들은 request를
            // dispose할 때 닫히므로 해시 검증은 이 메서드가 끝난 뒤에 한다.
            using (var request = new UnityWebRequest(_baseUrl + objectPath, UnityWebRequest.kHttpVerbGET))
            {
                request.downloadHandler = new DownloadHandlerFile(filePath) { removeFileOnAbort = true };
                await SendAsync(context, request, objectPath, onBytesReceived, cancellationToken);
            }
        }

        private async Task<long> DownloadArtifactAsync(
            SynchronizationContext context,
            ManifestArtifact artifact,
            Action<long>? onBytesReceived,
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
                await DownloadToFileAsync(context, artifact.Name, temporaryPath, onBytesReceived, cancellationToken);
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
