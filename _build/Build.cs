using System;
using System.Linq;
using System.Xml.Linq;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Compile);

    // Nuke resolves every injected member by its *member name*: --configuration binds to Configuration, and
    // .nuke/parameters.json's "Solution" binds to Solution. That makes the member name part of the command-line
    // contract, so these deliberately use Nuke's PascalCase convention instead of the repository's _camelCase
    // rule for private fields - with underscores they were exposed as --_configuration/--_version and the
    // [Solution] injection failed outright.
    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    private readonly string Configuration = IsLocalBuild ? "Debug" : "Release";

    [Parameter("NuGet API key for publishing packages")]
    [Secret]
    private string NuGetApiKey = Environment.GetEnvironmentVariable("NUGET_API_KEY");

    [Parameter("NuGet source URL - Default is nuget.org")]
    private readonly string NuGetSource = "https://api.nuget.org/v3/index.json";

    [Parameter("Package version override")]
    private readonly string Version;

    [Solution]
    private readonly Solution Solution;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    // The PRD's deployment artifacts: four NuGet libraries plus the gpk .NET tool. GamePatchKit.Packager is
    // deliberately absent - it ships inside the gpk tool package (PackAsTool bundles project references) and
    // has no standalone consumers yet.
    string[] PackableProjects =>
    [
        "GamePatchKit.Core",
        "GamePatchKit.Runtime",
        "GamePatchKit.Compression.NativeCompressions",
        "GamePatchKit.DotNet",
        "GamePatchKit.Cli"
    ];

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(x => x.DeleteDirectory());
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(x => x.DeleteDirectory());
            ArtifactsDirectory.CreateOrCleanDirectory();
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .EnableNoRestore());
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetTest(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetFilter("Category!=Performance")
                .EnableNoRestore()
                .EnableNoBuild());
        });

    // Step 12 (docs/plan/12-performance-validation.md): the 10,000-file/1GiB performance suite is excluded
    // from Test above and run through this separate target instead, since it is far slower than the rest of
    // the suite and its real blocking-gate assertions only apply on Linux (see docs/perf/README.md).
    Target Performance => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetTest(s => s
                .SetProjectFile(TestsDirectory / "GamePatchKit.PerformanceTests" / "GamePatchKit.PerformanceTests.csproj")
                .SetConfiguration(Configuration)
                .SetFilter("Category=Performance")
                .EnableNoRestore()
                .EnableNoBuild());
        });

    Target Pack => _ => _
        .DependsOn(Test)
        .Produces(ArtifactsDirectory / "*.nupkg")
        .Executes(() =>
        {
            ArtifactsDirectory.CreateOrCleanDirectory();

            var buildPropsPath = RootDirectory / "Directory.Build.props";
            var doc = XDocument.Load(buildPropsPath);
            var versionElement = doc.Descendants("Version").First();

            string packVersion;
            if (Version != null)
            {
                packVersion = Version;
            }
            else
            {
                var parts = versionElement.Value.Split('.');
                parts[2] = (int.Parse(parts[2]) + 1).ToString();
                packVersion = string.Join(".", parts);
            }

            versionElement.Value = packVersion;
            doc.Save(buildPropsPath);

            foreach (var name in PackableProjects)
            {
                var projectPath = SourceDirectory / name / (name + ".csproj");
                DotNetPack(s => s
                    .SetProject(projectPath)
                    .SetConfiguration("Release")
                    .SetOutputDirectory(ArtifactsDirectory)
                    .SetVersion(packVersion));
            }
        });

    Target Push => _ => _
        .DependsOn(Pack)
        .Executes(() =>
        {
            if (string.IsNullOrEmpty(NuGetApiKey))
                throw new Exception(
                    "NuGet API key is not set. Provide it via --nuget-api-key parameter or NUGET_API_KEY environment variable.");

            ArtifactsDirectory.GlobFiles("*.nupkg")
                .Where(x => !x.ToString().EndsWith(".symbols.nupkg"))
                .ForEach(package =>
                {
                    DotNetNuGetPush(s => s
                        .SetTargetPath(package)
                        .SetSource(NuGetSource)
                        .SetApiKey(NuGetApiKey)
                        .EnableSkipDuplicate());
                });
        });
}
