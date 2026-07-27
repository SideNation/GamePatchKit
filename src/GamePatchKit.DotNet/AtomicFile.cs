using System;
using System.IO;

namespace GamePatchKit.DotNet;

// Shared by state replacement and cache commit: both write a full replacement to a uniquely named temporary
// file next to the target, then rename it into place. File.Move(..., overwrite: true) is an atomic rename on
// both Windows and Unix in modern .NET, which is what lets a reader only ever observe the old or the new
// bytes, never a partial write.
internal static class AtomicFile
{
    public static string TemporaryPathFor(string finalPath)
    {
        return finalPath + ".tmp-" + Guid.NewGuid().ToString("N");
    }

    public static void TryDeleteTemporary(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
