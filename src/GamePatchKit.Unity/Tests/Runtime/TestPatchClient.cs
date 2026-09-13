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
        private const string PackageName = "com.sidenation.gamepatchkit";
        private const string StreamingAssetsDirectoryName = "GamePatchKitFixtures";
        private const string ManifestFileName = "manifest.json";
        private const string MetaFileExtension = ".meta";
        private const string ArchiveObjectPath = "archives/content/1.gpka";
        private const string ConfigObjectPath = "files/raw/1/config.txt.v1.0";
        private const string UnitsObjectPath = "files/content/1/units.json.v1.1";
        private const string UnitsEntryPath = "units.json";
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
            Assert.That(result.ReusedCount, Is.EqualTo(1));
            Assert.That(result.ExtractedCount, Is.EqualTo(3));
            Assert.That(result.RemovedCount, Is.EqualTo(0));
            AssertSynchronized(1);
        }

        [Test]
        public async Task SyncAsync_PreviousGeneration_RollsBack()
        {
            await _client.SyncAsync(1);

            PatchSyncResult result = await _client.SyncAsync(0);

            Assert.That(result.PreviousReleaseVersion, Is.EqualTo(1));
            Assert.That(result.DownloadedCount, Is.EqualTo(1));
            Assert.That(result.ReusedCount, Is.EqualTo(1));
            Assert.That(result.ExtractedCount, Is.EqualTo(2));
            Assert.That(result.RemovedCount, Is.EqualTo(1));
            AssertSynchronized(0);
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
        // 풀린 뒤라 트리는 두 세대가 섞인 상태이고 이전 세대 표식도 없다. 다음 동기화는 이전 세대를 가정하지 않고
        // 전체를 다시 풀어 수렴해야 한다.
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
            Assert.That(File.Exists(Path.Combine(_rootPath, ManifestFileName)), Is.False);
            Assert.That(File.Exists(Path.Combine(_client.DataPath, "content", "maps", "forest.json")), Is.True);
            Assert.That(
                File.ReadAllBytes(Path.Combine(_client.DataPath, "content", "units.json")),
                Is.EqualTo(File.ReadAllBytes(Path.Combine(FixturesPath, "source", "0", "content", "units.json"))));
            AssertNoTemporaryFiles();

            PatchSyncResult recovered = await _client.SyncAsync(0);

            Assert.That(recovered.PreviousReleaseVersion, Is.Null);
            Assert.That(recovered.DownloadedCount, Is.EqualTo(0));
            Assert.That(recovered.RemovedCount, Is.EqualTo(1));
            AssertSynchronized(0);
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
            Assert.That(File.Exists(Path.Combine(_rootPath, ToLocalPath(ArchiveObjectPath))), Is.False);
            AssertNoTemporaryFiles();
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
