namespace GamePatchKit.PerformanceTests.Support;

// Same walk-up-from-AppContext.BaseDirectory pattern already used by GoldenVectorFixtures, TestSchemaFixtures
// and TestArchitecture in the other test projects.
internal static class RepositoryRoot
{
    public static string Find()
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
