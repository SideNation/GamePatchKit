using System.Globalization;
using System.Text;
using GamePatchKit.Core;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Runtime.Tests;

internal static class RuntimeArtifactFixtures
{
    private const int TarBlockSize = 512;

    public static (FinalizedManifest Release, byte[] BundleBytes) CreateBundleRelease(
        bool compressed = false,
        bool addTrailingByte = false)
    {
        const string filePath = "data/bundle.bin";
        byte[] fileBytes = RuntimeFixture.CoreBytes;
        byte[] bundleBytes = CreateTar(filePath, fileBytes);

        if (addTrailingByte)
        {
            bundleBytes = bundleBytes.Concat(new byte[] { 1 }).ToArray();
        }

        string fileHash = RuntimeFixture.Hash(fileBytes);
        string bundleHash = RuntimeFixture.Hash(bundleBytes);
        CompressionKind compression = compressed
            ? CompressionKind.Zstd
            : CompressionKind.None;
        var bundle = new ManifestArtifact.BundleArtifact(
            "core",
            ContentAddressedPath.BundleArtifactPath(
                RuntimeFixture.PackageId,
                "core",
                bundleHash,
                compression),
            bundleBytes.LongLength,
            bundleHash,
            compression,
            new[] { new BundleEntry(filePath) });
        var file = new ManifestFileEntry(
            filePath,
            "core",
            fileBytes.LongLength,
            fileHash,
            new FileSource.BundleEntryReference(bundleHash, filePath));
        var draft = new ReleaseManifest(
            1,
            RuntimeFixture.PackageId,
            "v1-" + new string('0', 64),
            0,
            new[] { new ManifestGroupEntry("core", required: true) },
            new ManifestArtifact[] { bundle },
            new[] { file });
        Assert.True(ManifestValidator.Validate(draft).IsValid);
        return (ReleaseIdentity.Finalize(draft, compactVersion: 0), bundleBytes);
    }

    public static (FinalizedManifest Release, byte[] FirstPart, byte[] SecondPart) CreatePartsRelease()
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes("multipart-runtime-data");
        byte[] first = bytes.Take(7).ToArray();
        byte[] second = bytes.Skip(7).ToArray();
        string artifactHash = RuntimeFixture.Hash(bytes);
        var parts = new[]
        {
            new FilePart(
                0,
                ContentAddressedPath.FilePartPath(RuntimeFixture.PackageId, artifactHash, 0),
                first.LongLength,
                RuntimeFixture.Hash(first)),
            new FilePart(
                1,
                ContentAddressedPath.FilePartPath(RuntimeFixture.PackageId, artifactHash, 1),
                second.LongLength,
                RuntimeFixture.Hash(second)),
        };
        var artifact = new ManifestArtifact.FileArtifact(
            CompressionKind.None,
            new FilePayload.Parts(bytes.LongLength, artifactHash, parts));
        var file = new ManifestFileEntry(
            "data/parts.bin",
            "core",
            bytes.LongLength,
            artifactHash,
            new FileSource.FileReference(artifactHash));
        var draft = new ReleaseManifest(
            1,
            RuntimeFixture.PackageId,
            "v1-" + new string('0', 64),
            0,
            new[] { new ManifestGroupEntry("core", required: true) },
            new ManifestArtifact[] { artifact },
            new[] { file });
        Assert.True(ManifestValidator.Validate(draft).IsValid);
        return (ReleaseIdentity.Finalize(draft, compactVersion: 0), first, second);
    }

    private static byte[] CreateTar(string path, byte[] data)
    {
        using var archive = new MemoryStream();
        byte[] attributes = CreateExtendedAttributes(path, data.LongLength);
        Write(archive, CreateHeader("PaxHeaders/00000000", attributes.LongLength, (byte)'x'));
        Write(archive, attributes);
        WritePadding(archive, attributes.LongLength);
        Write(archive, CreateHeader(path, data.LongLength, (byte)'0'));
        Write(archive, data);
        WritePadding(archive, data.LongLength);
        Write(archive, new byte[TarBlockSize * 2]);
        return archive.ToArray();
    }

    private static byte[] CreateExtendedAttributes(string path, long size)
    {
        using var stream = new MemoryStream();
        WritePaxRecord(stream, "path", path);
        WritePaxRecord(stream, "size", size.ToString(CultureInfo.InvariantCulture));
        WritePaxRecord(stream, "mtime", "0");
        return stream.ToArray();
    }

    private static void WritePaxRecord(Stream destination, string key, string value)
    {
        byte[] body = Encoding.UTF8.GetBytes($"{key}={value}\n");
        int length = body.Length + 3;

        while (true)
        {
            int adjusted = body.Length
                + length.ToString(CultureInfo.InvariantCulture).Length
                + 1;
            if (adjusted == length)
            {
                break;
            }

            length = adjusted;
        }

        Write(
            destination,
            Encoding.ASCII.GetBytes(
                length.ToString(CultureInfo.InvariantCulture) + " "));
        Write(destination, body);
    }

    private static byte[] CreateHeader(string name, long size, byte typeFlag)
    {
        var header = new byte[TarBlockSize];
        Copy(header, 0, Encoding.UTF8.GetBytes(name));
        WriteOctal(header, 100, 8, 420);
        WriteOctal(header, 108, 8, 0);
        WriteOctal(header, 116, 8, 0);
        WriteOctal(header, 124, 12, size);
        WriteOctal(header, 136, 12, 0);

        for (int index = 148; index < 156; index++)
        {
            header[index] = (byte)' ';
        }

        header[156] = typeFlag;
        Copy(header, 257, Encoding.ASCII.GetBytes("ustar\0"));
        Copy(header, 263, Encoding.ASCII.GetBytes("00"));
        WriteOctal(header, 329, 8, 0);
        WriteOctal(header, 337, 8, 0);
        int checksum = header.Sum(value => value);
        string octal = Convert.ToString(checksum, 8);
        int padding = 6 - octal.Length;

        for (int index = 0; index < padding; index++)
        {
            header[148 + index] = (byte)'0';
        }

        Copy(header, 148 + padding, Encoding.ASCII.GetBytes(octal));
        header[154] = 0;
        header[155] = (byte)' ';
        return header;
    }

    private static void WriteOctal(
        byte[] destination,
        int offset,
        int length,
        long value)
    {
        string octal = Convert.ToString(value, 8);
        int padding = length - 1 - octal.Length;

        for (int index = 0; index < padding; index++)
        {
            destination[offset + index] = (byte)'0';
        }

        Copy(destination, offset + padding, Encoding.ASCII.GetBytes(octal));
        destination[offset + length - 1] = 0;
    }

    private static void WritePadding(Stream destination, long size)
    {
        int padding = (int)((TarBlockSize - (size % TarBlockSize)) % TarBlockSize);
        Write(destination, new byte[padding]);
    }

    private static void Write(Stream destination, byte[] bytes)
    {
        destination.Write(bytes, 0, bytes.Length);
    }

    private static void Copy(byte[] destination, int offset, byte[] bytes)
    {
        Buffer.BlockCopy(bytes, 0, destination, offset, bytes.Length);
    }
}
