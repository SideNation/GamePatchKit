using GamePatchKit.PerformanceTests.Fixtures;
using GamePatchKit.PerformanceTests.Support;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.PerformanceTests.Scenarios;

// Builds, once per fixture size and shared across every scenario test in the class (xUnit IClassFixture),
// the "golden" published states each scenario measures against: a first release, a second release built
// incrementally on it, and a signed copy of the first release. Scenario tests copy from these read-only
// golden directories rather than mutating them, so one setup pass serves every repeated measurement.
public sealed class ScenarioFixtureSet : IAsyncLifetime
{
    private readonly Dictionary<string, Task<SizeState>> _sizes = new(StringComparer.Ordinal);
    private readonly object _gate = new object();
    private string _workRoot = null!;

    public sealed record SizeState(
        PerformanceFixtureGenerator.FixtureLayout Fixture,
        string AfterInitialOutputRoot,
        string InitialManifestHash,
        string InitialDataVersion,
        string AfterIncrementalOutputRoot,
        string IncrementalManifestHash,
        string MutatedConfigPath,
        string SignedOutputRoot,
        string TrustedPublicKeyBase64Url);

    public Task InitializeAsync()
    {
        _workRoot = Path.Combine(Path.GetTempPath(), "gpk-perf-work-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workRoot);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_workRoot))
        {
            Directory.Delete(_workRoot, recursive: true);
        }

        return Task.CompletedTask;
    }

    // Builds golden state for one fixture size on first request and reuses it for every later caller,
    // regardless of which scenario asks first - every scenario in this class needs the same golden states.
    public Task<SizeState> GetOrBuildAsync(string sizeLabel, long totalBytes)
    {
        lock (_gate)
        {
            if (!_sizes.TryGetValue(sizeLabel, out Task<SizeState>? task))
            {
                task = BuildAsync(sizeLabel, totalBytes);
                _sizes[sizeLabel] = task;
            }

            return task;
        }
    }

    private async Task<SizeState> BuildAsync(string sizeLabel, long totalBytes)
    {
        PerformanceFixtureGenerator.FixtureLayout fixture = PerformanceFixtureGenerator.Ensure(
            PerformanceFixturePaths.SharedFixturesRoot, totalBytes, sizeLabel);

        string sizeWorkRoot = Path.Combine(_workRoot, sizeLabel);
        Directory.CreateDirectory(sizeWorkRoot);

        string afterInitialOutputRoot = Path.Combine(sizeWorkRoot, "after-initial");
        Directory.CreateDirectory(afterInitialOutputRoot);
        JObject initialResult = await RunCliAsync(
            "package", "--config", fixture.ConfigPath, "--output-root", afterInitialOutputRoot, "--json");
        AssertMatchesRequestedFixtureSize(initialResult, totalBytes);
        string initialManifestHash = ManifestHash(initialResult);
        string initialDataVersion = DataVersion(initialResult);

        string mutatedInputRoot = Path.Combine(sizeWorkRoot, "mutated-input");
        DirectoryCopy.CopyRecursively(fixture.InputRoot, mutatedInputRoot);
        PerformanceFixtureGenerator.MutateForIncremental(mutatedInputRoot);
        string mutatedConfigPath = WriteConfigWithInputRoot(
            fixture.ConfigPath, mutatedInputRoot, Path.Combine(sizeWorkRoot, "gamepatchkit-mutated.yml"));

        string afterIncrementalOutputRoot = Path.Combine(sizeWorkRoot, "after-incremental");
        DirectoryCopy.CopyRecursively(afterInitialOutputRoot, afterIncrementalOutputRoot);
        JObject incrementalResult = await RunCliAsync(
            "package", "--config", mutatedConfigPath, "--output-root", afterIncrementalOutputRoot,
            "--previous", initialManifestHash, "--json");
        string incrementalManifestHash = ManifestHash(incrementalResult);

        string signedOutputRoot = Path.Combine(sizeWorkRoot, "signed");
        DirectoryCopy.CopyRecursively(afterInitialOutputRoot, signedOutputRoot);
        (string privateKeyBase64Url, string publicKeyBase64Url) = FixedTestSigningKey.Generate();
        string keyFilePath = Path.Combine(sizeWorkRoot, "signing.key");
        File.WriteAllText(keyFilePath, privateKeyBase64Url);
        await RunCliAsync(
            "sign", "--output-root", signedOutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", initialManifestHash, "--key-file", keyFilePath, "--json");

        return new SizeState(
            fixture,
            afterInitialOutputRoot,
            initialManifestHash,
            initialDataVersion,
            afterIncrementalOutputRoot,
            incrementalManifestHash,
            mutatedConfigPath,
            signedOutputRoot,
            publicKeyBase64Url);
    }

    private static async Task<JObject> RunCliAsync(params string[] args)
    {
        ChildProcess.Result result = await ChildProcess.RunAsync(ChildProcess.CliDllPath, args);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"gpk {args[0]} failed (exit {result.ExitCode}).\nstdout: {result.StandardOutput}\nstderr: {result.StandardError}");
        }

        return JObject.Parse(result.StandardOutput);
    }

    // A config or glob regression that silently drops files would still let `gpk package` exit 0 - it would
    // just package fewer files than intended. Without this check, every downstream scenario would go on to
    // measure and certify an undersized workload as if it were the real 10,000-file/totalBytes fixture.
    private static void AssertMatchesRequestedFixtureSize(JObject packageResult, long totalBytes)
    {
        JObject totals = (JObject)packageResult["result"]!["totals"]!;
        int fileCount = (int)totals["fileCount"]!;
        long fileBytes = (long)totals["fileBytes"]!;

        if (fileCount != PerformanceFixtureGenerator.FileCount || fileBytes != totalBytes)
        {
            throw new InvalidOperationException(
                $"Golden package produced {fileCount} files / {fileBytes} bytes, expected "
                + $"{PerformanceFixtureGenerator.FileCount} files / {totalBytes} bytes - the fixture or config "
                + "silently dropped files.");
        }
    }

    private static string ManifestHash(JObject envelope) => (string)envelope["result"]!["identity"]!["manifestHash"]!;

    private static string DataVersion(JObject envelope) => (string)envelope["result"]!["identity"]!["dataVersion"]!;

    private static string WriteConfigWithInputRoot(string templateConfigPath, string newInputRoot, string destinationConfigPath)
    {
        string normalizedInputRoot = newInputRoot.Replace('\\', '/');

        using var writer = new StreamWriter(destinationConfigPath);

        foreach (string line in File.ReadAllLines(templateConfigPath))
        {
            writer.WriteLine(line.StartsWith("inputRoot:", StringComparison.Ordinal) ? $"inputRoot: {normalizedInputRoot}" : line);
        }

        return destinationConfigPath;
    }
}
