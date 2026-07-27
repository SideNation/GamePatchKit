using GamePatchKit.PerformanceTests.Fixtures;
using GamePatchKit.PerformanceTests.Scenarios;
using GamePatchKit.PerformanceTests.Support;

namespace GamePatchKit.PerformanceTests.Determinism;

// PRD 12단계의 "병렬 처리가 파일 순서, bundle 경계와 manifest byte를 바꾸지 않는다" 요구사항을 검증하려던
// 자리다. 하지만 src/GamePatchKit.Packager/FilePackageBuilder.cs를 확인한 결과 packaging 파이프라인은
// 현재 전부 순차 처리(foreach + await)이고 Task.WhenAll·Parallel.ForEach나 조절 가능한 병렬도 옵션이
// 어디에도 없다 - 즉 지금은 바꿔볼 "병렬도" 자체가 없다. 그래서 이 테스트는 대신 검증 기준 1("같은 입력과
// 설정으로 두 번 package하면 artifact와 manifest byte가 같다")을 1만 파일 규모에서 재확인한다 - 기존
// 단위 테스트들은 파일 몇 개 규모에서만 이를 확인했다. 병렬도 옵션이 추가되면 그때 이 테스트를 확장해서
// 실제 병렬 처리 결정성을 검증해야 한다.
[Trait("Category", "Performance")]
public class TestDeterminismAtScale : IDisposable
{
    private readonly List<string> _scratchDirectories = new List<string>();

    public void Dispose()
    {
        foreach (string directory in _scratchDirectories)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Package_SameTenThousandFileFixtureTwice_ProducesByteIdenticalOutput()
    {
        PerformanceFixtureGenerator.FixtureLayout fixture = PerformanceFixtureGenerator.Ensure(
            PerformanceFixturePaths.SharedFixturesRoot, ScenarioScale.SmokeTotalBytes, "smoke");

        string outputRootA = NewScratchDirectory();
        string outputRootB = NewScratchDirectory();

        await RunPackageAsync(fixture.ConfigPath, outputRootA);
        await RunPackageAsync(fixture.ConfigPath, outputRootB);

        AssertDirectoriesByteIdentical(outputRootA, outputRootB);
    }

    private static async Task RunPackageAsync(string configPath, string outputRoot)
    {
        ChildProcess.Result result = await ChildProcess.RunAsync(
            ChildProcess.CliDllPath,
            new[] { "package", "--config", configPath, "--output-root", outputRoot, "--json" });

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"gpk package failed (exit {result.ExitCode}): {result.StandardError}");
        }
    }

    private static void AssertDirectoriesByteIdentical(string left, string right)
    {
        string[] leftRelativePaths = RelativeFilePaths(left);
        string[] rightRelativePaths = RelativeFilePaths(right);
        Assert.Equal(leftRelativePaths, rightRelativePaths);

        foreach (string relativePath in leftRelativePaths)
        {
            byte[] leftBytes = File.ReadAllBytes(Path.Combine(left, relativePath));
            byte[] rightBytes = File.ReadAllBytes(Path.Combine(right, relativePath));
            Assert.True(leftBytes.AsSpan().SequenceEqual(rightBytes), $"'{relativePath}' differs between two package runs of the same input.");
        }
    }

    private static string[] RelativeFilePaths(string root)
    {
        return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private string NewScratchDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "gpk-perf-determinism-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _scratchDirectories.Add(path);
        return path;
    }
}
