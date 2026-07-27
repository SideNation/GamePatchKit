using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GamePatchKit.PerformanceTests.Support;

// Every scenario this project measures is a real dotnet assembly (gpk itself, or the Runtime install harness)
// launched as its own process - both because peak RSS only means anything for an isolated process, and
// because that is what the PRD's blocking gate asks for.
internal static class ChildProcess
{
    public static string CliDllPath => Path.Combine(AppContext.BaseDirectory, "GamePatchKit.Cli.dll");

    public static string HarnessDllPath => Path.Combine(AppContext.BaseDirectory, "GamePatchKit.PerformanceTests.Harness.dll");

    public sealed record Result(int ExitCode, string StandardOutput, string StandardError);

    // Runs the given dotnet assembly directly (no /usr/bin/time wrapper) for correctness-only checks where
    // peak RSS is not being measured.
    public static async Task<Result> RunAsync(string dllPath, IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        var startInfo = BuildStartInfo(dllPath, args);
        return await RunAsync(startInfo, cancellationToken).ConfigureAwait(false);
    }

    public static ProcessStartInfo BuildStartInfo(string dllPath, IReadOnlyList<string> args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(dllPath);

        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        return startInfo;
    }

    public static async Task<Result> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
    {
        using var process = new Process { StartInfo = startInfo };
        process.Start();

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);
        return new Result(process.ExitCode, stdout, stderr);
    }

    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public static bool IsMacOs => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
}
