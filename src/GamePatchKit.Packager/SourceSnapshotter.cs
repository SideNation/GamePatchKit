using System.Runtime.InteropServices;
using System.Security.Cryptography;
using GamePatchKit.Core;
using GamePatchKit.Core.Configuration;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Globbing;
using GamePatchKit.Core.Paths;
using Microsoft.Win32.SafeHandles;

namespace GamePatchKit.Packager;

internal sealed class SourceSnapshot
{
    public string RootPath { get; }

    public StableFileMetadata RootMetadata { get; }

    public IReadOnlyList<SourceEntrySnapshot> Entries { get; }

    public IReadOnlyList<SourceFileSnapshot> Files { get; }

    public SourceSnapshot(
        string rootPath,
        StableFileMetadata rootMetadata,
        IReadOnlyList<SourceEntrySnapshot> entries,
        IReadOnlyList<SourceFileSnapshot> files)
    {
        RootPath = rootPath;
        RootMetadata = rootMetadata;
        Entries = entries;
        Files = files;
    }
}

internal sealed class SourceEntrySnapshot
{
    public string RelativePath { get; }

    public string NativePath { get; }

    public SourceEntryKind Kind { get; }

    public StableFileMetadata Metadata { get; }

    public SourceEntrySnapshot(string relativePath, string nativePath, SourceEntryKind kind, StableFileMetadata metadata)
    {
        RelativePath = relativePath;
        NativePath = nativePath;
        Kind = kind;
        Metadata = metadata;
    }
}

internal sealed class SourceFileSnapshot
{
    public string RelativePath { get; }

    public string NativePath { get; }

    public string Group { get; }

    public StableFileMetadata Metadata { get; }

    public string FileHash { get; set; } = string.Empty;

    public long Size => Metadata.Size;

    public SourceFileSnapshot(string relativePath, string nativePath, string group, StableFileMetadata metadata)
    {
        RelativePath = relativePath;
        NativePath = nativePath;
        Group = group;
        Metadata = metadata;
    }
}

internal enum SourceEntryKind
{
    File,
    Directory,
}

internal readonly struct StableFileMetadata : IEquatable<StableFileMetadata>
{
    public ulong DeviceId { get; }

    public ulong FileId { get; }

    public long Size { get; }

    public long ModifiedSeconds { get; }

    public long ModifiedNanoseconds { get; }

    public SourceEntryKind Kind { get; }

    public StableFileMetadata(
        ulong deviceId,
        ulong fileId,
        long size,
        long modifiedSeconds,
        long modifiedNanoseconds,
        SourceEntryKind kind)
    {
        DeviceId = deviceId;
        FileId = fileId;
        Size = size;
        ModifiedSeconds = modifiedSeconds;
        ModifiedNanoseconds = modifiedNanoseconds;
        Kind = kind;
    }

    public bool Equals(StableFileMetadata other)
    {
        return DeviceId == other.DeviceId
            && FileId == other.FileId
            && Size == other.Size
            && ModifiedSeconds == other.ModifiedSeconds
            && ModifiedNanoseconds == other.ModifiedNanoseconds
            && Kind == other.Kind;
    }

    public override bool Equals(object? obj)
    {
        return obj is StableFileMetadata other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(DeviceId, FileId, Size, ModifiedSeconds, ModifiedNanoseconds, Kind);
    }
}

internal static class SourceSnapshotter
{
    private const string Stage = "source-snapshot";
    private const int StreamBufferSize = 64 * 1024;

    public static SourceSnapshot Capture(PackageConfig config)
    {
        string rootPath = Path.GetFullPath(config.InputRoot);
        StableFileMetadata rootMetadata = NativeFileSystem.ReadPathMetadata(rootPath, config.PackageId);

        if (rootMetadata.Kind != SourceEntryKind.Directory)
        {
            throw Failure(
                PackageErrorCodes.InvalidInputRoot,
                "The configured inputRoot must be a directory.",
                config.PackageId);
        }

        var entries = new List<SourceEntrySnapshot>();
        Enumerate(rootPath, rootPath, config.PackageId, entries);
        entries.Sort((left, right) => Utf8OrdinalStringComparer.Instance.Compare(left.RelativePath, right.RelativePath));

        List<SourceEntrySnapshot> fileEntries = entries.Where(entry => entry.Kind == SourceEntryKind.File).ToList();
        FileSelector.SelectionResult selection = FileSelector.Select(
            fileEntries.Select(entry => entry.RelativePath).ToList(),
            config.Include,
            config.Exclude,
            config.Groups.Select(group => new FileSelector.GroupDefinition(group.Name, group.Include)).ToList());

        if (!selection.IsValid)
        {
            throw new PackageException(selection.Errors);
        }

        var entryByPath = fileEntries.ToDictionary(entry => entry.RelativePath, StringComparer.Ordinal);
        var files = new List<SourceFileSnapshot>(selection.SelectedFiles.Count);

        foreach (FileSelector.SelectedFile selected in selection.SelectedFiles)
        {
            SourceEntrySnapshot entry = entryByPath[selected.Path];
            files.Add(new SourceFileSnapshot(entry.RelativePath, entry.NativePath, selected.Group, entry.Metadata));
        }

        return new SourceSnapshot(rootPath, rootMetadata, entries, files);
    }

    public static async Task HashFilesAsync(SourceSnapshot snapshot, string packageId, CancellationToken cancellationToken)
    {
        foreach (SourceFileSnapshot file in snapshot.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            file.FileHash = await HashFileAsync(file, packageId, cancellationToken).ConfigureAwait(false);
        }
    }

    public static void VerifyUnchanged(SourceSnapshot expected, PackageConfig config)
    {
        SourceSnapshot actual = Capture(config);

        if (!expected.RootMetadata.Equals(actual.RootMetadata))
        {
            throw SourceChanged(config.PackageId, null, "The source root changed during packaging.");
        }

        if (expected.Entries.Count != actual.Entries.Count)
        {
            throw SourceChanged(config.PackageId, null, "The source entry set changed during packaging.");
        }

        for (int index = 0; index < expected.Entries.Count; index++)
        {
            SourceEntrySnapshot left = expected.Entries[index];
            SourceEntrySnapshot right = actual.Entries[index];

            if (left.RelativePath != right.RelativePath || left.Kind != right.Kind || !left.Metadata.Equals(right.Metadata))
            {
                throw SourceChanged(config.PackageId, left.RelativePath, "A source entry changed during packaging.");
            }
        }

        if (expected.Files.Count != actual.Files.Count)
        {
            throw SourceChanged(config.PackageId, null, "The selected source file set changed during packaging.");
        }

        for (int index = 0; index < expected.Files.Count; index++)
        {
            SourceFileSnapshot left = expected.Files[index];
            SourceFileSnapshot right = actual.Files[index];

            if (left.RelativePath != right.RelativePath || left.Group != right.Group || !left.Metadata.Equals(right.Metadata))
            {
                throw SourceChanged(config.PackageId, left.RelativePath, "A selected source file changed during packaging.");
            }
        }
    }

    public static SafeFileHandle OpenVerifiedFile(SourceFileSnapshot file, string packageId)
    {
        SafeFileHandle handle = NativeFileSystem.OpenFileNoFollow(file.NativePath, packageId, file.RelativePath);

        try
        {
            StableFileMetadata openedMetadata = NativeFileSystem.ReadHandleMetadata(handle, packageId, file.RelativePath);
            if (!file.Metadata.Equals(openedMetadata))
            {
                throw SourceChanged(packageId, file.RelativePath, "The source file was replaced before it could be opened.");
            }

            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public static void VerifyOpenedFile(SafeFileHandle handle, SourceFileSnapshot file, string packageId)
    {
        StableFileMetadata metadata = NativeFileSystem.ReadHandleMetadata(handle, packageId, file.RelativePath);

        if (!file.Metadata.Equals(metadata))
        {
            throw SourceChanged(packageId, file.RelativePath, "The source file changed while it was being read.");
        }
    }

    private static void Enumerate(string rootPath, string directoryPath, string packageId, List<SourceEntrySnapshot> entries)
    {
        StableFileMetadata before = NativeFileSystem.ReadPathMetadata(directoryPath, packageId);

        if (before.Kind != SourceEntryKind.Directory)
        {
            string relativeDirectory = directoryPath == rootPath ? null! : NormalizeRelativePath(rootPath, directoryPath, packageId);
            throw SourceChanged(packageId, relativeDirectory, "A source directory was replaced during enumeration.");
        }

        string[] nativeEntries;

        try
        {
            nativeEntries = Directory.GetFileSystemEntries(directoryPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Failure(
                PackageErrorCodes.InvalidInputRoot,
                "The source directory could not be enumerated.",
                packageId);
        }

        foreach (string nativeEntry in nativeEntries)
        {
            string relativePath = NormalizeRelativePath(rootPath, nativeEntry, packageId);
            StableFileMetadata metadata = NativeFileSystem.ReadPathMetadata(nativeEntry, packageId, relativePath);
            var entry = new SourceEntrySnapshot(relativePath, nativeEntry, metadata.Kind, metadata);
            entries.Add(entry);

            if (metadata.Kind == SourceEntryKind.Directory)
            {
                Enumerate(rootPath, nativeEntry, packageId, entries);
            }
        }

        StableFileMetadata after = NativeFileSystem.ReadPathMetadata(directoryPath, packageId);
        if (!before.Equals(after))
        {
            string? relativeDirectory = directoryPath == rootPath ? null : NormalizeRelativePath(rootPath, directoryPath, packageId);
            throw SourceChanged(packageId, relativeDirectory, "A source directory changed during enumeration.");
        }
    }

    private static string NormalizeRelativePath(string rootPath, string nativePath, string packageId)
    {
        string candidate = Path.GetRelativePath(rootPath, nativePath);

        if (OperatingSystem.IsWindows())
        {
            candidate = candidate.Replace('\\', '/');
        }

        if (!RelativePathNormalizer.TryNormalize(candidate, out string normalized, out string errorCode))
        {
            throw Failure(
                errorCode,
                "A source path cannot be represented as a canonical relative path.",
                packageId,
                candidate);
        }

        return normalized;
    }

    private static async Task<string> HashFileAsync(SourceFileSnapshot file, string packageId, CancellationToken cancellationToken)
    {
        using SafeFileHandle handle = OpenVerifiedFile(file, packageId);
        using var stream = new FileStream(handle, FileAccess.Read, StreamBufferSize, isAsync: false);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[StreamBufferSize];
        long size = 0;

        while (true)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, read);
            size += read;
        }

        VerifyOpenedFile(handle, file, packageId);

        if (size != file.Size)
        {
            throw SourceChanged(packageId, file.RelativePath, "The source file size changed while it was being hashed.");
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static PackageException SourceChanged(string packageId, string? relativePath, string message)
    {
        return Failure(PackageErrorCodes.SourceChanged, message, packageId, relativePath);
    }

    private static PackageException Failure(string code, string message, string packageId, string? relativePath = null)
    {
        return new PackageException(new GamePatchKitError(Stage, code, message, packageId, relativePath));
    }
}

internal static class NativeFileSystem
{
    private const string Stage = "source-snapshot";

    private const uint WindowsFileAttributeDirectory = 0x00000010;
    private const uint WindowsFileAttributeReparsePoint = 0x00000400;
    private const uint WindowsGenericRead = 0x80000000;
    private const uint WindowsShareRead = 0x00000001;
    private const uint WindowsShareWrite = 0x00000002;
    private const uint WindowsShareDelete = 0x00000004;
    private const uint WindowsOpenExisting = 3;
    private const uint WindowsFlagBackupSemantics = 0x02000000;
    private const uint WindowsFlagOpenReparsePoint = 0x00200000;
    private const uint WindowsFlagSequentialScan = 0x08000000;

    private const int UnixOpenReadOnly = 0;
    private const uint UnixFileTypeMask = 0xF000;
    private const uint UnixFileTypeRegular = 0x8000;
    private const uint UnixFileTypeDirectory = 0x4000;
    private const uint UnixFileTypeLink = 0xA000;
    private const int StatBufferSize = 512;

    public static StableFileMetadata ReadPathMetadata(string path, string packageId, string? relativePath = null)
    {
        EnsureSupportedPlatform(packageId, relativePath);

        try
        {
            return OperatingSystem.IsWindows()
                ? ReadWindowsPathMetadata(path, packageId, relativePath)
                : ReadUnixPathMetadata(path, packageId, relativePath);
        }
        catch (PackageException)
        {
            throw;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException)
        {
            throw Failure(
                PackageErrorCodes.UnsupportedFileSystem,
                "Stable file identity is unavailable on this platform.",
                packageId,
                relativePath);
        }
    }

    public static SafeFileHandle OpenFileNoFollow(string path, string packageId, string relativePath)
    {
        EnsureSupportedPlatform(packageId, relativePath);

        if (OperatingSystem.IsWindows())
        {
            SafeFileHandle handle = CreateFile(
                path,
                WindowsGenericRead,
                WindowsShareRead | WindowsShareWrite | WindowsShareDelete,
                IntPtr.Zero,
                WindowsOpenExisting,
                WindowsFlagOpenReparsePoint | WindowsFlagSequentialScan,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                handle.Dispose();
                throw Failure(
                    PackageErrorCodes.SourceChanged,
                    "The source file could not be opened without following links.",
                    packageId,
                    relativePath);
            }

            return handle;
        }

        int noFollow = OperatingSystem.IsMacOS() ? 0x00000100 : 0x00020000;
        int descriptor = Open(path, UnixOpenReadOnly | noFollow);

        if (descriptor < 0)
        {
            throw Failure(PackageErrorCodes.SourceChanged, "The source file could not be opened without following links.", packageId, relativePath);
        }

        return new SafeFileHandle(new IntPtr(descriptor), ownsHandle: true);
    }

    public static StableFileMetadata ReadHandleMetadata(SafeFileHandle handle, string packageId, string? relativePath)
    {
        EnsureSupportedPlatform(packageId, relativePath);

        if (OperatingSystem.IsWindows())
        {
            if (!GetFileInformationByHandle(handle, out ByHandleFileInformation information))
            {
                throw Failure(PackageErrorCodes.SourceChanged, "The opened source file metadata could not be read.", packageId, relativePath);
            }

            return WindowsMetadata(information, packageId, relativePath);
        }

        IntPtr buffer = Marshal.AllocHGlobal(StatBufferSize);

        try
        {
            if (FStat(handle.DangerousGetHandle().ToInt32(), buffer) != 0)
            {
                throw Failure(PackageErrorCodes.SourceChanged, "The opened source file metadata could not be read.", packageId, relativePath);
            }

            return UnixMetadata(buffer, packageId, relativePath);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static StableFileMetadata ReadWindowsPathMetadata(string path, string packageId, string? relativePath)
    {
        SafeFileHandle handle = CreateFile(
            path,
            0,
            WindowsShareRead | WindowsShareWrite | WindowsShareDelete,
            IntPtr.Zero,
            WindowsOpenExisting,
            WindowsFlagBackupSemantics | WindowsFlagOpenReparsePoint,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw Failure(PackageErrorCodes.InvalidInputRoot, "A source entry could not be inspected.", packageId, relativePath);
        }

        using (handle)
        {
            if (!GetFileInformationByHandle(handle, out ByHandleFileInformation information))
            {
                throw Failure(PackageErrorCodes.InvalidInputRoot, "A source entry could not be inspected.", packageId, relativePath);
            }

            return WindowsMetadata(information, packageId, relativePath);
        }
    }

    private static StableFileMetadata WindowsMetadata(ByHandleFileInformation information, string packageId, string? relativePath)
    {
        if ((information.FileAttributes & WindowsFileAttributeReparsePoint) != 0)
        {
            throw Failure(
                PackageErrorCodes.UnsupportedEntry,
                "Symlink, junction, and reparse-point source entries are not allowed.",
                packageId,
                relativePath);
        }

        bool isDirectory = (information.FileAttributes & WindowsFileAttributeDirectory) != 0;
        SourceEntryKind kind = isDirectory ? SourceEntryKind.Directory : SourceEntryKind.File;
        ulong fileId = ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow;
        long size = isDirectory ? 0 : ((long)information.FileSizeHigh << 32) | information.FileSizeLow;
        long fileTime = ((long)information.LastWriteTimeHigh << 32) | information.LastWriteTimeLow;

        return new StableFileMetadata(information.VolumeSerialNumber, fileId, size, fileTime, 0, kind);
    }

    private static StableFileMetadata ReadUnixPathMetadata(string path, string packageId, string? relativePath)
    {
        IntPtr buffer = Marshal.AllocHGlobal(StatBufferSize);

        try
        {
            if (LStat(path, buffer) != 0)
            {
                throw Failure(PackageErrorCodes.InvalidInputRoot, "A source entry could not be inspected.", packageId, relativePath);
            }

            return UnixMetadata(buffer, packageId, relativePath);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static StableFileMetadata UnixMetadata(IntPtr buffer, string packageId, string? relativePath)
    {
        bool isMac = OperatingSystem.IsMacOS();
        ulong deviceId = isMac ? unchecked((uint)Marshal.ReadInt32(buffer, 0)) : unchecked((ulong)Marshal.ReadInt64(buffer, 0));
        ulong fileId = unchecked((ulong)Marshal.ReadInt64(buffer, 8));
        uint mode = isMac ? unchecked((ushort)Marshal.ReadInt16(buffer, 4)) : unchecked((uint)Marshal.ReadInt32(buffer, 24));
        uint fileType = mode & UnixFileTypeMask;

        if (fileType == UnixFileTypeLink)
        {
            throw Failure(PackageErrorCodes.UnsupportedEntry, "Symlink source entries are not allowed.", packageId, relativePath);
        }

        SourceEntryKind kind;

        if (fileType == UnixFileTypeRegular)
        {
            kind = SourceEntryKind.File;
        }
        else if (fileType == UnixFileTypeDirectory)
        {
            kind = SourceEntryKind.Directory;
        }
        else
        {
            throw Failure(
                PackageErrorCodes.UnsupportedEntry,
                "Only regular files and directories are allowed in the source tree.",
                packageId,
                relativePath);
        }

        long size = kind == SourceEntryKind.File ? Marshal.ReadInt64(buffer, isMac ? 96 : 48) : 0;
        long modifiedSeconds = Marshal.ReadInt64(buffer, isMac ? 48 : 88);
        long modifiedNanoseconds = Marshal.ReadInt64(buffer, isMac ? 56 : 96);

        return new StableFileMetadata(deviceId, fileId, size, modifiedSeconds, modifiedNanoseconds, kind);
    }

    private static PackageException Failure(string code, string message, string packageId, string? relativePath)
    {
        return new PackageException(new GamePatchKitError(Stage, code, message, packageId, relativePath));
    }

    private static void EnsureSupportedPlatform(string packageId, string? relativePath)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw Failure(
                PackageErrorCodes.UnsupportedFileSystem,
                "Stable file identity is supported only on Windows, Linux, and macOS.",
                packageId,
                relativePath);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

    [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
    private static extern int LStat(string path, IntPtr buffer);

    [DllImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static extern int FStat(int fileDescriptor, IntPtr buffer);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open(string path, int flags);
}
