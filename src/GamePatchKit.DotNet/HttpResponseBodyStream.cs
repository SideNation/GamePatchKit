using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GamePatchKit.Runtime;

namespace GamePatchKit.DotNet;

// Ties the HttpResponseMessage's lifetime to the body stream handed to Runtime, so disposing the stream (which
// is Runtime's job per the IArtifactTransport contract) also releases the underlying connection instead of
// waiting on finalization to do it.
//
// GetAsync only classifies failures up to the point the response headers arrive - a connection dropped while
// Runtime is still reading the body is exactly as retryable, so read failures here go through the same
// ArtifactTransportException translation instead of escaping as a raw, unclassified exception that bypasses
// Runtime's retry loop entirely.
internal sealed class HttpResponseBodyStream : Stream
{
    private readonly HttpResponseMessage _response;
    private readonly Stream _body;

    public HttpResponseBodyStream(HttpResponseMessage response, Stream body)
    {
        _response = response;
        _body = body;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _body.Position;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        try
        {
            return _body.Read(buffer, offset, count);
        }
        catch (HttpRequestException exception)
        {
            throw new ArtifactTransportException("The artifact response failed while reading its body.", isTransient: true, exception);
        }
        catch (OperationCanceledException exception)
        {
            // Unlike the async overloads, synchronous Read takes no CancellationToken of its own, so there is
            // no caller token to compare against - any cancellation-shaped exception reaching a call that
            // cannot itself be cancelled must come from somewhere else (an internal timeout), not the caller.
            throw new ArtifactTransportException("The artifact response timed out while reading its body.", isTransient: true, exception);
        }
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        try
        {
            return await _body.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new ArtifactTransportException("The artifact response failed while reading its body.", isTransient: true, exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ArtifactTransportException("The artifact response timed out while reading its body.", isTransient: true, exception);
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new ArtifactTransportException("The artifact response failed while reading its body.", isTransient: true, exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ArtifactTransportException("The artifact response timed out while reading its body.", isTransient: true, exception);
        }
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _body.Dispose();
            _response.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _body.DisposeAsync().ConfigureAwait(false);
        _response.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
