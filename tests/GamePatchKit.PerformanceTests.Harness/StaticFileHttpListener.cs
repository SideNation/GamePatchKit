using System.Net;
using System.Net.Sockets;
using System.Text;

namespace GamePatchKit.PerformanceTests.Harness;

// A real loopback-socket static file server for the publish tree, mirroring how FakePublishServer resolves a
// request path (tests/GamePatchKit.IntegrationTests/FakePublishServer.cs) but over an actual socket instead of
// an in-process HttpMessageHandler, since this harness needs to run as its own measured process. Hand-rolls a
// minimal HTTP/1.1 GET responder over TcpListener rather than using HttpListener: HttpListener is backed by
// HTTP.sys on Windows, which rejects a dynamically chosen loopback prefix with "Access is denied" unless the
// process is elevated or the prefix has a machine-level URL ACL reservation - unacceptable for a harness that
// TestObservabilityMetrics launches from the ordinary, always-on test run. Streams each file directly to the
// response rather than buffering it, so the server side never contributes more than a small fixed buffer to
// this process's peak RSS.
internal sealed class StaticFileHttpListener : IDisposable
{
    private const int StreamBufferSize = 64 * 1024;

    private readonly string _publishRoot;
    private readonly TcpListener _listener;
    private Task? _acceptLoop;

    public StaticFileHttpListener(string publishRoot)
    {
        _publishRoot = publishRoot;
        _listener = new TcpListener(IPAddress.Loopback, 0);
    }

    public string Start()
    {
        _listener.Start();
        int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = AcceptLoopAsync();
        return $"http://127.0.0.1:{port}/";
    }

    public async Task StopAsync()
    {
        _listener.Stop();

        if (_acceptLoop != null)
        {
            await _acceptLoop.ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _listener.Stop();
    }

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            TcpClient client;

            try
            {
                client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            _ = HandleAsync(client);
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                string? requestLine = await ReadLineAsync(stream).ConfigureAwait(false);

                if (requestLine == null)
                {
                    return;
                }

                // This harness only ever issues simple, bodyless GETs against itself, so the request headers
                // are read and discarded rather than parsed - only the request line's path is used.
                while (!string.IsNullOrEmpty(await ReadLineAsync(stream).ConfigureAwait(false)))
                {
                }

                string[] parts = requestLine.Split(' ');
                string relativePath = parts.Length > 1 ? parts[1].TrimStart('/') : string.Empty;
                string fullPath = Path.Combine(_publishRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(fullPath))
                {
                    await WriteHeaderAsync(stream, "404 Not Found", 0).ConfigureAwait(false);
                    return;
                }

                await using var fileStream = new FileStream(
                    fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, StreamBufferSize, useAsync: true);
                await WriteHeaderAsync(stream, "200 OK", fileStream.Length).ConfigureAwait(false);
                await fileStream.CopyToAsync(stream, StreamBufferSize).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
            {
                // The client disconnected, or StopAsync was called mid-request - nothing to report.
            }
        }
    }

    private static Task WriteHeaderAsync(NetworkStream stream, string statusLine, long contentLength)
    {
        byte[] header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusLine}\r\nContent-Length: {contentLength}\r\nConnection: close\r\n\r\n");
        return stream.WriteAsync(header).AsTask();
    }

    // Reads a single CRLF- (or bare LF-) terminated line, one byte at a time. Request lines and headers here
    // are always short, so simplicity wins over a buffered reader.
    private static async Task<string?> ReadLineAsync(NetworkStream stream)
    {
        var bytes = new List<byte>();
        var buffer = new byte[1];

        while (true)
        {
            int read = await stream.ReadAsync(buffer).ConfigureAwait(false);

            if (read == 0)
            {
                return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
            }

            if (buffer[0] == '\n')
            {
                if (bytes.Count > 0 && bytes[^1] == '\r')
                {
                    bytes.RemoveAt(bytes.Count - 1);
                }

                return Encoding.ASCII.GetString(bytes.ToArray());
            }

            bytes.Add(buffer[0]);
        }
    }
}
