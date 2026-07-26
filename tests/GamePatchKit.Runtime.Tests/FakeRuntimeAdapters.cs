using GamePatchKit.Core;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Runtime.Tests;

internal sealed class FakeArtifactTransport : IArtifactTransport
{
    private readonly Dictionary<string, byte[]> _manifests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _artifacts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _transientFailures = new(StringComparer.Ordinal);

    public Dictionary<string, int> ArtifactOpenCount { get; } = new(StringComparer.Ordinal);

    public Action<string>? BeforeArtifactOpen { get; set; }

    public void AddRelease(FinalizedManifest release)
    {
        _manifests[release.ManifestHash] = release.GetCanonicalBytes();
    }

    public void AddManifest(string manifestHash, byte[] bytes)
    {
        _manifests[manifestHash] = bytes;
    }

    public void AddArtifact(string path, byte[] bytes)
    {
        _artifacts[path] = bytes;
    }

    public void FailTransiently(string path, int count)
    {
        _transientFailures[path] = count;
    }

    public Task<Stream> OpenManifestAsync(
        TargetManifestReference target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(
            new MemoryStream(_manifests[target.ManifestHash], writable: false));
    }

    public Task<Stream> OpenManifestSignatureAsync(
        TargetManifestReference target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(new MemoryStream(Array.Empty<byte>(), writable: false));
    }

    public Task<Stream> OpenArtifactAsync(
        string packageId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArtifactOpenCount.TryGetValue(relativePath, out int count);
        ArtifactOpenCount[relativePath] = count + 1;
        BeforeArtifactOpen?.Invoke(relativePath);
        cancellationToken.ThrowIfCancellationRequested();

        if (_transientFailures.TryGetValue(relativePath, out int failures) && failures > 0)
        {
            _transientFailures[relativePath] = failures - 1;
            throw new ArtifactTransportException("transient failure", isTransient: true);
        }

        return Task.FromResult<Stream>(
            new MemoryStream(_artifacts[relativePath], writable: false));
    }
}

internal sealed class FakeRuntimeStorage : IRuntimeStorage
{
    private readonly Dictionary<string, byte[]> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, byte[]>> _installations =
        new(StringComparer.Ordinal);
    private int _installationIndex;

    public byte[]? StateBytes { get; set; }

    public int ReplaceCount { get; private set; }

    public bool FailNextReplace { get; set; }

    public bool FailNextWriterLock { get; set; }

    public string? FailPromotionForGroup { get; set; }

    public string? FailStagingFilePath { get; set; }

    public Action? BeforeWriterLock { get; set; }

    public IReadOnlyDictionary<string, byte[]> Cache => _cache;

    // Returns a stream of the test's choosing for one cached object path, so a cache entry can be oversized
    // or endless in a way a byte[] cannot express.
    public Func<string, Stream?>? CacheStreamOverride { get; set; }

    public string AddInstallation(string group, params (string Path, byte[] Bytes)[] files)
    {
        string key = $"install/{group}/{++_installationIndex}";
        _installations.Add(
            key,
            files.ToDictionary(
                file => file.Path,
                file => file.Bytes.ToArray(),
                StringComparer.Ordinal));
        return key;
    }

    public void PutCache(string path, byte[] bytes)
    {
        _cache[path] = bytes.ToArray();
    }

    public void RemoveInstallation(string installationKey)
    {
        _installations.Remove(installationKey);
    }

    // Damages an installed file in place, leaving the installation and its paths intact. A stale group's
    // contents are never re-read while the state is loaded, so this is the only way to reach the code that
    // decides what a group becomes when its data no longer verifies.
    public void CorruptInstallationFile(string installationKey, string relativePath, byte[] bytes)
    {
        _installations[installationKey][relativePath] = bytes.ToArray();
    }

    public Task<IAsyncDisposable> AcquirePackageWriterLockAsync(
        string packageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (FailNextWriterLock)
        {
            FailNextWriterLock = false;
            throw new IOException("injected writer lock failure");
        }

        Action? callback = BeforeWriterLock;
        BeforeWriterLock = null;
        callback?.Invoke();
        return Task.FromResult<IAsyncDisposable>(new FakeAsyncDisposable());
    }

    public Task<byte[]?> ReadPackageStateAsync(
        string packageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(StateBytes?.ToArray());
    }

    public Task ReplacePackageStateAsync(
        string packageId,
        byte[] canonicalStateBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (FailNextReplace)
        {
            FailNextReplace = false;
            throw new IOException("injected state replacement failure");
        }

        StateBytes = canonicalStateBytes.ToArray();
        ReplaceCount++;
        return Task.CompletedTask;
    }

    public Task<Stream?> OpenCachedArtifactAsync(
        string packageId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (CacheStreamOverride?.Invoke(relativePath) is Stream overridden)
        {
            return Task.FromResult<Stream?>(overridden);
        }

        Stream? stream = _cache.TryGetValue(relativePath, out byte[]? bytes)
            ? new MemoryStream(bytes, writable: false)
            : null;
        return Task.FromResult(stream);
    }

    public Task<IRuntimeCacheWriter> CreateCacheWriterAsync(
        string packageId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IRuntimeCacheWriter>(
            new FakeCacheWriter(
                bytes => _cache[relativePath] = bytes));
    }

    public Task<bool> InstallationExistsAsync(
        string installationKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_installations.ContainsKey(installationKey));
    }

    public Task<IReadOnlyList<string>> GetInstallationFilePathsAsync(
        string installationKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<string> paths = _installations.TryGetValue(
                installationKey,
                out Dictionary<string, byte[]>? files)
            ? files.Keys.OrderBy(path => path, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
        return Task.FromResult(paths);
    }

    public Task<Stream?> OpenInstallationFileAsync(
        string installationKey,
        string relativePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream? stream = _installations.TryGetValue(
                installationKey,
                out Dictionary<string, byte[]>? files)
            && files.TryGetValue(relativePath, out byte[]? bytes)
                ? new MemoryStream(bytes, writable: false)
                : null;
        return Task.FromResult(stream);
    }

    public Task<IRuntimeStagingArea> CreateStagingAreaAsync(
        string packageId,
        string group,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IRuntimeStagingArea>(
            new FakeStagingArea(this, group));
    }

    public Task<Stream> CreateScratchStreamAsync(
        string packageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(new MemoryStream());
    }

    private sealed class FakeAsyncDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeCacheWriter : IRuntimeCacheWriter
    {
        private readonly Action<byte[]> _commit;
        private readonly MemoryStream _content = new();

        public Stream Content => _content;

        public FakeCacheWriter(Action<byte[]> commit)
        {
            _commit = commit;
        }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _commit(_content.ToArray());
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _content.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeStagingArea : IRuntimeStagingArea
    {
        private readonly FakeRuntimeStorage _owner;
        private readonly string _group;
        private readonly Dictionary<string, MemoryStream> _files = new(StringComparer.Ordinal);

        public FakeStagingArea(FakeRuntimeStorage owner, string group)
        {
            _owner = owner;
            _group = group;
        }

        public Task<Stream> CreateFileAsync(
            string relativePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_owner.FailStagingFilePath == relativePath)
            {
                throw new IOException("injected staging file failure");
            }

            var stream = new MemoryStream();
            _files.Add(relativePath, stream);
            return Task.FromResult<Stream>(new LeaveOpenStream(stream));
        }

        public Task<string> PromoteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_owner.FailPromotionForGroup == _group)
            {
                throw new IOException("injected promotion failure");
            }

            string key = $"install/{_group}/{++_owner._installationIndex}";
            _owner._installations.Add(
                key,
                _files.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.ToArray(),
                    StringComparer.Ordinal));
            return Task.FromResult(key);
        }

        public ValueTask DisposeAsync()
        {
            foreach (MemoryStream stream in _files.Values)
            {
                stream.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class LeaveOpenStream : Stream
    {
        private readonly Stream _inner;

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => _inner.CanWrite;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public LeaveOpenStream(Stream inner)
        {
            _inner = inner;
        }

        public override void Flush()
        {
            _inner.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return _inner.FlushAsync(cancellationToken);
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return _inner.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return _inner.WriteAsync(buffer, offset, count, cancellationToken);
        }
    }
}

internal sealed class PassThroughZstdCodec : ICompressionCodec
{
    public string CodecId => CompressionCodecIds.Zstd;

    public Task CompressAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        return source.CopyToAsync(destination, cancellationToken);
    }

    public Task DecompressAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        return source.CopyToAsync(destination, cancellationToken);
    }
}
