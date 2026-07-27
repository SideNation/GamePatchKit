using GamePatchKit.PerformanceTests.Support;

namespace GamePatchKit.PerformanceTests.Fixtures;

// The shared, on-disk, marker-cached fixture location every scenario/determinism test regenerates from. Kept
// inside the repo (gitignored) rather than under the OS temp directory so a generated fixture survives between
// separate `dotnet test` invocations instead of being rebuilt every run, and so it is easy to find while
// debugging - the 10,000 generated files never get committed (see .gitignore).
internal static class PerformanceFixturePaths
{
    public static string SharedFixturesRoot => Path.Combine(RepositoryRoot.Find(), "tests", "GamePatchKit.PerformanceTests", ".fixtures");
}
