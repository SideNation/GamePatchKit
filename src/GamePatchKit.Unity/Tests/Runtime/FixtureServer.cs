#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace GamePatchKit.Unity.Tests
{
    // 픽스처 버킷 트리를 루프백 HTTP로 서빙한다. 공개 Storage처럼 GET <BaseUrl><objectPath>에 파일 바이트를 돌려준다.
    internal sealed class FixtureServer : IDisposable
    {
        private const int NotFoundStatusCode = 404;
        private const int OkStatusCode = 200;
        private const int ResponderShutdownTimeoutMilliseconds = 5000;

        private readonly HttpListener _listener;
        private readonly string _rootPath;
        private readonly ManualResetEventSlim _heldResponses = new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim _throttledResponse = new ManualResetEventSlim(false);
        private readonly List<Task> _responders = new List<Task>();
        private int _requestCount;

        public FixtureServer(string rootPath)
        {
            _rootPath = rootPath;
            BaseUrl = $"http://127.0.0.1:{FindFreePort()}/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            _ = Task.Run(AcceptLoopAsync);
        }

        public string BaseUrl { get; }

        public int RequestCount => Volatile.Read(ref _requestCount);

        // objectPath를 받아 응답 바이트를 돌려준다. null이면 404로 응답한다. 지정하지 않으면 파일을 그대로 읽는다.
        public Func<string, byte[]?>? Override { get; set; }

        // objectPath가 이 접두사로 시작하는 요청은 ReleaseHeldResponses 또는 Dispose 전까지 응답하지 않는다.
        public string? HeldObjectPathPrefix { get; set; }

        // objectPath가 이 값과 같으면 본문을 절반씩 두 번에 나눠 보내고 그 사이에서 멈춘다. 받는 도중의
        // 진행률을 관측하려면 응답이 한 번에 끝나지 않아야 한다.
        public string? ThrottledObjectPath { get; set; }

        // 요청을 받은 직후 서버 스레드에서 호출한다.
        public Action<string>? OnRequest { get; set; }

        public void ReleaseHeldResponses()
        {
            _heldResponses.Set();
        }

        public void ReleaseThrottledResponse()
        {
            _throttledResponse.Set();
        }

        public byte[] ReadObject(string objectPath)
        {
            return ReadObjectOrNull(objectPath) ?? throw new FileNotFoundException(objectPath);
        }

        public byte[]? ReadObjectOrNull(string objectPath)
        {
            string path = Path.Combine(_rootPath, objectPath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        public void Dispose()
        {
            _heldResponses.Set();
            _throttledResponse.Set();
            _listener.Close();
            Task[] responders;

            lock (_responders)
            {
                responders = _responders.ToArray();
            }

            // 진행 중이던 응답 스레드가 다음 테스트로 넘어가지 않게 기다린다.
            Task.WaitAll(responders, ResponderShutdownTimeoutMilliseconds);
            _heldResponses.Dispose();
            _throttledResponse.Dispose();
        }

        private static int FindFreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private async Task AcceptLoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;

                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (HttpListenerException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                Task responder = Task.Run(() => Respond(context));

                lock (_responders)
                {
                    _responders.Add(responder);
                }
            }
        }

        private void Respond(HttpListenerContext context)
        {
            Interlocked.Increment(ref _requestCount);

            try
            {
                string objectPath = context.Request.Url.AbsolutePath.TrimStart('/');
                OnRequest?.Invoke(objectPath);

                if (HeldObjectPathPrefix is not null && objectPath.StartsWith(HeldObjectPathPrefix, StringComparison.Ordinal))
                {
                    _heldResponses.Wait();
                }

                byte[]? body = Override is not null ? Override(objectPath) : ReadObjectOrNull(objectPath);

                if (body is null)
                {
                    context.Response.StatusCode = NotFoundStatusCode;
                    context.Response.Close();
                    return;
                }

                context.Response.StatusCode = OkStatusCode;
                context.Response.ContentType = "application/octet-stream";
                context.Response.ContentLength64 = body.Length;
                WriteBody(context, objectPath, body);
                context.Response.Close();
            }
            catch (Exception)
            {
                // 클라이언트가 먼저 연결을 끊은 경우(취소 테스트)다.
                try
                {
                    context.Response.Abort();
                }
                catch (Exception)
                {
                }
            }
        }

        // 절반을 보내고 멈춘 뒤 ReleaseThrottledResponse를 기다린다. 클라이언트는 그동안 부분 수신 상태로 남는다.
        private void WriteBody(HttpListenerContext context, string objectPath, byte[] body)
        {
            bool isThrottled = ThrottledObjectPath is not null
                && objectPath == ThrottledObjectPath
                && body.Length > 1;

            if (!isThrottled)
            {
                context.Response.OutputStream.Write(body, 0, body.Length);
                return;
            }

            int firstLength = body.Length / 2;
            context.Response.OutputStream.Write(body, 0, firstLength);
            context.Response.OutputStream.Flush();
            _throttledResponse.Wait(ResponderShutdownTimeoutMilliseconds);
            context.Response.OutputStream.Write(body, firstLength, body.Length - firstLength);
        }
    }
}
