#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GamePatchKit.Core;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Core.Paths;
using GamePatchKit.Runtime;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GamePatchKit.Unity.Tests
{
    public sealed class TestUnityWebRequestArtifactTransport
    {
        private const string DATA_VERSION = "v1-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string MANIFEST_HASH = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private const string PACKAGE_ID = "game-data";

        private string _downloadRoot = null!;

        [SetUp]
        public void SetUp()
        {
            _downloadRoot = Path.Combine(
                Path.GetTempPath(),
                "GamePatchKit.Unity.Transport.Tests",
                Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_downloadRoot))
            {
                Directory.Delete(_downloadRoot, recursive: true);
            }
        }

        [UnityTest]
        public IEnumerator Manifest_DownloadsExpectedPathAndDeletesTemporaryFileOnDispose()
        {
            using var server = new LoopbackHttpServer();
            var transport = new UnityWebRequestArtifactTransport(server.BaseUrl, _downloadRoot);
            var target = new TargetManifestReference(PACKAGE_ID, DATA_VERSION, MANIFEST_HASH);

            Task<Stream> openTask = transport.OpenManifestAsync(target, CancellationToken.None);
            yield return WaitFor(openTask);

            Stream stream = openTask.GetAwaiter().GetResult();
            string body;
            using (var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true))
            {
                body = reader.ReadToEnd();
            }

            Assert.That(body, Is.EqualTo("payload"));
            Assert.That(
                server.LastRequestPath,
                Is.EqualTo($"/{PACKAGE_ID}/manifests/{MANIFEST_HASH}/manifest.json"));
            Assert.That(Directory.GetFiles(_downloadRoot), Has.Length.EqualTo(1));

            stream.Dispose();
            Assert.That(Directory.GetFiles(_downloadRoot), Is.Empty);
        }

        [UnityTest]
        public IEnumerator Artifact_Http404IsConfirmedNotFound()
        {
            using var server = new LoopbackHttpServer();
            var transport = new UnityWebRequestArtifactTransport(server.BaseUrl, _downloadRoot);

            Task<Stream> openTask = transport.OpenArtifactAsync(
                PACKAGE_ID,
                "missing",
                CancellationToken.None);
            yield return WaitFor(openTask);

            ArtifactTransportException exception = Assert.Throws<ArtifactTransportException>(
                () => openTask.GetAwaiter().GetResult())!;
            Assert.That(exception.IsTransient, Is.False);
            Assert.That(exception.IsNotFound, Is.True);
        }

        [UnityTest]
        public IEnumerator Artifact_Http503IsTransient()
        {
            using var server = new LoopbackHttpServer();
            var transport = new UnityWebRequestArtifactTransport(server.BaseUrl, _downloadRoot);

            Task<Stream> openTask = transport.OpenArtifactAsync(
                PACKAGE_ID,
                "retry",
                CancellationToken.None);
            yield return WaitFor(openTask);

            ArtifactTransportException exception = Assert.Throws<ArtifactTransportException>(
                () => openTask.GetAwaiter().GetResult())!;
            Assert.That(exception.IsTransient, Is.True);
            Assert.That(exception.IsNotFound, Is.False);
        }

        [UnityTest]
        public IEnumerator Artifact_CallerCancellationAbortsRequest()
        {
            using var server = new LoopbackHttpServer();
            var transport = new UnityWebRequestArtifactTransport(server.BaseUrl, _downloadRoot);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            Task<Stream> openTask = transport.OpenArtifactAsync(
                PACKAGE_ID,
                "slow",
                cancellation.Token);
            yield return WaitFor(openTask);

            try
            {
                openTask.GetAwaiter().GetResult();
                Assert.Fail("The canceled request must not return a stream.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        [Test]
        public void Constructor_RequiresTrailingSlash()
        {
            Assert.Throws<ArgumentException>(
                () => new UnityWebRequestArtifactTransport("https://example.com", _downloadRoot));
        }

        [UnityTest]
        public IEnumerator PackageRuntime_InstallsRequiredFileThroughUnityAdapters()
        {
            byte[] fileBytes = Encoding.UTF8.GetBytes("unity-runtime-data");
            FinalizedManifest release = CreateRelease(fileBytes, out string artifactPath);
            var responses = new Dictionary<string, byte[]>
            {
                [$"/{PACKAGE_ID}/manifests/{release.ManifestHash}/manifest.json"] =
                    release.GetCanonicalBytes(),
                ["/" + artifactPath] = fileBytes,
            };
            using var server = new LoopbackHttpServer(responses);
            string runtimeRoot = Path.Combine(
                Path.GetTempPath(),
                "GamePatchKit.Unity.Runtime.Tests",
                Guid.NewGuid().ToString("N"));

            try
            {
                var storage = new UnityRuntimeStorage(runtimeRoot);
                var transport = new UnityWebRequestArtifactTransport(server.BaseUrl, _downloadRoot);
                var runtime = new PackageRuntime(transport, storage);
                var target = new TargetManifestReference(
                    PACKAGE_ID,
                    release.DataVersion,
                    release.ManifestHash);

                Task<PackageState> installTask = runtime.InstallOrUpdateAsync(target);
                yield return WaitFor(installTask);

                PackageState state = installTask.GetAwaiter().GetResult();
                PackageGroupState group = state.Groups.Single();
                Assert.That(group.Status, Is.EqualTo(PackageGroupStatus.Ready));
                Assert.That(group.InstallationKey, Is.Not.Null);

                Task<Stream?> openTask = storage.OpenInstallationFileAsync(
                    group.InstallationKey!,
                    "data/runtime.bin",
                    CancellationToken.None);
                yield return WaitFor(openTask);

                using Stream? installed = openTask.GetAwaiter().GetResult();
                Assert.That(installed, Is.Not.Null);
                using var buffer = new MemoryStream();
                installed!.CopyTo(buffer);
                Assert.That(buffer.ToArray(), Is.EqualTo(fileBytes));
            }
            finally
            {
                if (Directory.Exists(runtimeRoot))
                {
                    Directory.Delete(runtimeRoot, recursive: true);
                }
            }
        }

        private static IEnumerator WaitFor(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }
        }

        private static FinalizedManifest CreateRelease(
            byte[] fileBytes,
            out string artifactPath)
        {
            string fileHash = Sha256Hash.ComputeHex(fileBytes);
            artifactPath = ContentAddressedPath.FileSinglePayloadPath(
                PACKAGE_ID,
                fileHash,
                CompressionKind.None);
            var artifact = new ManifestArtifact.FileArtifact(
                CompressionKind.None,
                new FilePayload.Single(
                    artifactPath,
                    fileBytes.LongLength,
                    fileHash));
            var file = new ManifestFileEntry(
                "data/runtime.bin",
                "core",
                fileBytes.LongLength,
                fileHash,
                new FileSource.FileReference(fileHash));
            var draft = new ReleaseManifest(
                schemaVersion: 1,
                PACKAGE_ID,
                "v1-" + new string('0', 64),
                compactVersion: 0,
                new[] { new ManifestGroupEntry("core", required: true) },
                new ManifestArtifact[] { artifact },
                new[] { file });

            Assert.That(ManifestValidator.Validate(draft).IsValid, Is.True);
            return ReleaseIdentity.Finalize(draft, compactVersion: 0);
        }

        private sealed class LoopbackHttpServer : IDisposable
        {
            private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
            private readonly TcpListener _listener;
            private readonly IReadOnlyDictionary<string, byte[]> _responses;
            private readonly Task _serverTask;

            public string BaseUrl { get; }

            public string? LastRequestPath { get; private set; }

            public LoopbackHttpServer(IReadOnlyDictionary<string, byte[]>? responses = null)
            {
                _responses = responses ?? new Dictionary<string, byte[]>();
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                BaseUrl = $"http://127.0.0.1:{port}/";
                _serverTask = Task.Run(ServeAsync);
            }

            public void Dispose()
            {
                _cancellation.Cancel();
                _listener.Stop();

                try
                {
                    _serverTask.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                }

                _cancellation.Dispose();
            }

            private async Task ServeAsync()
            {
                while (!_cancellation.IsCancellationRequested)
                {
                    TcpClient client;

                    try
                    {
                        client = await _listener.AcceptTcpClientAsync();
                    }
                    catch (SocketException) when (_cancellation.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (ObjectDisposedException) when (_cancellation.IsCancellationRequested)
                    {
                        return;
                    }

                    _ = HandleAsync(client);
                }
            }

            private async Task HandleAsync(TcpClient client)
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    string requestLine = await ReadRequestAsync(stream);
                    string[] requestParts = requestLine.Split(' ');
                    LastRequestPath = requestParts.Length >= 2 ? requestParts[1] : string.Empty;

                    if (LastRequestPath == "/slow")
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(30), _cancellation.Token);
                        }
                        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
                        {
                        }

                        return;
                    }

                    if (LastRequestPath == "/missing")
                    {
                        await WriteResponseAsync(stream, 404, "Not Found", string.Empty);
                        return;
                    }

                    if (LastRequestPath == "/retry")
                    {
                        await WriteResponseAsync(stream, 503, "Service Unavailable", string.Empty);
                        return;
                    }

                    if (_responses.TryGetValue(LastRequestPath, out byte[] response))
                    {
                        await WriteResponseAsync(stream, 200, "OK", response);
                        return;
                    }

                    await WriteResponseAsync(stream, 200, "OK", "payload");
                }
            }

            private static async Task<string> ReadRequestAsync(NetworkStream stream)
            {
                using var reader = new StreamReader(
                    stream,
                    Encoding.ASCII,
                    false,
                    1024,
                    leaveOpen: true);
                string requestLine = await reader.ReadLineAsync() ?? string.Empty;

                while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
                {
                }

                return requestLine;
            }

            private static async Task WriteResponseAsync(
                NetworkStream stream,
                int statusCode,
                string reason,
                string body)
            {
                await WriteResponseAsync(
                    stream,
                    statusCode,
                    reason,
                    Encoding.UTF8.GetBytes(body));
            }

            private static async Task WriteResponseAsync(
                NetworkStream stream,
                int statusCode,
                string reason,
                byte[] bodyBytes)
            {
                string headers =
                    $"HTTP/1.1 {statusCode} {reason}\r\n"
                    + $"Content-Length: {bodyBytes.Length}\r\n"
                    + "Connection: close\r\n"
                    + "\r\n";
                byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
                await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
                await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length);
                await stream.FlushAsync();
            }
        }
    }
}
