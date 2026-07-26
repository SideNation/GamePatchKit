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

    // Core owns the ICompressionCodec contract and the codec identifiers but must never reach a concrete
    // implementation, which is what lets Runtime stay free of a compression library.
    [Fact]
    public void CoreDoesNotReferenceACompressionImplementation()
    {
        Assembly assembly = Assembly.Load("GamePatchKit.Core");

        Assert.DoesNotContain(
            assembly.GetReferencedAssemblies(),
            reference => reference.Name != null && reference.Name.Contains("NativeCompressions"));
    }
}
