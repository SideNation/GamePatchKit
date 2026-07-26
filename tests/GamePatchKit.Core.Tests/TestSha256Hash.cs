using System;
using System.IO;
using System.Text;
using GamePatchKit.Core;

namespace GamePatchKit.Core.Tests;

public class TestSha256Hash
{
    [Theory]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    public void MatchesKnownAnswerVectors(string input, string expectedHex)
    {
        byte[] data = Encoding.UTF8.GetBytes(input);

        Assert.Equal(expectedHex, Sha256Hash.ComputeHex(data));
        Assert.Equal(expectedHex, Sha256Hash.ComputeHex(new MemoryStream(data)));
    }

    [Fact]
    public void ProducesLowercaseHex64()
    {
        string hex = Sha256Hash.ComputeHex(Encoding.UTF8.GetBytes("game-patch-kit"));

        Assert.True(Hex64.IsValid(hex), hex);
    }

    [Fact]
    public void ReadsStreamsInBoundedChunks()
    {
        byte[] data = new byte[1024 * 1024];

        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 251);
        }

        var stream = new RecordingStream(data);
        string streamHex = Sha256Hash.ComputeHex(stream);

        Assert.Equal(Sha256Hash.ComputeHex(data), streamHex);

        // The whole 1 MiB input is never requested at once, so hashing a package-sized payload costs one
        // fixed buffer rather than the payload itself.
        Assert.True(stream.MaxRequestedCount <= 64 * 1024, $"Largest single read was {stream.MaxRequestedCount} bytes.");
        Assert.True(stream.ReadCallCount >= data.Length / (64 * 1024), $"Only {stream.ReadCallCount} reads for {data.Length} bytes.");
    }

    private sealed class RecordingStream : Stream
    {
        private readonly MemoryStream _inner;

        public RecordingStream(byte[] data)
        {
            _inner = new MemoryStream(data);
        }

        public int MaxRequestedCount { get; private set; }

        public int ReadCallCount { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            MaxRequestedCount = Math.Max(MaxRequestedCount, count);
            ReadCallCount++;

            return _inner.Read(buffer, offset, count);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
