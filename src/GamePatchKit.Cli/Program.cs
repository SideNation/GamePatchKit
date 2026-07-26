using System.Text;
using GamePatchKit.Cli.Commands;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;
using GamePatchKit.Packager;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Cli;

public static class Program
{
    private const string JsonOption = "--json";
    private const string DryRunOption = "--dry-run";

    private static readonly string _usage = string.Join(
        Environment.NewLine,
        "gpk <command> [options]",
        "",
        "Commands:",
        "  package        Create the first or an incremental release",
        "  diff           Report logical file and physical artifact differences between two releases",
        "  verify         Verify a published release: schema, references, artifact bytes, signature document",
        "  compact        Rebuild selected bundle groups as a new baseline",
        "  plan-download  Compute what a local state needs to reach a target release",
        "  sign           Sign a published canonical manifest",
        "",
        "Common options:",
        "  --json         Emit a machine-readable result on stdout",
        "  --dry-run      Compute and report without creating any file (package, compact, sign)",
        "",
        "Exit codes: 0 success, 1 input error, 2 integrity error, 3 execution failure");

    public static async Task<int> Main(string[] args)
    {
        return await RunAsync(args, Console.Out, Console.Error, CancellationToken.None).ConfigureAwait(false);
    }

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        // Read straight off the raw arguments: the reporting format has to be settled before parsing, because
        // a parse failure is exactly the case a CI job most needs to be able to read.
        bool json = args.Contains(JsonOption);
        bool dryRun = args.Contains(DryRunOption);
        string command = args.Count > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? args[0] : string.Empty;

        try
        {
            if (args.Count == 0 || args.Contains("--help") || command == "help")
            {
                output.WriteLine(_usage);
                return ExitCode.Success;
            }

            CommandOutcome outcome = await DispatchAsync(CliArguments.Parse(args), cancellationToken).ConfigureAwait(false);
            WriteSuccess(output, command, dryRun, json, outcome);
            return ExitCode.Success;
        }
        catch (CliException exception)
        {
            return WriteFailure(output, error, command, dryRun, json, exception.Errors);
        }
        catch (PackageException exception)
        {
            return WriteFailure(output, error, command, dryRun, json, exception.Errors);
        }
        catch (ArgumentException exception)
        {
            // Core and Packager throw this for precondition violations - an undeclared group, a diff across
            // packages - which from the command line always means the caller asked for something invalid.
            return WriteFailure(output, error, command, dryRun, json, Single(CliErrorCodes.InvalidArguments, exception.Message));
        }
        catch (OperationCanceledException)
        {
            return WriteFailure(output, error, command, dryRun, json, Single(CliErrorCodes.Cancelled, "The command was cancelled."));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return WriteFailure(output, error, command, dryRun, json, Single(CliErrorCodes.ExecutionFailed, exception.Message));
        }
        catch (Exception exception)
        {
            // Last resort, so that every outcome lands on one of the four documented exit codes. A CI job that
            // gets a stack trace and exit 134 cannot tell "this input was bad" from "this tool broke"; an
            // unexpected failure is an execution failure and says so in the same envelope as any other.
            return WriteFailure(output, error, command, dryRun, json, Single(CliErrorCodes.ExecutionFailed, exception.Message));
        }
    }

    private static Task<CommandOutcome> DispatchAsync(CliArguments args, CancellationToken cancellationToken)
    {
        return args.Command switch
        {
            "package" => PackageCommand.RunAsync(args, cancellationToken),
            "diff" => DiffCommand.RunAsync(args, cancellationToken),
            "verify" => VerifyCommand.RunAsync(args, cancellationToken),
            "compact" => CompactCommand.RunAsync(args, cancellationToken),
            "plan-download" => PlanDownloadCommand.RunAsync(args, cancellationToken),
            "sign" => SignCommand.RunAsync(args, cancellationToken),
            _ => throw new CliException(CliErrorCodes.InvalidArguments, $"Unknown command '{args.Command}'."),
        };
    }

    private static void WriteSuccess(TextWriter output, string command, bool dryRun, bool json, CommandOutcome outcome)
    {
        if (json)
        {
            WriteJson(output, Envelope(command, dryRun, ExitCode.Success, outcome.Result, errors: null));
            return;
        }

        foreach (string line in outcome.TextLines)
        {
            output.WriteLine(line);
        }
    }

    private static int WriteFailure(
        TextWriter output,
        TextWriter error,
        string command,
        bool dryRun,
        bool json,
        IReadOnlyList<GamePatchKitError> errors)
    {
        int exitCode = ExitCode.Classify(errors);

        if (json)
        {
            WriteJson(output, Envelope(command, dryRun, exitCode, result: null, errors));
            return exitCode;
        }

        foreach (GamePatchKitError item in errors)
        {
            error.WriteLine(FormatError(item));
        }

        return exitCode;
    }

    private static JObject Envelope(string command, bool dryRun, int exitCode, JObject? result, IReadOnlyList<GamePatchKitError>? errors)
    {
        var envelope = new JObject
        {
            ["command"] = command,
            ["ok"] = exitCode == ExitCode.Success,
            ["exitCode"] = exitCode,
            ["dryRun"] = dryRun,
        };

        if (result != null)
        {
            envelope["result"] = result;
        }

        if (errors != null)
        {
            envelope["errors"] = new JArray(errors.Select(ToJson).Cast<object>().ToArray());
        }

        return envelope;
    }

    private static JObject ToJson(GamePatchKitError error)
    {
        var json = new JObject
        {
            ["stage"] = error.Stage,
            ["code"] = error.Code,
            ["message"] = error.Message,
        };

        if (error.PackageId != null)
        {
            json["packageId"] = error.PackageId;
        }

        if (error.RelativePath != null)
        {
            json["path"] = error.RelativePath;
        }

        if (error.Group != null)
        {
            json["group"] = error.Group;
        }

        return json;
    }

    private static string FormatError(GamePatchKitError error)
    {
        string scope = error.RelativePath == null ? string.Empty : $" ({error.RelativePath})";
        return $"error: [{error.Stage}/{error.Code}] {error.Message}{scope}";
    }

    // Canonical JSON: keys sorted, no insignificant whitespace, UTF-8. The same writer the manifests use, so
    // the machine-readable output is stable byte-for-byte wherever the values themselves did not change.
    private static void WriteJson(TextWriter output, JObject envelope)
    {
        output.WriteLine(Encoding.UTF8.GetString(CanonicalJsonWriter.Write(envelope)));
    }

    private static GamePatchKitError[] Single(string code, string message)
    {
        return new[] { new GamePatchKitError("cli", code, message) };
    }
}
