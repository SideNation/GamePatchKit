using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Packager;

internal sealed class ArtifactObjectReadStream : Stream
{
    private const int StreamBufferSize = 64 * 1024;

    private readonly string _outputRoot;
    private readonly IReadOnlyList<ArtifactPayloadObject> _objects;
    private int _objectIndex;
    private FileStream? _current;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => _objects.Sum(item => item.Size);

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public ArtifactObjectReadStream(string outputRoot, IReadOnlyList<ArtifactPayloadObject> objects)
    {
        _outputRoot = outputRoot ?? throw new ArgumentNullException(nameof(outputRoot));
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            if (_current == null)
            {
                if (_objectIndex >= _objects.Count)
                {
                    return 0;
                }

                string path = PackagePath.Resolve(_outputRoot, _objects[_objectIndex].Path);
                _current = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    StreamBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
            }

            int read = await _current.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read > 0)
            {
                return read;
            }

            await _current.DisposeAsync().ConfigureAwait(false);
            _current = null;
            _objectIndex++;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _current?.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_current != null)
        {
            await _current.DisposeAsync().ConfigureAwait(false);
        }

        await base.DisposeAsync().ConfigureAwait(false);
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
