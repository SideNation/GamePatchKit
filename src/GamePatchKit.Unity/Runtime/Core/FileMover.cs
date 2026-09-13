#nullable enable
using System.IO;

namespace GamePatchKit.Unity
{
    internal static class FileMover
    {
        // Unity의 .NET Standard 2.1 프로필에는 File.Move(source, destination, overwrite) 오버로드가 없다. 대상이 이미
        // 있으면 File.Replace가 같은 볼륨 안에서 rename으로 교체하므로 대상이 사라지는 순간이 없다.
        public static void MoveReplacing(string sourcePath, string destinationPath)
        {
            if (File.Exists(destinationPath))
            {
                File.Replace(sourcePath, destinationPath, null);
                return;
            }

            File.Move(sourcePath, destinationPath);
        }
    }
}
