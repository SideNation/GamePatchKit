using System.Reflection;
using System.Security.Cryptography;
using GamePatchKit.Core;

namespace GamePatchKit.Compression.NativeCompressions.Tests;

public class TestZstdCompressionCodec
{
    [Fact]
    public void FactoryCreatesZstdCodec()
    {
        ICompressionCodec codec = ZstdCompressionCodecFactory.Create();

        Assert.Equal(CompressionCodecIds.Zstd, codec.CodecId);
    }

    [Fact]
    public void PublicApiDoesNotExposeNativeCompressionsTypes()
    {
        Assembly assembly = typeof(ZstdCompressionCodecFactory).Assembly;
        Type[] exposedSignatureTypes = assembly.GetExportedTypes()
            .SelectMany(GetPublicSignatureTypes)
            .ToArray();

        Assert.DoesNotContain(
            exposedSignatureTypes,
            type => type.Assembly.GetName().Name?.StartsWith("NativeCompressions", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task SameInputProducesSameCompressedBytesAcrossSourceChunkSizes()
    {
        byte[] sourceBytes = CreateSourceBytes(1_048_576);

        byte[] smallChunkResult = await CompressAsync(sourceBytes, 17);
        byte[] largeChunkResult = await CompressAsync(sourceBytes, 65_536);

        Assert.Equal(smallChunkResult, largeChunkResult);
    }

    [Fact]
    public async Task StreamingRoundTripRestoresOriginalSha256()
    {
        byte[] sourceBytes = CreateSourceBytes(2_097_152);
        byte[] compressedBytes = await CompressAsync(sourceBytes, 257);
        ICompressionCodec codec = ZstdCompressionCodecFactory.Create();
        await using var compressedStream = new MemoryStream(compressedBytes);
        await using var restoredStream = new MemoryStream();

        await codec.DecompressAsync(compressedStream, restoredStream, CancellationToken.None);

        byte[] expectedHash = SHA256.HashData(sourceBytes);
        byte[] actualHash = SHA256.HashData(restoredStream.ToArray());
        Assert.Equal(expectedHash, actualHash);
    }

    [Fact]
    public async Task EmptyInputProducesAValidRoundTripFrame()
    {
        byte[] compressedBytes = await CompressAsync(Array.Empty<byte>(), 17);
        ICompressionCodec codec = ZstdCompressionCodecFactory.Create();
        await using var compressedStream = new MemoryStream(compressedBytes);
        await using var restoredStream = new MemoryStream();

        await codec.DecompressAsync(compressedStream, restoredStream, CancellationToken.None);

        Assert.NotEmpty(compressedBytes);
        Assert.Empty(restoredStream.ToArray());
    }

    private static async Task<byte[]> CompressAsync(byte[] sourceBytes, int maximumReadSize)
    {
        ICompressionCodec codec = ZstdCompressionCodecFactory.Create();
        await using var source = new ChunkedReadStream(sourceBytes, maximumReadSize);
        await using var destination = new MemoryStream();

        await codec.CompressAsync(source, destination, CancellationToken.None);

        return destination.ToArray();
    }

    private static byte[] CreateSourceBytes(int length)
    {
        var bytes = new byte[length];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)((index * 31L + index / 7) % 251);
        }

        return bytes;
    }

    private static IEnumerable<Type> GetPublicSignatureTypes(Type exportedType)
    {
        const BindingFlags PublicMembers =
            BindingFlags.Instance |
            BindingFlags.Static |
            BindingFlags.Public |
            BindingFlags.DeclaredOnly;

        IEnumerable<Type> fieldTypes = exportedType.GetFields(PublicMembers).Select(field => field.FieldType);
        IEnumerable<Type> propertyTypes = exportedType.GetProperties(PublicMembers).Select(property => property.PropertyType);
        IEnumerable<Type> eventTypes = exportedType.GetEvents(PublicMembers)
            .Select(@event => @event.EventHandlerType)
            .OfType<Type>();
        IEnumerable<Type> methodTypes = exportedType.GetMethods(PublicMembers)
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType));
        IEnumerable<Type> constructorTypes = exportedType.GetConstructors(PublicMembers)
            .SelectMany(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType));

        return fieldTypes
            .Concat(propertyTypes)
            .Concat(eventTypes)
            .Concat(methodTypes)
            .Concat(constructorTypes)
            .SelectMany(ExpandType);
    }

    private static IEnumerable<Type> ExpandType(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is Type elementType)
        {
            foreach (Type nestedType in ExpandType(elementType))
            {
                yield return nestedType;
            }
        }

        foreach (Type genericArgument in type.GetGenericArguments())
        {
            foreach (Type nestedType in ExpandType(genericArgument))
            {
                yield return nestedType;
            }
        }
    }

    private sealed class ChunkedReadStream : Stream
    {
        private readonly MemoryStream _stream;
        private readonly int _maximumReadSize;

        public ChunkedReadStream(byte[] bytes, int maximumReadSize)
        {
            _stream = new MemoryStream(bytes, writable: false);
            _maximumReadSize = maximumReadSize;
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

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _stream.Read(buffer, offset, Math.Min(count, _maximumReadSize));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _stream.ReadAsync(buffer, offset, Math.Min(count, _maximumReadSize), cancellationToken);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return _stream.ReadAsync(buffer[..Math.Min(buffer.Length, _maximumReadSize)], cancellationToken);
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
                _stream.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
