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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GamePatchKit.Unity.Tests
{
    // 실제 gpk build 산출물(Fixtures/bucket)을 루프백 HTTP로 받아 동기화 흐름 전체를 검증한다.
    // 에디터 PlayMode와 IL2CPP Player에서 같은 테스트가 실행된다.
    public sealed class TestPatchClient : IPrebuildSetup, IPostBuildCleanup
    {
        // Progress<T>는 캡처된 컨텍스트로 비동기 post해서 보고가 도착하는 시점이 흔들린다. 순서를 검증하려면
        // 동기적으로 모아야 한다. Extracting 단계는 백그라운드 스레드에서 오므로 잠근다.
        private sealed class ProgressRecorder : IProgress<PatchSyncProgress>
        {
            private readonly List<PatchSyncProgress> _reports = new List<PatchSyncProgress>();

            public IReadOnlyList<PatchSyncProgress> Reports
            {
                get
                {
                    lock (_reports)
                    {
                        return _reports.ToArray();
                    }
                }
            }

            public void Report(PatchSyncProgress value)
            {
                lock (_reports)
                {
                    _reports.Add(value);
                }
            }
        }

        private const string PackageName = "com.sidenation.gamepatchkit";
        private const string StreamingAssetsDirectoryName = "GamePatchKitFixtures";
        private const string ManifestFileName = "manifest.json";
        private const string MetaFileExtension = ".meta";
        private const string ArchiveObjectPath = "archives/content/1.gpka";
        private const string ConfigObjectPath = "files/raw/1/config.txt.v1.0";
        private const string UnitsObjectPath = "files/content/1/units.json.v1.1";
        private const string UnitsEntryPath = "units.json";
        private const string ArchivesDirectoryName = "archives";
        private const string FilesDirectoryName = "files";
        private const string RawObjectPathPrefix = "files/raw/";
        private const int AbortTimeoutMilliseconds = 10000;

        private string _rootPath = null!;
        private FixtureServer _server = null!;
        private PatchClient _client = null!;

        private static string FixturesPath
        {
            get
            {
#if UNITY_EDITOR
                return GetPackageFixturesPath();
#else
                return Path.Combine(Application.streamingAssetsPath, StreamingAssetsDirectoryName);
#endif
            }
        }

        // Player는 패키지 폴더를 읽을 수 없으므로 빌드 전에 픽스처를 StreamingAssets로 복사한다.
        public void Setup()
        {
#if UNITY_EDITOR
            string destination = Path.Combine(Application.streamingAssetsPath, StreamingAssetsDirectoryName);

            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }

            CopyDirectory(GetPackageFixturesPath(), destination);
            UnityEditor.AssetDatabase.Refresh();
#endif
        }

        public void Cleanup()
        {
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.DeleteAsset($"Assets/StreamingAssets/{StreamingAssetsDirectoryName}");

            if (Directory.Exists(Application.streamingAssetsPath)
                && !Directory.EnumerateFileSystemEntries(Application.streamingAssetsPath).Any())
            {
                UnityEditor.AssetDatabase.DeleteAsset("Assets/StreamingAssets");
            }
#endif
        }

        [SetUp]
        public void SetUp()
        {
            _rootPath = Path.Combine(Application.temporaryCachePath, "GamePatchKitTests", Guid.NewGuid().ToString("N"));
            _server = new FixtureServer(Path.Combine(FixturesPath, "bucket"));
            _client = new PatchClient(_server.BaseUrl, _rootPath);
        }

        [TearDown]
        public void TearDown()
        {
            _server.Dispose();

            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }

        [Test]
        public void Constructor_RelativeBaseUrl_Throws()
        {
            Assert.Throws<ArgumentException>(() => new PatchClient("storage/v1/object/public/bucket/", _rootPath));
        }

        [Test]
        public async Task SyncAsync_FirstGeneration_DownloadsArtifactsAndExtractsTree()
        {
            PatchSyncResult result = await _client.SyncAsync(0);

            Assert.That(result.IsAlreadyUpToDate, Is.False);
            Assert.That(result.ReleaseVersion, Is.EqualTo(0));
            Assert.That(result.PreviousReleaseVersion, Is.Null);
            Assert.That(result.DownloadedCount, Is.EqualTo(2));
            Assert.That(result.DownloadedBytes, Is.EqualTo(ObjectSize(ArchiveObjectPath) + ObjectSize(ConfigObjectPath)));
            Assert.That(result.ReusedCount, Is.EqualTo(0));
            Assert.That(result.ExtractedCount, Is.EqualTo(3));
            Assert.That(result.RemovedCount, Is.EqualTo(0));
            AssertSynchronized(0);
            AssertMirrorDeleted();
        }

        [Test]
        public async Task SyncAsync_SameGeneration_IsAlreadyUpToDateWithoutRequests()
        {
            await _client.SyncAsync(0);
            int requestCount = _server.RequestCount;

            PatchSyncResult result = await _client.SyncAsync(0);

            Assert.That(result.IsAlreadyUpToDate, Is.True);
            Assert.That(result.ReleaseVersion, Is.EqualTo(0));
            Assert.That(result.PreviousReleaseVersion, Is.EqualTo(0));
            Assert.That(_server.RequestCount, Is.EqualTo(requestCount));
            AssertSynchronized(0);
        }

        [Test]
        public async Task SyncAsync_NextGeneration_DownloadsOnlyNewArtifacts()
        {
            await _client.SyncAsync(0);

            PatchSyncResult result = await _client.SyncAsync(1);

            Assert.That(result.PreviousReleaseVersion, Is.EqualTo(0));
            Assert.That(result.DownloadedCount, Is.EqualTo(3));
            Assert.That(result.ReusedCount, Is.EqualTo(0));
            Assert.That(result.ExtractedCount, Is.EqualTo(3));
            Assert.That(result.RemovedCount, Is.EqualTo(0));
            AssertSynchronized(1);
            AssertMirrorDeleted();
        }

        [Test]
        public async Task SyncAsync_PreviousGeneration_RollsBack()
        {
            await _client.SyncAsync(1);

            PatchSyncResult result = await _client.SyncAsync(0);

            Assert.That(result.PreviousReleaseVersion, Is.EqualTo(1));
            // units.json이 아카이브 구간으로 되돌아가므로 아카이브를 다시 받는다. 미러를 지웠기 때문이다.
            Assert.That(result.DownloadedCount, Is.EqualTo(2));
            Assert.That(result.ReusedCount, Is.EqualTo(0));
            Assert.That(result.ExtractedCount, Is.EqualTo(2));
            Assert.That(result.RemovedCount, Is.EqualTo(1));
            AssertSynchronized(0);
            AssertMirrorDeleted();
        }

        [Test]
        public async Task SyncAsync_CorruptedArtifact_FailsWithoutChangingLocalGeneration()
        {
            byte[] corrupted = _server.ReadObject(ConfigObjectPath);
            corrupted[0] ^= 0xFF;
            _server.Override = objectPath => objectPath == ConfigObjectPath ? corrupted : _server.ReadObjectOrNull(objectPath);

            PatchClientException exception = await AssertThrowsAsync<PatchClientException>(() => _client.SyncAsync(0));

            Assert.That(exception.Message, Does.Contain("checksum"));
            Assert.That(File.Exists(Path.Combine(_rootPath, ManifestFileName)), Is.False);
            Assert.That(File.Exists(GetPendingPath(0)), Is.True);
            Assert.That(File.Exists(Path.Combine(_rootPath, ToLocalPath(ConfigObjectPath))), Is.False);
            AssertNoTemporaryFiles();
        }

        [Test]
        public async Task SyncAsync_MissingObject_Fails()
        {
            _server.Override = objectPath => objectPath == ArchiveObjectPath ? null : _server.ReadObjectOrNull(objectPath);

            PatchClientException exception = await AssertThrowsAsync<PatchClientException>(() => _client.SyncAsync(0));

            Assert.That(exception.Message, Does.Contain("게시된 객체가 없습니다"));
            Assert.That(File.Exists(Path.Combine(_rootPath, ManifestFileName)), Is.False);
            AssertNoTemporaryFiles();
        }

        [Test]
        public async Task SyncAsync_ManifestReleaseVersionMismatch_Fails()
        {
            _server.Override = objectPath => objectPath == "manifests/2.json"
                ? _server.ReadObject("manifests/1.json")
                : _server.ReadObjectOrNull(objectPath);

            PatchClientException exception = await AssertThrowsAsync<PatchClientException>(() => _client.SyncAsync(2));

            Assert.That(exception.Message, Does.Contain("releaseVersion"));
            Assert.That(File.Exists(Path.Combine(_rootPath, ManifestFileName)), Is.False);
        }

        [Test]
        public async Task SyncAsync_InvalidManifest_Fails()
        {
            _server.Override = objectPath => objectPath == "manifests/0.json"
                ? Encoding.UTF8.GetBytes("{}")
                : _server.ReadObjectOrNull(objectPath);

            PatchClientException exception = await AssertThrowsAsync<PatchClientException>(() => _client.SyncAsync(0));

            Assert.That(exception.Message, Does.StartWith("세대 매니페스트가 올바르지 않습니다."));
        }

        [Test]
        public async Task SyncAsync_CorruptedArtifactAfterPreviousGeneration_KeepsPreviousGeneration()
        {
            await _client.SyncAsync(0);
            byte[] corrupted = _server.ReadObject(UnitsObjectPath);
            corrupted[0] ^= 0xFF;
            _server.Override = objectPath => objectPath == UnitsObjectPath ? corrupted : _server.ReadObjectOrNull(objectPath);

            PatchClientException exception = await AssertThrowsAsync<PatchClientException>(() => _client.SyncAsync(1));

            Assert.That(exception.Message, Does.Contain("checksum"));
            AssertSynchronized(0);
        }

        // checksum은 맞지만 zstd 프레임이 아닌 산출물은 해제 단계에서 실패한다. 앞선 엔트리(forest)는 이미 세대 1로
        // 풀린 뒤라 트리는 두 세대가 섞인 상태다. manifest.json은 세대 0 그대로 남고 목표는 1.json으로 남으므로,
        // 이전 세대로 되돌리는 동기화는 두 매니페스트가 모두 보증하는 엔트리만 건너뛰고 나머지를 다시 푼다.
        [Test]
        public async Task SyncAsync_UndecodableArtifact_FailsAndNextSyncConverges()
        {
            await _client.SyncAsync(0);
            byte[] garbage = Encoding.ASCII.GetBytes(new string('A', 69));
            byte[] manifest = ReplaceStoredObject(_server.ReadObject("manifests/1.json"), UnitsEntryPath, garbage);
            _server.Override = objectPath =>
            {
                switch (objectPath)
                {
                    case "manifests/1.json":
                        return manifest;
                    case UnitsObjectPath:
                        return garbage;
                    default:
                        return _server.ReadObjectOrNull(objectPath);
                }
            };

            PatchClientException exception = await AssertThrowsAsync<PatchClientException>(() => _client.SyncAsync(1));

            Assert.That(exception.Message, Does.Contain("해제하지 못했습니다"));
            Assert.That(exception.InnerException, Is.Not.Null);
            Assert.That(File.Exists(GetPendingPath(1)), Is.True);
            Assert.That(File.Exists(Path.Combine(_client.DataPath, "content", "maps", "forest.json")), Is.True);
            Assert.That(
                File.ReadAllBytes(Path.Combine(_client.DataPath, "content", "units.json")),
                Is.EqualTo(File.ReadAllBytes(Path.Combine(FixturesPath, "source", "0", "content", "units.json"))));
            AssertNoTemporaryFiles();

            PatchSyncResult recovered = await _client.SyncAsync(0);

            Assert.That(recovered.PreviousReleaseVersion, Is.EqualTo(0));
            // 세대 0의 units.json은 아카이브 구간이고 config.txt도 되돌려야 하는데 둘 다 미러에 없어 다시 받는다.
            Assert.That(recovered.DownloadedCount, Is.EqualTo(2));
            Assert.That(recovered.RemovedCount, Is.EqualTo(1));
            AssertSynchronized(0);
            AssertMirrorDeleted();
        }

        // 아카이브 응답을 열어 주지 않은 채 취소하므로, 동기화가 끝났다면 Abort가 요청을 실제로 끊은 것이다.
        [Test]
        public async Task SyncAsync_CancelledDuringArtifactDownload_AbortsRequestAndLeavesNoFiles()
        {
            _server.HeldObjectPathPrefix = "archives/";

            using (var cancellation = new CancellationTokenSource())
            {
                // 아카이브 요청이 서버에 닿은 뒤 서버 스레드에서 취소한다. DownloadHandlerFile이 임시 파일을 연 상태다.
                _server.OnRequest = objectPath =>
                {
                    if (objectPath == ArchiveObjectPath)
                    {
                        cancellation.Cancel();
                    }
                };

                try
                {
                    Task<PatchSyncResult> sync = _client.SyncAsync(0, cancellation.Token);
                    Task finished = await Task.WhenAny(sync, Task.Delay(AbortTimeoutMilliseconds));

                    Assert.That(finished, Is.SameAs(sync), "응답이 열리기 전에 취소로 끝나야 합니다.");
                    await AssertThrowsAsync<OperationCanceledException>(() => sync);
                }
                finally
                {
                    _server.ReleaseHeldResponses();
                }
            }

            Assert.That(_server.RequestCount, Is.EqualTo(2));
            Assert.That(File.Exists(Path.Combine(_rootPath, ManifestFileName)), Is.False);
            Assert.That(File.Exists(GetPendingPath(0)), Is.True);
            Assert.That(File.Exists(Path.Combine(_rootPath, ToLocalPath(ArchiveObjectPath))), Is.False);
            AssertNoTemporaryFiles();
        }

        // 완료된 세대가 없으면 <세대>.json만으로는 트리의 파일이 어디서 왔는지 보증할 수 없다. 크기만 같고 내용이
        // 다른 파일이 남아 있어도 건너뛰지 않고 다시 풀어야 한다.
        [Test]
        public async Task SyncAsync_WithoutCompletedGeneration_DoesNotTrustExistingFiles()
        {
            _server.HeldObjectPathPrefix = ArchivesDirectoryName;

            using (var cancellation = new CancellationTokenSource())
            {
                _server.OnRequest = objectPath =>
                {
                    if (objectPath == ArchiveObjectPath)
                    {
                        cancellation.Cancel();
                    }
                };

                try
                {
                    await AssertThrowsAsync<OperationCanceledException>(() => _client.SyncAsync(0, cancellation.Token));
                }
                finally
                {
                    _server.ReleaseHeldResponses();
                }
            }

            Assert.That(File.Exists(Path.Combine(_rootPath, ManifestFileName)), Is.False);
            Assert.That(File.Exists(GetPendingPath(0)), Is.True);

            // 세대 0의 raw/config.txt와 길이는 같고 내용이 다른 파일을 심는다.
            string plantedPath = Path.Combine(_client.DataPath, "raw", "config.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(plantedPath)!);
            File.WriteAllBytes(plantedPath, Enumerable.Repeat((byte)'X', _server.ReadObject(ConfigObjectPath).Length).ToArray());

            _server.HeldObjectPathPrefix = null;
            _server.OnRequest = null;
            await _client.SyncAsync(0);

            AssertSynchronized(0);
            AssertMirrorDeleted();
        }

        // 해석할 수 없는 <세대>.json도 트리가 섞여 있다는 증거다. 지워 버리면 혼합 트리를 이미 최신으로 착각한다.
        [Test]
        public async Task SyncAsync_UnreadablePendingManifest_StillRepairsMixedTree()
        {
            await _client.SyncAsync(0);
            byte[] garbage = Encoding.ASCII.GetBytes(new string('A', 69));
            byte[] manifest = ReplaceStoredObject(_server.ReadObject("manifests/1.json"), UnitsEntryPath, garbage);
            _server.Override = objectPath =>
            {
                switch (objectPath)
                {
                    case "manifests/1.json":
                        return manifest;
                    case UnitsObjectPath:
                        return garbage;
                    default:
                        return _server.ReadObjectOrNull(objectPath);
                }
            };

            await AssertThrowsAsync<PatchClientException>(() => _client.SyncAsync(1));

            Assert.That(File.Exists(Path.Combine(_client.DataPath, "content", "maps", "forest.json")), Is.True);
            File.WriteAllText(GetPendingPath(1), "{}");

            // 세대 0과 1의 config.txt는 길이가 같고 내용만 다르다. 표식을 해석할 수 없으면 기존 manifest.json도
            // 근거로 쓸 수 없다는 것을 이 파일로 고정한다. 건너뛰면 세대 1 내용이 그대로 남는다.
            File.Copy(
                Path.Combine(FixturesPath, "source", "1", "raw", "config.txt"),
                Path.Combine(_client.DataPath, "raw", "config.txt"),
                overwrite: true);
            _server.Override = null;

            PatchSyncResult recovered = await _client.SyncAsync(0);

            Assert.That(recovered.IsAlreadyUpToDate, Is.False);
            Assert.That(File.Exists(GetPendingPath(1)), Is.False);
            AssertSynchronized(0);
            AssertMirrorDeleted();
        }

        // 중단된 목표 세대를 다시 요청하면 매니페스트를 다시 받지 않고, 이미 받아 둔 산출물도 다시 받지 않는다.
        [Test]
        public async Task SyncAsync_ResumesInterruptedGeneration_WithoutDownloadingAgain()
        {
            await _client.SyncAsync(0);
            _server.HeldObjectPathPrefix = RawObjectPathPrefix;

            using (var cancellation = new CancellationTokenSource())
            {
                // 오버레이 3개 중 앞의 2개가 자리를 잡은 뒤 마지막 요청이 서버에 닿으면 취소한다.
                _server.OnRequest = objectPath =>
                {
                    if (objectPath.StartsWith(RawObjectPathPrefix, StringComparison.Ordinal))
                    {
                        cancellation.Cancel();
                    }
                };

                try
                {
                    await AssertThrowsAsync<OperationCanceledException>(() => _client.SyncAsync(1, cancellation.Token));
                }
                finally
                {
                    _server.ReleaseHeldResponses();
                }
            }

            Assert.That(File.Exists(Path.Combine(_rootPath, ManifestFileName)), Is.True);
            Assert.That(File.Exists(GetPendingPath(1)), Is.True);

            var requestedPaths = new List<string>();
            _server.OnRequest = objectPath =>
            {
                lock (requestedPaths)
                {
                    requestedPaths.Add(objectPath);
                }
            };

            PatchSyncResult result = await _client.SyncAsync(1);

            Assert.That(requestedPaths, Does.Not.Contain("manifests/1.json"));
            Assert.That(result.DownloadedCount, Is.EqualTo(1));
            Assert.That(result.ReusedCount, Is.EqualTo(2));
            Assert.That(result.ExtractedCount, Is.EqualTo(3));
            AssertSynchronized(1);
            AssertMirrorDeleted();
        }

        [Test]
        public async Task SyncAsync_WithProgress_ReportsPhasesInOrderAndReachesTotals()
        {
            var recorder = new ProgressRecorder();

            PatchSyncResult result = await _client.SyncAsync(0, recorder);

            IReadOnlyList<PatchSyncProgress> reports = recorder.Reports;
            Assert.That(reports, Is.Not.Empty);

            // 단계는 앞으로만 간다. 되돌아오면 값이 커지지 않으므로 Is.Ordered가 잡는다.
            Assert.That(reports.Select(report => (int)report.Phase).ToArray(), Is.Ordered);
            Assert.That(
                reports.Select(report => report.Phase).Distinct().ToArray(),
                Is.EqualTo(new[] { PatchPhase.FetchingManifest, PatchPhase.Downloading, PatchPhase.Extracting }));

            foreach (PatchSyncProgress report in reports)
            {
                Assert.That(report.Ratio, Is.InRange(0d, 1d));
            }

            PatchSyncProgress[] fetching = OfPhase(reports, PatchPhase.FetchingManifest);
            Assert.That(fetching.Length, Is.EqualTo(1));
            Assert.That(fetching[0].TotalBytes, Is.Zero);
            Assert.That(fetching[0].TotalCount, Is.Zero);

            PatchSyncProgress[] downloading = OfPhase(reports, PatchPhase.Downloading);
            Assert.That(downloading[0].TotalCount, Is.EqualTo(result.DownloadedCount));
            Assert.That(
                downloading[0].TotalBytes,
                Is.EqualTo(ObjectSize(ArchiveObjectPath) + ObjectSize(ConfigObjectPath)));
            Assert.That(downloading[downloading.Length - 1].CompletedCount, Is.EqualTo(result.DownloadedCount));
            Assert.That(downloading[downloading.Length - 1].CompletedBytes, Is.EqualTo(result.DownloadedBytes));
            Assert.That(
                downloading[downloading.Length - 1].CompletedBytes,
                Is.EqualTo(downloading[downloading.Length - 1].TotalBytes));
            AssertMonotonic(downloading);

            PatchSyncProgress[] extracting = OfPhase(reports, PatchPhase.Extracting);
            Assert.That(extracting[0].TotalCount, Is.EqualTo(result.ExtractedCount));
            Assert.That(extracting[extracting.Length - 1].CompletedCount, Is.EqualTo(result.ExtractedCount));
            Assert.That(
                extracting[extracting.Length - 1].CompletedBytes,
                Is.EqualTo(extracting[extracting.Length - 1].TotalBytes));
            AssertMonotonic(extracting);
        }

        // 재사용할 산출물을 총량에서 빼지 않으면 받지도 않을 2개가 분모에 남아 진행률이 끝까지 차지 않는다.
        [Test]
        public async Task SyncAsync_ResumesInterruptedGeneration_ReportsOnlyRemainingArtifact()
        {
            await _client.SyncAsync(0);
            _server.HeldObjectPathPrefix = RawObjectPathPrefix;

            using (var cancellation = new CancellationTokenSource())
            {
                _server.OnRequest = objectPath =>
                {
                    if (objectPath.StartsWith(RawObjectPathPrefix, StringComparison.Ordinal))
                    {
                        cancellation.Cancel();
                    }
                };

                try
                {
                    await AssertThrowsAsync<OperationCanceledException>(() => _client.SyncAsync(1, cancellation.Token));
                }
                finally
                {
                    _server.ReleaseHeldResponses();
                }
            }

            _server.OnRequest = null;
            var recorder = new ProgressRecorder();

            PatchSyncResult result = await _client.SyncAsync(1, recorder);

            Assert.That(result.DownloadedCount, Is.EqualTo(1));
            Assert.That(result.ReusedCount, Is.EqualTo(2));

            // 재사용분이 총량에 섞이면 TotalCount가 3이 되고 분모가 실제 수신량보다 커진다.
            PatchSyncProgress[] downloading = OfPhase(recorder.Reports, PatchPhase.Downloading);
            Assert.That(downloading[0].TotalCount, Is.EqualTo(1));
            Assert.That(downloading[0].TotalBytes, Is.EqualTo(result.DownloadedBytes));
            Assert.That(downloading[downloading.Length - 1].CompletedCount, Is.EqualTo(1));
            Assert.That(downloading[downloading.Length - 1].CompletedBytes, Is.EqualTo(result.DownloadedBytes));
        }

        // 진행률을 받을 때는 수신 바이트를 주기적으로 읽느라 대기 구조가 달라진다. 그 경로에서도 취소가
        // 같은 예외로 끝나고 임시 파일을 남기지 않아야 한다.
        [Test]
        public async Task SyncAsync_CancelledWithProgress_AbortsRequestAndLeavesNoFiles()
        {
            var recorder = new ProgressRecorder();
            _server.HeldObjectPathPrefix = "archives/";

            using (var cancellation = new CancellationTokenSource())
            {
                _server.OnRequest = objectPath =>
                {
                    if (objectPath == ArchiveObjectPath)
                    {
                        cancellation.Cancel();
                    }
                };

                try
                {
                    Task<PatchSyncResult> sync = _client.SyncAsync(0, recorder, cancellation.Token);
                    Task finished = await Task.WhenAny(sync, Task.Delay(AbortTimeoutMilliseconds));

                    Assert.That(finished, Is.SameAs(sync), "폴링 중에도 취소로 끝나야 합니다.");
                    await AssertThrowsAsync<OperationCanceledException>(() => sync);
                }
                finally
                {
                    _server.ReleaseHeldResponses();
                }
            }

            Assert.That(File.Exists(Path.Combine(_rootPath, ToLocalPath(ArchiveObjectPath))), Is.False);
            AssertNoTemporaryFiles();

            // 취소돼도 이미 나간 보고는 계약을 지킨다.
            PatchSyncProgress[] downloading = OfPhase(recorder.Reports, PatchPhase.Downloading);
            Assert.That(downloading[0].TotalCount, Is.EqualTo(2));
            AssertMonotonic(downloading);
        }

        // 원격을 한 번도 호출하지 않는 지름길이다. 0바이트를 받은 것과 받을 것이 없는 것은 다른 상태다.
        [Test]
        public async Task SyncAsync_SameGeneration_DoesNotReportProgress()
        {
            await _client.SyncAsync(0);
            var recorder = new ProgressRecorder();

            PatchSyncResult result = await _client.SyncAsync(0, recorder);

            Assert.That(result.IsAlreadyUpToDate, Is.True);
            Assert.That(recorder.Reports, Is.Empty);
        }

#if UNITY_EDITOR
        private static string GetPackageFixturesPath()
        {
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssetPath($"Packages/{PackageName}/package.json");
            return Path.Combine(package.resolvedPath, "Tests", "Runtime", "Fixtures");
        }

        private static void CopyDirectory(string sourcePath, string destinationPath)
        {
            foreach (string sourceFile in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                if (sourceFile.EndsWith(MetaFileExtension, StringComparison.Ordinal))
                {
                    continue;
                }

                string destinationFile = Path.Combine(destinationPath, Path.GetRelativePath(sourcePath, sourceFile));
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
                File.Copy(sourceFile, destinationFile);
            }
        }
#endif

        private static PatchSyncProgress[] OfPhase(IReadOnlyList<PatchSyncProgress> reports, PatchPhase phase)
        {
            PatchSyncProgress[] selected = reports.Where(report => report.Phase == phase).ToArray();
            Assert.That(selected, Is.Not.Empty, $"{phase} 단계의 보고가 있어야 합니다.");
            return selected;
        }

        private static void AssertMonotonic(PatchSyncProgress[] reports)
        {
            for (int index = 1; index < reports.Length; index++)
            {
                Assert.That(
                    reports[index].CompletedCount,
                    Is.GreaterThanOrEqualTo(reports[index - 1].CompletedCount));
                Assert.That(
                    reports[index].CompletedBytes,
                    Is.GreaterThanOrEqualTo(reports[index - 1].CompletedBytes));
            }
        }

        private static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
            where TException : Exception
        {
            try
            {
                await action();
            }
            catch (TException exception)
            {
                return exception;
            }

            Assert.Fail($"{typeof(TException).Name}이 발생해야 합니다.");
            return null!;
        }

        private static Dictionary<string, byte[]> ReadTree(string rootPath)
        {
            var tree = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            if (!Directory.Exists(rootPath))
            {
                return tree;
            }

            foreach (string filePath in Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories))
            {
                if (filePath.EndsWith(MetaFileExtension, StringComparison.Ordinal))
                {
                    continue;
                }

                tree[Path.GetRelativePath(rootPath, filePath).Replace(Path.DirectorySeparatorChar, '/')] = File.ReadAllBytes(filePath);
            }

            return tree;
        }

        private static string ToLocalPath(string objectPath)
        {
            return objectPath.Replace('/', Path.DirectorySeparatorChar);
        }

        // 매니페스트에서 entryPath 파일 엔트리의 storedSize·checksum을 storedBytes에 맞게 바꾼다.
        private static byte[] ReplaceStoredObject(byte[] manifestBytes, string entryPath, byte[] storedBytes)
        {
            JObject manifest = JObject.Parse(Encoding.UTF8.GetString(manifestBytes));
            JObject entry = manifest["groups"]!
                .SelectMany(group => group["entries"]!)
                .Cast<JObject>()
                .Single(candidate => (string?)candidate["path"] == entryPath);
            entry["storedSize"] = storedBytes.Length;

            using (SHA256 sha256 = SHA256.Create())
            {
                entry["checksum"] = string.Concat(sha256.ComputeHash(storedBytes).Select(value => value.ToString("x2")));
            }

            return Encoding.UTF8.GetBytes(manifest.ToString(Formatting.None));
        }

        private string GetPendingPath(long releaseVersion)
        {
            return Path.Combine(_rootPath, $"{releaseVersion}.json");
        }

        private void AssertMirrorDeleted()
        {
            Assert.That(Directory.Exists(Path.Combine(_rootPath, ArchivesDirectoryName)), Is.False);
            Assert.That(Directory.Exists(Path.Combine(_rootPath, FilesDirectoryName)), Is.False);
        }

        private long ObjectSize(string objectPath)
        {
            return _server.ReadObject(objectPath).Length;
        }

        private void AssertSynchronized(long releaseVersion)
        {
            string version = releaseVersion.ToString(CultureInfo.InvariantCulture);
            byte[] expectedManifest = _server.ReadObject($"manifests/{version}.json");
            Assert.That(File.ReadAllBytes(Path.Combine(_rootPath, ManifestFileName)), Is.EqualTo(expectedManifest));

            Dictionary<string, byte[]> expected = ReadTree(Path.Combine(FixturesPath, "source", version));
            Dictionary<string, byte[]> actual = ReadTree(_client.DataPath);
            Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys));

            foreach (KeyValuePair<string, byte[]> pair in expected)
            {
                Assert.That(actual[pair.Key], Is.EqualTo(pair.Value), pair.Key);
            }

            AssertNoTemporaryFiles();
        }

        private void AssertNoTemporaryFiles()
        {
            Assert.That(Directory.GetFiles(_rootPath, "*.tmp", SearchOption.AllDirectories), Is.Empty);
        }
    }
}
