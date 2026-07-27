namespace GamePatchKit.PerformanceTests.Scenarios;

// The repeat-reset rule: a scenario that mutates output-root or runtime-root must start every one of its 3
// measured repeats from the same pristine state, or the 2nd/3rd repeat silently takes a cheaper "already
// there" path and under-reports peak RSS. This is the reset primitive those scenarios use.
internal static class DirectoryCopy
{
    public static void CopyRecursively(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (string directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, directory)));
        }

        foreach (string file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, file));
            File.Copy(file, destination, overwrite: true);
        }
    }
}
