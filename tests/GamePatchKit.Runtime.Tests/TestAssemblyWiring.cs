using System.Reflection;

namespace GamePatchKit.Runtime.Tests;

public class TestAssemblyWiring
{
    [Fact]
    public void LoadsProjectAssembly()
    {
        Assembly assembly = Assembly.Load("GamePatchKit.Runtime");

        Assert.Equal("GamePatchKit.Runtime", assembly.GetName().Name);
    }
}
