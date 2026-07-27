using System.Linq;
using GamePatchKit.DotNet;
using GamePatchKit.Runtime;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.PerformanceTests.Harness;

// Standalone process that drives one real Runtime InstallOrUpdateAsync call against a real loopback HTTP
// socket, so the performance harness can measure this scenario's peak RSS the same way it measures the CLI's:
// by wrapping a subprocess in /usr/bin/time. Kept out of GamePatchKit.Cli (the shipped `gpk` tool) so the
// Runtime/DotNet dependency this needs never reaches the packaged product.
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);
            JObject result = await RunAsync(options).ConfigureAwait(false);
            Console.WriteLine(result.ToString(Newtonsoft.Json.Formatting.None));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task<JObject> RunAsync(Options options)
    {
        using var listener = new StaticFileHttpListener(options.PublishRoot);
        string baseAddress = listener.Start();

        using var httpClient = new HttpClient { BaseAddress = new Uri(baseAddress) };
        var storage = new FileSystemRuntimeStorage(options.RuntimeRoot);

        TrustedSigningKeys? trustedSigningKeys = options.TrustedKeys.Count == 0
            ? null
            : new TrustedSigningKeys(options.TrustedKeys.Select(DecodeBase64Url));

        var runtime = new PackageRuntime(
            new HttpArtifactTransport(httpClient),
            storage,
            DefaultCompressionCodecs.Create(),
            trustedSigningKeys,
            options.RequireSignature);

        // System.Progress<T> posts each report through the thread pool rather than invoking it inline, because a
        // console app has no captured SynchronizationContext - so InstallOrUpdateAsync could return before every
        // queued report has actually run, silently dropping some from progressReports. SynchronousProgress below
        // invokes the handler directly on the reporting thread instead, so nothing is still in flight once
        // InstallOrUpdateAsync returns.
        var progressReports = new List<PatchProgress>();
        var progressGate = new object();
        var progress = new SynchronousProgress<PatchProgress>(report =>
        {
            lock (progressGate)
            {
                progressReports.Add(report);
            }
        });

        var target = new TargetManifestReference(options.PackageId, options.DataVersion, options.ManifestHash);
        PackageState state = await runtime.InstallOrUpdateAsync(target, progress).ConfigureAwait(false);

        await listener.StopAsync().ConfigureAwait(false);

        return BuildResult(state, progressReports);
    }

    // PRD's 성능·관측 기준 observability list applied to a Runtime result: the released identity this install
    // actually landed on, each group's status, and the progress PackageRuntime reported while getting there -
    // the Runtime-side counterpart to the CLI's own --json output.
    private static JObject BuildResult(PackageState state, IReadOnlyList<PatchProgress> progressReports)
    {
        PatchProgress? last = progressReports.Count == 0 ? null : progressReports[^1];

        // PackageRuntime always reports its final PatchProgress as `new PatchProgress(PatchStage.Completed)`
        // with every other constructor parameter left at its 0/null default - so `last` above is real for
        // lastStage (Completed genuinely is the final stage) but useless for file/byte totals. Only the
        // Downloading/Staging-stage reports that ProgressTracker.Downloaded/FileCompleted produce ever carry
        // real totals, so the latest report with a nonzero total is what the file/byte fields below use.
        PatchProgress? lastWithTotals = progressReports.LastOrDefault(report => report.TotalFiles > 0 || report.TotalBytes > 0);

        // PatchProgress.RetryCount is per-object (PackageRuntime.DownloadObjectAsync resets its local attempt
        // counter to 0 for every artifact), so Max() across all reports would only surface the single object
        // that needed the most attempts and silently drop every other object's retries. Counting distinct
        // (RelativePath, RetryCount) pairs above zero counts each object's retry level once, which sums to the
        // real total number of retries across the whole operation.
        int retryEventCount = progressReports
            .Where(report => report.RetryCount > 0 && report.RelativePath != null)
            .Select(report => (report.RelativePath, report.RetryCount))
            .Distinct()
            .Count();

        return new JObject
        {
            ["packageId"] = state.PackageId,
            ["active"] = new JObject
            {
                ["dataVersion"] = state.Active.DataVersion,
                ["manifestHash"] = state.Active.ManifestHash,
            },
            ["groups"] = new JArray(state.Groups.Select(group => new JObject
            {
                ["name"] = group.Name,
                ["status"] = group.Status.ToString(),
                ["verifiedManifestHash"] = group.VerifiedManifestHash,
                ["installationKey"] = group.InstallationKey,
            }).Cast<object>().ToArray()),
            ["progress"] = new JObject
            {
                ["reportCount"] = progressReports.Count,
                ["retryCount"] = retryEventCount,
                ["lastStage"] = last?.Stage.ToString(),
                ["lastCompletedFiles"] = lastWithTotals?.CompletedFiles ?? 0,
                ["lastTotalFiles"] = lastWithTotals?.TotalFiles ?? 0,
                ["lastCompletedBytes"] = lastWithTotals?.CompletedBytes ?? 0,
                ["lastTotalBytes"] = lastWithTotals?.TotalBytes ?? 0,
            },
        };
    }

    private static byte[] DecodeBase64Url(string value)
    {
        string base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - (base64.Length % 4)) % 4);
        return Convert.FromBase64String(base64);
    }

    // Unlike System.Progress<T>, Report() runs the handler synchronously on the calling thread instead of
    // posting it to a SynchronizationContext/thread pool - required here so every report has been handled by
    // the time InstallOrUpdateAsync returns.
    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public SynchronousProgress(Action<T> handler)
        {
            _handler = handler;
        }

        public void Report(T value)
        {
            _handler(value);
        }
    }

    private sealed class Options
    {
        public required string PublishRoot { get; init; }

        public required string RuntimeRoot { get; init; }

        public required string PackageId { get; init; }

        public required string DataVersion { get; init; }

        public required string ManifestHash { get; init; }

        public IReadOnlyList<string> TrustedKeys { get; init; } = Array.Empty<string>();

        public bool RequireSignature { get; init; }

        public static Options Parse(string[] args)
        {
            string? publishRoot = null;
            string? runtimeRoot = null;
            string? packageId = null;
            string? dataVersion = null;
            string? manifestHash = null;
            var trustedKeys = new List<string>();
            bool requireSignature = false;

            for (int index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--publish-root":
                        publishRoot = args[++index];
                        break;
                    case "--runtime-root":
                        runtimeRoot = args[++index];
                        break;
                    case "--package-id":
                        packageId = args[++index];
                        break;
                    case "--data-version":
                        dataVersion = args[++index];
                        break;
                    case "--manifest-hash":
                        manifestHash = args[++index];
                        break;
                    case "--trusted-key":
                        trustedKeys.Add(args[++index]);
                        break;
                    case "--require-signature":
                        requireSignature = true;
                        break;
                    default:
                        throw new ArgumentException($"Unrecognized argument '{args[index]}'.");
                }
            }

            return new Options
            {
                PublishRoot = publishRoot ?? throw new ArgumentException("--publish-root is required."),
                RuntimeRoot = runtimeRoot ?? throw new ArgumentException("--runtime-root is required."),
                PackageId = packageId ?? throw new ArgumentException("--package-id is required."),
                DataVersion = dataVersion ?? throw new ArgumentException("--data-version is required."),
                ManifestHash = manifestHash ?? throw new ArgumentException("--manifest-hash is required."),
                TrustedKeys = trustedKeys,
                RequireSignature = requireSignature,
            };
        }
    }
}
