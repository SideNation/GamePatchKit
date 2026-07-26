using System;
using System.IO;
using System.Text;

namespace GamePatchKit.Core.Tests.GoldenVectors;

public static class GoldenVectorFixtures
{
    public static byte[] ReadBytes(string vectorName, string fileName)
    {
        return File.ReadAllBytes(Path.Combine(VectorDirectory(vectorName), fileName));
    }

    public static string ReadText(string vectorName, string fileName)
    {
        return File.ReadAllText(Path.Combine(VectorDirectory(vectorName), fileName), Encoding.ASCII);
    }

    private static string VectorDirectory(string vectorName)
    {
        return Path.Combine(FindRepositoryRoot(), "tests", "fixtures", "golden-vectors", vectorName);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GamePatchKit.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("GamePatchKit.sln not found above " + AppContext.BaseDirectory);
    }
}
