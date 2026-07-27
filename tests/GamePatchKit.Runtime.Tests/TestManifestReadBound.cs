using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Runtime.Tests;

// The manifest is read into memory before its hash can reject it, so the read itself has to be bounded. These
// use a transport that never stops producing bytes: without a bound the runtime would read until the process
// died, so a test that merely returns "a big array" would prove much less than one that cannot terminate on
// its own.
public class TestManifestReadBound
{
    [Fact]
    public async Task EndlessManifestResponse_IsRejectedInsteadOfReadUntilMemoryRunsOut()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new EndlessManifestTransport();
        var runtime = new PackageRuntime(transport, new FakeRuntimeStorage());

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release)));

        Assert.Equal(RuntimeErrorCodes.ManifestInvalid, exception.Error.Code);

        // Abandoned early rather than drained: the stream is infinite, so any bound at all must stop while it
        // still has more to give.
        Assert.True(
            transport.BytesRead <= 128L * 1024 * 1024,
            $"read {transport.BytesRead} bytes from an endless manifest response");
    }

    [Fact]
    public async Task OversizedResponseIsNotRetried()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new EndlessManifestTransport();
        var runtime = new PackageRuntime(transport, new FakeRuntimeStorage());

        await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release)));

        // A response too large to be this manifest is a broken endpoint, not a transient failure; retrying
        // would spend the limit twice more for the same answer.
        Assert.Equal(1, transport.ManifestOpenCount);
    }

    private sealed class EndlessManifestTransport : IArtifactTransport
    {
        public int ManifestOpenCount { get; private set; }

        public long BytesRead => _stream?.BytesRead ?? 0;

        private EndlessStream? _stream;

        public Task<Stream> OpenManifestAsync(TargetManifestReference target, CancellationToken cancellationToken)
        {
            ManifestOpenCount++;
            _stream = new EndlessStream();
            return Task.FromResult<Stream>(_stream);
        }

        public Task<Stream> OpenManifestSignatureAsync(TargetManifestReference target, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<Stream> OpenArtifactAsync(string packageId, string objectPath, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class EndlessStream : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => BytesRead;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)'{', offset, count);
            BytesRead += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
