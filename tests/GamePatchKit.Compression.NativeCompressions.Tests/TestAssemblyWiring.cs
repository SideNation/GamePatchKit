using System.Reflection;

namespace GamePatchKit.Compression.NativeCompressions.Tests;

public class TestAssemblyWiring
{
    [Fact]
    public void LoadsProjectAssembly()
    {
        Assembly assembly = Assembly.Load("GamePatchKit.Compression.NativeCompressions");

        Assert.Equal("GamePatchKit.Compression.NativeCompressions", assembly.GetName().Name);
    }
}
