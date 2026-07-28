#nullable enable

using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.TestRunner;
using UnityEngine.TestTools;

[assembly: TestRunCallback(typeof(GamePatchKit.Unity.Tests.PlayerTestRunReporter))]

namespace GamePatchKit.Unity.Tests
{
    public sealed class TestPlayerRuntimeSmoke
    {
        private const string ARTIFACT_BODY = "player-artifact";
        private const string ARTIFACT_PATH = "smoke/artifact.bin";
        private const string PACKAGE_ID = "player-smoke";
        private const string STATE_BODY = "player-state";

        private string _downloadRoot = null!;
        private string _runtimeRoot = null!;

        [SetUp]
        public void SetUp()
        {
            string testId = Guid.NewGuid().ToString("N");
            _runtimeRoot = Path.Combine(
                Application.persistentDataPath,
                "GamePatchKit.PlayerTests",
                testId);
            _downloadRoot = Path.Combine(
                Application.temporaryCachePath,
                "GamePatchKit.PlayerTests",
                testId);
        }

        [TearDown]
        public void TearDown()
        {
            DeleteDirectory(_downloadRoot);
            DeleteDirectory(_runtimeRoot);
        }

        [UnityTest]
        public IEnumerator PlayerRuntime_PersistsStateAndDownloadsArtifact()
        {
            Assert.That(Application.isEditor, Is.False);

            var storage = new UnityRuntimeStorage(_runtimeRoot);
            byte[] stateBytes = Encoding.UTF8.GetBytes(STATE_BODY);
            Task writeStateTask = storage.ReplacePackageStateAsync(
                PACKAGE_ID,
                stateBytes,
                CancellationToken.None);
            yield return WaitFor(writeStateTask);
            writeStateTask.GetAwaiter().GetResult();

            Task<byte[]?> readStateTask = storage.ReadPackageStateAsync(
                PACKAGE_ID,
                CancellationToken.None);
            yield return WaitFor(readStateTask);
            Assert.That(readStateTask.GetAwaiter().GetResult(), Is.EqualTo(stateBytes));

            using var server = new LoopbackHttpServer(ARTIFACT_BODY);
            var transport = new UnityWebRequestArtifactTransport(server.BaseUrl, _downloadRoot);
            Task<Stream> openArtifactTask = transport.OpenArtifactAsync(
                PACKAGE_ID,
                ARTIFACT_PATH,
                CancellationToken.None);
            yield return WaitFor(openArtifactTask);

            using (Stream artifact = openArtifactTask.GetAwaiter().GetResult())
            using (var reader = new StreamReader(artifact, Encoding.UTF8, false, 1024, leaveOpen: false))
            {
                Assert.That(reader.ReadToEnd(), Is.EqualTo(ARTIFACT_BODY));
            }

            Assert.That(server.RequestPath, Is.EqualTo("/" + ARTIFACT_PATH));
            Assert.That(Directory.GetFiles(_downloadRoot), Is.Empty);
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        private static IEnumerator WaitFor(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }
        }

        private sealed class LoopbackHttpServer : IDisposable
        {
            private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
            private readonly TcpListener _listener;
            private readonly string _responseBody;
            private readonly Task _serverTask;

            public string BaseUrl { get; }

            public string RequestPath { get; private set; } = string.Empty;

            public LoopbackHttpServer(string responseBody)
            {
                _responseBody = responseBody;
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
                try
                {
                    using TcpClient client = await _listener.AcceptTcpClientAsync();
                    using NetworkStream stream = client.GetStream();
                    RequestPath = await ReadRequestPathAsync(stream);
                    await WriteResponseAsync(stream, _responseBody);
                }
                catch (SocketException) when (_cancellation.IsCancellationRequested)
                {
                }
                catch (ObjectDisposedException) when (_cancellation.IsCancellationRequested)
                {
                }
            }

            private static async Task<string> ReadRequestPathAsync(NetworkStream stream)
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

                string[] parts = requestLine.Split(' ');
                return parts.Length >= 2 ? parts[1] : string.Empty;
            }

            private static async Task WriteResponseAsync(NetworkStream stream, string body)
            {
                byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
                string headers =
                    $"HTTP/1.1 200 OK\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
                byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
                await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
                await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length);
                await stream.FlushAsync();
            }
        }
    }

    [Preserve]
    public sealed class PlayerTestRunReporter : ITestRunCallback
    {
        private const string RESULT_MARKER = "GAMEPATCHKIT_UNITY_PLAYER_TEST_RESULT";

        public void RunStarted(ITest testsToRun)
        {
        }

        public void RunFinished(ITestResult testResults)
        {
            Debug.Log($"{RESULT_MARKER}:{testResults.ResultState.Status}");
        }

        public void TestStarted(ITest test)
        {
        }

        public void TestFinished(ITestResult result)
        {
        }
    }
}
