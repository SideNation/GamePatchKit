using System.Net;

namespace GamePatchKit.IntegrationTests;

// Serves a real publish tree on disk over HttpMessageHandler, so HttpArtifactTransport is exercised exactly as
// it would be against a real CDN mirroring FilePackageBuilder's output - plus the injection points (open
// counts, delay, one-shot failure, byte corruption) the adapter-level scenarios need.
internal sealed class FakePublishServer : HttpMessageHandler
{
    private readonly string _publishRoot;
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _openCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _remainingFailures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<byte[], byte[]>> _corruptions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource> _holdGates = new(StringComparer.Ordinal);
    private readonly List<string> _requestOrder = new();

    public FakePublishServer(string publishRoot)
    {
        _publishRoot = publishRoot;
    }

    public int GetOpenCount(string relativePath)
    {
        lock (_gate)
        {
            return _openCounts.TryGetValue(relativePath, out int count) ? count : 0;
        }
    }

    // Artifacts are planned in content-hash order, not file-path order, so scenarios that care which of two
    // artifacts is requested first must discover it here rather than assume it from source file names.
    public IReadOnlyList<string> RequestOrder
    {
        get
        {
            lock (_gate)
            {
                return _requestOrder.ToArray();
            }
        }
    }

    public void FailNextRequests(string relativePath, int times)
    {
        lock (_gate)
        {
            _remainingFailures[relativePath] = times;
        }
    }

    public void Corrupt(string relativePath, Func<byte[], byte[]> transform)
    {
        lock (_gate)
        {
            _corruptions[relativePath] = transform;
        }
    }

    // Blocks the response for relativePath until the returned handle is disposed, so a test can deterministically
    // control when an in-flight request resolves instead of racing a fixed delay against a cancellation window.
    public IDisposable HoldUntilReleased(string relativePath)
    {
        var holdGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_gate)
        {
            _holdGates[relativePath] = holdGate;
        }

        return new ReleaseOnDispose(holdGate);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string relativePath = request.RequestUri!.AbsolutePath.TrimStart('/');
        Task? holdTask;

        lock (_gate)
        {
            _openCounts[relativePath] = _openCounts.TryGetValue(relativePath, out int count) ? count + 1 : 1;
            _requestOrder.Add(relativePath);
            holdTask = _holdGates.TryGetValue(relativePath, out TaskCompletionSource? holdGate) ? holdGate.Task : null;
        }

        if (holdTask != null)
        {
            await holdTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        bool shouldFail;
        lock (_gate)
        {
            shouldFail = _remainingFailures.TryGetValue(relativePath, out int remaining) && remaining > 0;
            if (shouldFail)
            {
                _remainingFailures[relativePath] = remaining - 1;
            }
        }

        if (shouldFail)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        string fullPath = Path.Combine(_publishRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(fullPath))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        byte[] bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        Func<byte[], byte[]>? corrupt;

        lock (_gate)
        {
            _corruptions.TryGetValue(relativePath, out corrupt);
        }

        if (corrupt != null)
        {
            bytes = corrupt(bytes);
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    }

    private sealed class ReleaseOnDispose : IDisposable
    {
        private readonly TaskCompletionSource _holdGate;

        public ReleaseOnDispose(TaskCompletionSource holdGate)
        {
            _holdGate = holdGate;
        }

        public void Dispose()
        {
            _holdGate.TrySetResult();
        }
    }
}
