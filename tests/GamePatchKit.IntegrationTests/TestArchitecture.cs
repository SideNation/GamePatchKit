using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace GamePatchKit.IntegrationTests;

public class TestArchitecture
{
    private static readonly string[] _forbiddenAssemblyPrefixes = ["UnityEngine", "NativeCompressions"];

    private static readonly IReadOnlyDictionary<string, string[]> _allowedProjectReferences =
        new Dictionary<string, string[]>
        {
            ["GamePatchKit.Core"] = [],
            ["GamePatchKit.Compression.NativeCompressions"] = ["GamePatchKit.Core"],
            ["GamePatchKit.Runtime"] = ["GamePatchKit.Core"],
            ["GamePatchKit.Packager"] = ["GamePatchKit.Compression.NativeCompressions", "GamePatchKit.Core"],
            ["GamePatchKit.DotNet"] = ["GamePatchKit.Compression.NativeCompressions", "GamePatchKit.Core", "GamePatchKit.Runtime"],
            ["GamePatchKit.Cli"] = ["GamePatchKit.Packager"],
        };

    [Fact]
    public void SourceProjectReferencesMatchAllowedGraph()
    {
        string sourceRoot = Path.Combine(FindRepositoryRoot(), "src");

        foreach ((string projectName, string[] allowedReferences) in _allowedProjectReferences)
        {
            string projectPath = Path.Combine(sourceRoot, projectName, projectName + ".csproj");
            string[] actualReferences = ReadProjectReferenceNames(projectPath)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(allowedReferences, actualReferences);
        }
    }

    [Fact]
    public void CoreAndRuntimeProjectsDoNotReferenceForbiddenPackages()
    {
        string sourceRoot = Path.Combine(FindRepositoryRoot(), "src");

        foreach (string projectName in new[] { "GamePatchKit.Core", "GamePatchKit.Runtime" })
        {
            string projectPath = Path.Combine(sourceRoot, projectName, projectName + ".csproj");
            string[] forbiddenPackages = ReadPackageReferenceNames(projectPath)
                .Where(IsForbiddenAssemblyName)
                .ToArray();

            Assert.Empty(forbiddenPackages);
        }
    }

    [Fact]
    public void CoreAssemblyReferencesNoGamePatchKitOrForbiddenAssemblies()
    {
        string[] references = ReadAssemblyReferenceNames("GamePatchKit.Core.dll");
        string[] gamePatchKitReferences = references
            .Where(name => name.StartsWith("GamePatchKit", StringComparison.Ordinal))
            .ToArray();
        string[] forbiddenReferences = references.Where(IsForbiddenAssemblyName).ToArray();

        Assert.Empty(gamePatchKitReferences);
        Assert.Empty(forbiddenReferences);
    }

    [Fact]
    public void RuntimeAssemblyReferencesOnlyCoreAmongGamePatchKitAssemblies()
    {
        string[] references = ReadAssemblyReferenceNames("GamePatchKit.Runtime.dll");
        string[] disallowedReferences = references
            .Where(name => name.StartsWith("GamePatchKit", StringComparison.Ordinal) && name != "GamePatchKit.Core")
            .ToArray();
        string[] forbiddenReferences = references.Where(IsForbiddenAssemblyName).ToArray();

        Assert.Empty(disallowedReferences);
        Assert.Empty(forbiddenReferences);
    }

    private static bool IsForbiddenAssemblyName(string assemblyName)
    {
        return _forbiddenAssemblyPrefixes.Any(prefix => assemblyName.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
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

    private static string[] ReadProjectReferenceNames(string projectPath)
    {
        return XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => (string?)reference.Attribute("Include") ?? string.Empty)
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
            .ToArray();
    }

    private static string[] ReadPackageReferenceNames(string projectPath)
    {
        return XDocument.Load(projectPath)
            .Descendants("PackageReference")
            .Select(reference => (string?)reference.Attribute("Include") ?? string.Empty)
            .ToArray();
    }

    private static string[] ReadAssemblyReferenceNames(string assemblyFileName)
    {
        string assemblyPath = Path.Combine(AppContext.BaseDirectory, assemblyFileName);
        using FileStream stream = File.OpenRead(assemblyPath);
        using PEReader peReader = new PEReader(stream);
        MetadataReader metadataReader = peReader.GetMetadataReader();

        return metadataReader.AssemblyReferences
            .Select(handle => metadataReader.GetString(metadataReader.GetAssemblyReference(handle).Name))
            .ToArray();
    }
}
