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

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    private readonly string _configuration = IsLocalBuild ? "Debug" : "Release";

    [Parameter("NuGet API key for publishing packages")]
    [Secret]
    private string _nuGetApiKey = Environment.GetEnvironmentVariable("NUGET_API_KEY");

    [Parameter("NuGet source URL - Default is nuget.org")]
    private readonly string _nuGetSource = "https://api.nuget.org/v3/index.json";

    [Parameter("Package version override")]
    private readonly string _version;

    [Solution]
    private readonly Solution _solution;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    string[] PackableProjects =>
    [
        "GamePatchKit.Core",
        "GamePatchKit.Runtime",
        "GamePatchKit.Compression.NativeCompressions",
        "GamePatchKit.DotNet"
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
                .SetProjectFile(_solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(_solution)
                .SetConfiguration(_configuration)
                .EnableNoRestore());
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetTest(s => s
                .SetProjectFile(_solution)
                .SetConfiguration(_configuration)
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
            if (_version != null)
            {
                packVersion = _version;
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
            if (string.IsNullOrEmpty(_nuGetApiKey))
                throw new Exception(
                    "NuGet API key is not set. Provide it via --nuget-api-key parameter or NUGET_API_KEY environment variable.");

            ArtifactsDirectory.GlobFiles("*.nupkg")
                .Where(x => !x.ToString().EndsWith(".symbols.nupkg"))
                .ForEach(package =>
                {
                    DotNetNuGetPush(s => s
                        .SetTargetPath(package)
                        .SetSource(_nuGetSource)
                        .SetApiKey(_nuGetApiKey)
                        .EnableSkipDuplicate());
                });
        });
}
