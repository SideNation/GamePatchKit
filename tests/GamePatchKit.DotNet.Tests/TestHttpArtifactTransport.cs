using System.Net;
using System.Text;
using GamePatchKit.Runtime;

namespace GamePatchKit.DotNet.Tests;

// HttpArtifactTransport's whole job is turning a response into a stream or turning a failure into an
// ArtifactTransportException with the right IsTransient - Runtime, not this class, decides whether to retry.
public class TestHttpArtifactTransport
{
    private static readonly TargetManifestReference _target = new(
        "game-data",
        "v1-" + new string('0', 64),
        new string('a', 64));

    [Fact]
    public async Task OpenManifestAsync_RequestsTheManifestPathUnderTheTargetHash()
    {
        Uri? requestedUri = null;
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            requestedUri = request.RequestUri;
            return Task.FromResult(TextResponse(HttpStatusCode.OK, "manifest-bytes"));
        });
        var transport = new HttpArtifactTransport(CreateClient(handler));

        await using Stream stream = await transport.OpenManifestAsync(_target, CancellationToken.None);

        Assert.Equal(
            $"/publish/game-data/manifests/{_target.ManifestHash}/manifest.json",
            requestedUri!.AbsolutePath);
        Assert.Equal("manifest-bytes", await ReadAllTextAsync(stream));
    }

    [Fact]
    public async Task OpenManifestSignatureAsync_RequestsTheSignaturePathUnderTheTargetHash()
    {
        Uri? requestedUri = null;
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            requestedUri = request.RequestUri;
            return Task.FromResult(TextResponse(HttpStatusCode.OK, "signature-bytes"));
        });
        var transport = new HttpArtifactTransport(CreateClient(handler));

        await using Stream stream = await transport.OpenManifestSignatureAsync(_target, CancellationToken.None);

        Assert.Equal(
            $"/publish/game-data/manifests/{_target.ManifestHash}/manifest.sig",
            requestedUri!.AbsolutePath);
        Assert.Equal("signature-bytes", await ReadAllTextAsync(stream));
    }

    [Fact]
    public async Task OpenArtifactAsync_RequestsTheRelativePathVerbatim()
    {
        Uri? requestedUri = null;
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            requestedUri = request.RequestUri;
            return Task.FromResult(TextResponse(HttpStatusCode.OK, "artifact-bytes"));
        });
        var transport = new HttpArtifactTransport(CreateClient(handler));

        // relativePath already carries its own packageId prefix - the transport must not add another.
        await using Stream stream = await transport.OpenArtifactAsync(
            "game-data",
            "game-data/artifacts/files/aa11/content",
            CancellationToken.None);

        Assert.Equal("/publish/game-data/artifacts/files/aa11/content", requestedUri!.AbsolutePath);
        Assert.Equal("artifact-bytes", await ReadAllTextAsync(stream));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task OpenArtifactAsync_PermanentClientErrors_AreNotTransient(HttpStatusCode statusCode)
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)));
        var transport = new HttpArtifactTransport(CreateClient(handler));

        ArtifactTransportException exception = await Assert.ThrowsAsync<ArtifactTransportException>(
            () => transport.OpenArtifactAsync("game-data", "game-data/artifacts/files/aa11/content", CancellationToken.None));

        Assert.False(exception.IsTransient);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task OpenArtifactAsync_ServerAndRateLimitErrors_AreTransient(HttpStatusCode statusCode)
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)));
        var transport = new HttpArtifactTransport(CreateClient(handler));

        ArtifactTransportException exception = await Assert.ThrowsAsync<ArtifactTransportException>(
            () => transport.OpenArtifactAsync("game-data", "game-data/artifacts/files/aa11/content", CancellationToken.None));

        Assert.True(exception.IsTransient);
    }

    [Fact]
    public async Task OpenArtifactAsync_ConnectionFailure_IsTransient()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new HttpRequestException("connection refused"));
        var transport = new HttpArtifactTransport(CreateClient(handler));

        ArtifactTransportException exception = await Assert.ThrowsAsync<ArtifactTransportException>(
            () => transport.OpenArtifactAsync("game-data", "game-data/artifacts/files/aa11/content", CancellationToken.None));

        Assert.True(exception.IsTransient);
    }

    [Fact]
    public async Task OpenArtifactAsync_CallerCancellation_PropagatesUnwrapped()
    {
        // The fake has to observe the token itself, the same way a real handler would - HttpClient does not
        // preemptively refuse an already-cancelled token before invoking the handler pipeline.
        var handler = new FakeHttpMessageHandler((_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(TextResponse(HttpStatusCode.OK, "unused"));
        });
        var transport = new HttpArtifactTransport(CreateClient(handler));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.OpenArtifactAsync("game-data", "game-data/artifacts/files/aa11/content", cancelled.Token));
    }

    [Fact]
    public async Task OpenArtifactAsync_TimeoutShapedCancellationWithoutCallerCancellation_IsTransient()
    {
        // Simulates HttpClient.Timeout: the handler throws an OperationCanceledException even though the
        // caller's own token was never cancelled.
        var handler = new FakeHttpMessageHandler((_, _) => throw new TaskCanceledException("simulated timeout"));
        var transport = new HttpArtifactTransport(CreateClient(handler));

        ArtifactTransportException exception = await Assert.ThrowsAsync<ArtifactTransportException>(
            () => transport.OpenArtifactAsync("game-data", "game-data/artifacts/files/aa11/content", CancellationToken.None));

        Assert.True(exception.IsTransient);
    }

    [Fact]
    public async Task OpenArtifactAsync_FailureOpeningTheBodyStream_IsWrappedAsTransient()
    {
        // Between headers arriving and the body stream actually being obtainable, ReadAsStreamAsync itself can
        // fail - a separate point in GetAsync from both the initial SendAsync and later body reads.
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new FailingReadAsStreamContent() };
            return Task.FromResult(response);
        });
        var transport = new HttpArtifactTransport(CreateClient(handler));

        ArtifactTransportException exception = await Assert.ThrowsAsync<ArtifactTransportException>(
            () => transport.OpenArtifactAsync("game-data", "game-data/artifacts/files/aa11/content", CancellationToken.None));

        Assert.True(exception.IsTransient);
    }

    [Fact]
    public async Task OpenArtifactAsync_ConnectionDropsMidBody_IsWrappedAsTransientInsteadOfEscapingRaw()
    {
        // GetAsync only classifies failures up to the point headers arrive. A real connection reset while
        // Runtime is still reading a large artifact's body is exactly this shape: the response started fine,
        // then the body stream itself throws.
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new FailingAfterFirstReadStream(Encoding.UTF8.GetBytes("partial"))),
            };
            return Task.FromResult(response);
        });
        var transport = new HttpArtifactTransport(CreateClient(handler));

        await using Stream stream = await transport.OpenArtifactAsync(
            "game-data", "game-data/artifacts/files/aa11/content", CancellationToken.None);
        var buffer = new byte[16];

        // Matches PackageRuntime.DownloadObjectAsync's own read loop shape.
        int firstRead = await stream.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None);
        Assert.Equal(7, firstRead);

        ArtifactTransportException exception = await Assert.ThrowsAsync<ArtifactTransportException>(
            () => stream.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None));
        Assert.True(exception.IsTransient);
    }

    [Fact]
    public async Task OpenArtifactAsync_SynchronousReadTimeoutShapedCancellation_IsWrappedAsTransient()
    {
        // Synchronous Read has no CancellationToken parameter at all, so there is nothing to compare an
        // OperationCanceledException against - it must always be treated as a transport-level timeout.
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new ThrowingOnSyncReadStream()),
            };
            return Task.FromResult(response);
        });
        var transport = new HttpArtifactTransport(CreateClient(handler));

        await using Stream stream = await transport.OpenArtifactAsync(
            "game-data", "game-data/artifacts/files/aa11/content", CancellationToken.None);
        var buffer = new byte[16];

        ArtifactTransportException exception = Assert.Throws<ArtifactTransportException>(
            () => stream.Read(buffer, 0, buffer.Length));
        Assert.True(exception.IsTransient);
    }

    private static HttpClient CreateClient(HttpMessageHandler handler)
    {
        return new HttpClient(handler) { BaseAddress = new Uri("http://fake.local/publish/") };
    }

    private static HttpResponseMessage TextResponse(HttpStatusCode statusCode, string content)
    {
        return new HttpResponseMessage(statusCode) { Content = new StringContent(content, Encoding.UTF8) };
    }

    private static async Task<string> ReadAllTextAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    // Fails at the point HttpContent.ReadAsStreamAsync obtains its stream - distinct from both the initial
    // SendAsync (headers) and a later read on an already-obtained stream.
    private sealed class FailingReadAsStreamContent : HttpContent
    {
        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            throw new HttpRequestException("simulated failure opening the body stream");
        }

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
        {
            throw new HttpRequestException("simulated failure opening the body stream");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            throw new NotSupportedException();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    // Returns one chunk successfully, then throws HttpRequestException on every subsequent read - simulating a
    // connection that resets partway through a body transfer.
    private sealed class FailingAfterFirstReadStream : Stream
    {
        private readonly byte[] _firstChunk;
        private bool _firstReadDone;

        public FailingAfterFirstReadStream(byte[] firstChunk)
        {
            _firstChunk = firstChunk;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (!_firstReadDone)
            {
                _firstReadDone = true;
                Array.Copy(_firstChunk, 0, buffer, offset, _firstChunk.Length);
                return Task.FromResult(_firstChunk.Length);
            }

            throw new HttpRequestException("simulated mid-transfer connection reset");
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    // Throws directly from the synchronous Read override, bypassing ReadAsync entirely.
    private sealed class ThrowingOnSyncReadStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new TaskCanceledException("simulated synchronous timeout");
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
