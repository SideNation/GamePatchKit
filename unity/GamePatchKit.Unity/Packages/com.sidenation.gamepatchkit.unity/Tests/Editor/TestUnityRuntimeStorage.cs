#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GamePatchKit.Runtime;
using NUnit.Framework;

namespace GamePatchKit.Unity.Tests
{
    public sealed class TestUnityRuntimeStorage
    {
        private const string PACKAGE_ID = "game-data";

        private string _runtimeRoot = null!;
        private UnityRuntimeStorage _storage = null!;

        [SetUp]
        public void SetUp()
        {
            _runtimeRoot = Path.Combine(
                Path.GetTempPath(),
                "GamePatchKit.Unity.Tests",
                Guid.NewGuid().ToString("N"));
            _storage = new UnityRuntimeStorage(_runtimeRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_runtimeRoot))
            {
                Directory.Delete(_runtimeRoot, recursive: true);
            }
        }

        [Test]
        public async Task PackageState_ReplacePublishesOnlyCompleteBytes()
        {
            Assert.That(
                await _storage.ReadPackageStateAsync(PACKAGE_ID, CancellationToken.None),
                Is.Null);

            byte[] first = Encoding.UTF8.GetBytes("{\"stateRevision\":1}");
            byte[] second = Encoding.UTF8.GetBytes("{\"stateRevision\":2}");
            await _storage.ReplacePackageStateAsync(PACKAGE_ID, first, CancellationToken.None);
            Assert.That(
                await _storage.ReadPackageStateAsync(PACKAGE_ID, CancellationToken.None),
                Is.EqualTo(first));

            await _storage.ReplacePackageStateAsync(PACKAGE_ID, second, CancellationToken.None);
            Assert.That(
                await _storage.ReadPackageStateAsync(PACKAGE_ID, CancellationToken.None),
                Is.EqualTo(second));
        }

        [Test]
        public async Task CacheWriter_IsInvisibleUntilCommitAndCanReplaceCorruption()
        {
            const string relativePath = "game-data/artifacts/file/aa/object";
            byte[] first = Encoding.UTF8.GetBytes("first");
            byte[] second = Encoding.UTF8.GetBytes("second");

            await using (IRuntimeCacheWriter writer = await _storage.CreateCacheWriterAsync(
                PACKAGE_ID,
                relativePath,
                CancellationToken.None))
            {
                await writer.Content.WriteAsync(
                    first,
                    0,
                    first.Length,
                    CancellationToken.None);
                Assert.That(
                    await _storage.OpenCachedArtifactAsync(
                        PACKAGE_ID,
                        relativePath,
                        CancellationToken.None),
                    Is.Null);
                await writer.CommitAsync(CancellationToken.None);
            }

            Assert.That(await ReadCachedBytesAsync(relativePath), Is.EqualTo(first));

            await using (IRuntimeCacheWriter writer = await _storage.CreateCacheWriterAsync(
                PACKAGE_ID,
                relativePath,
                CancellationToken.None))
            {
                await writer.Content.WriteAsync(
                    second,
                    0,
                    second.Length,
                    CancellationToken.None);
                await writer.CommitAsync(CancellationToken.None);
            }

            Assert.That(await ReadCachedBytesAsync(relativePath), Is.EqualTo(second));
        }

        [Test]
        public async Task StagingArea_PromotionMakesWholeInstallationVisible()
        {
            await using IRuntimeStagingArea staging = await _storage.CreateStagingAreaAsync(
                PACKAGE_ID,
                "core",
                CancellationToken.None);
            await using (Stream file = await staging.CreateFileAsync(
                "config/settings.json",
                CancellationToken.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes("settings");
                await file.WriteAsync(bytes, 0, bytes.Length, CancellationToken.None);
            }

            string installationKey = await staging.PromoteAsync(CancellationToken.None);
            Assert.That(
                await _storage.InstallationExistsAsync(
                    installationKey,
                    CancellationToken.None),
                Is.True);
            Assert.That(
                await _storage.GetInstallationFilePathsAsync(
                    installationKey,
                    CancellationToken.None),
                Is.EqualTo(new[] { "config/settings.json" }));

            await using Stream? installed = await _storage.OpenInstallationFileAsync(
                installationKey,
                "config/settings.json",
                CancellationToken.None);
            Assert.That(installed, Is.Not.Null);
            Assert.That(await ReadAllBytesAsync(installed!), Is.EqualTo(Encoding.UTF8.GetBytes("settings")));
        }

        [Test]
        public async Task PackageWriterLock_IsExclusiveUntilDisposed()
        {
            IAsyncDisposable first = await _storage.AcquirePackageWriterLockAsync(
                PACKAGE_ID,
                CancellationToken.None);

            try
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                try
                {
                    await _storage.AcquirePackageWriterLockAsync(
                        PACKAGE_ID,
                        cancellation.Token);
                    Assert.Fail("The second writer lock acquisition must be canceled.");
                }
                catch (OperationCanceledException)
                {
                }
            }
            finally
            {
                await first.DisposeAsync();
            }

            await using IAsyncDisposable second = await _storage.AcquirePackageWriterLockAsync(
                PACKAGE_ID,
                CancellationToken.None);
        }

        [Test]
        public void CacheWriter_RejectsPathOutsidePackageRoot()
        {
            Assert.ThrowsAsync<IOException>(
                async () => await _storage.CreateCacheWriterAsync(
                    PACKAGE_ID,
                    "../escape.bin",
                    CancellationToken.None));
        }

        [Test]
        public void Installation_RejectsMalformedOpaqueKey()
        {
            Assert.ThrowsAsync<ArgumentException>(
                async () => await _storage.InstallationExistsAsync(
                    "game-data//tmp",
                    CancellationToken.None));
        }

        private async Task<byte[]> ReadCachedBytesAsync(string relativePath)
        {
            await using Stream? stream = await _storage.OpenCachedArtifactAsync(
                PACKAGE_ID,
                relativePath,
                CancellationToken.None);
            Assert.That(stream, Is.Not.Null);
            return await ReadAllBytesAsync(stream!);
        }

        private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            return buffer.ToArray();
        }
    }
}
