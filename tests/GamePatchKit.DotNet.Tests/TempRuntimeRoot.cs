namespace GamePatchKit.DotNet.Tests;

internal sealed class TempRuntimeRoot : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        $"gamepatchkit-dotnet-tests-{Guid.NewGuid():N}");

    public TempRuntimeRoot()
    {
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
