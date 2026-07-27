using System;
using System.IO;

namespace GamePatchKit.Unity
{
    internal static class UnityAtomicFile
    {
        public static string TemporaryPathFor(string finalPath)
        {
            return finalPath + ".tmp-" + Guid.NewGuid().ToString("N");
        }

        public static void Replace(string temporaryPath, string finalPath)
        {
            if (File.Exists(finalPath))
            {
                File.Replace(temporaryPath, finalPath, destinationBackupFileName: null);
                return;
            }

            File.Move(temporaryPath, finalPath);
        }

        public static void TryDelete(string path)
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
}

