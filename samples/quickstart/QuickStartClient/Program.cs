using GamePatchKit.DotNet;
using GamePatchKit.Runtime;

namespace QuickStartClient;

// gpk가 만든 publish tree에서 required group을 설치하고, 그 다음 optional group을
// 따로 설치하는 최소 client. run.sh가 이 프로그램에 인자를 넘겨 실행한다.
public static class Program
{
    private const string OptionalGroupName = "maps";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 5)
        {
            Console.Error.WriteLine(
                "usage: QuickStartClient <baseUrl> <runtimeRoot> <packageId> <dataVersion> <manifestHash>");
            return 1;
        }

        string baseUrl = args[0];
        string runtimeRoot = args[1];
        string packageId = args[2];
        string dataVersion = args[3];
        string manifestHash = args[4];

        // BaseAddress는 publish tree의 root이고 반드시 '/'로 끝나야 한다.
        // 끝의 '/'가 없으면 상대 경로를 붙일 때 마지막 segment가 잘린다.
        using var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var runtime = new PackageRuntime(
            new HttpArtifactTransport(httpClient),
            new FileSystemRuntimeStorage(runtimeRoot),
            DefaultCompressionCodecs.Create());

        // packageId·dataVersion·manifestHash는 Runtime이 스스로 고르지 않는다.
        // 신뢰하는 host나 서버가 "이 release로 가라"고 알려주는 값이다.
        var target = new TargetManifestReference(packageId, dataVersion, manifestHash);

        var progress = new Progress<PatchProgress>(ReportProgress);

        Console.WriteLine("== InstallOrUpdateAsync: required group만 설치 ==");
        PackageState state = await runtime.InstallOrUpdateAsync(target, progress);
        PrintState(state);

        Console.WriteLine();
        Console.WriteLine($"== InstallOptionalGroupsAsync: optional group '{OptionalGroupName}' 설치 ==");
        state = await runtime.InstallOptionalGroupsAsync(packageId, new[] { OptionalGroupName }, progress);
        PrintState(state);

        Console.WriteLine();
        Console.WriteLine("== 설치된 파일 ==");
        PrintInstalledFiles(runtimeRoot, packageId);

        return 0;
    }

    private static void ReportProgress(PatchProgress progress)
    {
        Console.WriteLine(
            $"  [{progress.Stage}] {progress.CompletedFiles}/{progress.TotalFiles} files, " +
            $"{progress.CompletedBytes}/{progress.TotalBytes} bytes, retries={progress.RetryCount}");
    }

    private static void PrintState(PackageState state)
    {
        Console.WriteLine($"  stateRevision : {state.StateRevision}");
        Console.WriteLine($"  active        : {state.Active.DataVersion}");

        foreach (PackageGroupState group in state.Groups)
        {
            Console.WriteLine($"  group '{group.Name}' : {group.Status}");
        }
    }

    // installationKey는 storage adapter만 해석하는 불투명 값이다. 여기서 디렉터리를
    // 직접 뒤지는 것은 샘플이 결과를 보여주기 위한 것이고, 실제 애플리케이션은
    // Runtime이 승격한 installation을 storage adapter를 통해 읽는다.
    private static void PrintInstalledFiles(string runtimeRoot, string packageId)
    {
        string installsRoot = Path.Combine(runtimeRoot, "packages", packageId, "installs");

        if (!Directory.Exists(installsRoot))
        {
            Console.WriteLine("  (없음)");
            return;
        }

        foreach (string path in Directory.EnumerateFiles(installsRoot, "*", SearchOption.AllDirectories).Order())
        {
            Console.WriteLine($"  {Path.GetRelativePath(installsRoot, path)}");
        }
    }
}
