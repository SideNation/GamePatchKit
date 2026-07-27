using System.Text;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Cli.Tests;

// One temp package per test: a source tree, an output root and a gamepatchkit.yml, plus an in-process runner
// for the CLI.
//
// Program.RunAsync is called directly rather than launching the built executable. It is the same entry point
// Main uses, with the same argument list, exit code and writers, so the contract under test is unchanged -
// and a test can then assert on exact stdout bytes without a process boundary in the way.
internal sealed class CliFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"gamepatchkit-cli-tests-{Guid.NewGuid():N}");

    public string PackageId { get; } = "test-package";

    public string SourceRoot => Path.Combine(_root, "source");

    public string OutputRoot => Path.Combine(_root, "output");

    public string ConfigPath => Path.Combine(_root, "gamepatchkit.yml");

    public CliFixture()
    {
        Directory.CreateDirectory(SourceRoot);
        Directory.CreateDirectory(OutputRoot);
    }

    public void WriteSource(string relativePath, string content)
    {
        string path = Path.Combine(SourceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public string OutputPath(string canonicalPath)
    {
        return Path.Combine(OutputRoot, canonicalPath.Replace('/', Path.DirectorySeparatorChar));
    }

    // Default configuration: one required file group and one optional bundle group, which is enough shape for
    // compact, group selection and per-group reporting to have something to say.
    public void WriteConfig(string? yaml = null)
    {
        File.WriteAllText(ConfigPath, yaml ?? $"""
            schemaVersion: 1
            packageId: {PackageId}
            inputRoot: {SourceRoot.Replace('\\', '/')}
            include:
              - "**/*"
            compression:
              kind: none
            groups:
              - name: core
                include:
                  - "core/**/*"
                artifactMode: file
                required: true
              - name: maps
                include:
                  - "maps/**/*"
                artifactMode: bundle
                required: false
            """);
    }

    public CliRun Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int exitCode = Program.RunAsync(args, output, error, CancellationToken.None).GetAwaiter().GetResult();

        return new CliRun(exitCode, output.ToString(), error.ToString());
    }

    public CliRun RunExpectingSuccess(params string[] args)
    {
        CliRun run = Run(args);
        Assert.True(run.ExitCode == ExitCode.Success, $"exit {run.ExitCode}: {run.StandardOutput}{run.StandardError}");
        return run;
    }

    // Relative path to content hash for every file under the output root: comparing two of these catches an
    // added, removed or rewritten file alike.
    public IReadOnlyDictionary<string, string> OutputTree()
    {
        return Directory.EnumerateFiles(OutputRoot, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(OutputRoot, path).Replace(Path.DirectorySeparatorChar, '/'),
                path => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))),
                StringComparer.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    public static string RepositoryRoot()
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
}

internal sealed record CliRun(int ExitCode, string StandardOutput, string StandardError)
{
    public JObject Json()
    {
        return JObject.Parse(StandardOutput);
    }

    public JObject Result()
    {
        return (JObject)Json()["result"]!;
    }

    public string FirstErrorCode()
    {
        return (string)Json()["errors"]![0]!["code"]!;
    }

    // The canonical JSON bytes exactly as written, so a test can assert the envelope is stable rather than
    // merely parseable.
    public byte[] StandardOutputBytes()
    {
        return Encoding.UTF8.GetBytes(StandardOutput);
    }
}
