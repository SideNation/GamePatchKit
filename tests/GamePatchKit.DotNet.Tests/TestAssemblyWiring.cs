using System.Reflection;

namespace GamePatchKit.DotNet.Tests;

public class TestAssemblyWiring
{
    [Fact]
    public void LoadsProjectAssembly()
    {
        Assembly assembly = Assembly.Load("GamePatchKit.DotNet");

        Assert.Equal("GamePatchKit.DotNet", assembly.GetName().Name);
    }
}
