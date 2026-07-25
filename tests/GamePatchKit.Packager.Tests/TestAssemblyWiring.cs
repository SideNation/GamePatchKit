using System.Reflection;

namespace GamePatchKit.Packager.Tests;

public class TestAssemblyWiring
{
    [Fact]
    public void LoadsProjectAssembly()
    {
        Assembly assembly = Assembly.Load("GamePatchKit.Packager");

        Assert.Equal("GamePatchKit.Packager", assembly.GetName().Name);
    }
}
