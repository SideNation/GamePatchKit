using System.Reflection;

namespace GamePatchKit.Core.Tests;

public class TestAssemblyWiring
{
    [Fact]
    public void LoadsProjectAssembly()
    {
        Assembly assembly = Assembly.Load("GamePatchKit.Core");

        Assert.Equal("GamePatchKit.Core", assembly.GetName().Name);
    }
}
