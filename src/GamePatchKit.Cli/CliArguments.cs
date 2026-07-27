namespace GamePatchKit.Cli;

// Minimal option parsing: one command word, then '--name value', '--name=value' and bare '--flag'. Repeating
// an option accumulates values, which is how --group and --retained-manifest take a set.
//
// Hand-written rather than pulled from a library because the surface is this small and the failure messages
// are part of the CLI's contract with CI.
internal sealed class CliArguments
{
    private readonly Dictionary<string, List<string?>> _options;
    private readonly HashSet<string> _read = new HashSet<string>(StringComparer.Ordinal);

    public string Command { get; }

    private CliArguments(string command, Dictionary<string, List<string?>> options)
    {
        Command = command;
        _options = options;
    }

    public static CliArguments Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            throw new CliException(CliErrorCodes.InvalidArguments, "Expected a command as the first argument.");
        }

        var options = new Dictionary<string, List<string?>>(StringComparer.Ordinal);

        for (int index = 1; index < args.Count; index++)
        {
            string token = args[index];

            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                throw new CliException(
                    CliErrorCodes.InvalidArguments,
                    $"Unexpected positional argument '{token}'; every option is '--name value'.");
            }

            string name;
            string? value;
            int separator = token.IndexOf('=', StringComparison.Ordinal);

            if (separator >= 0)
            {
                name = token[2..separator];
                value = token[(separator + 1)..];
            }
            else
            {
                name = token[2..];

                // A following token that starts with '--' is the next option, not this one's value: that is
                // what makes bare flags work without a table of which names take values.
                bool hasValue = index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal);
                value = hasValue ? args[++index] : null;
            }

            if (name.Length == 0)
            {
                throw new CliException(CliErrorCodes.InvalidArguments, "An option name must not be empty.");
            }

            if (!options.TryGetValue(name, out List<string?>? values))
            {
                values = new List<string?>();
                options[name] = values;
            }

            values.Add(value);
        }

        return new CliArguments(args[0], options);
    }

    public string GetRequired(string name)
    {
        return GetOptional(name)
            ?? throw new CliException(CliErrorCodes.InvalidArguments, $"Option '--{name}' is required.");
    }

    public string? GetOptional(string name)
    {
        IReadOnlyList<string> values = GetAll(name);

        if (values.Count > 1)
        {
            throw new CliException(CliErrorCodes.InvalidArguments, $"Option '--{name}' was given more than once.");
        }

        return values.Count == 0 ? null : values[0];
    }

    public IReadOnlyList<string> GetAll(string name)
    {
        _read.Add(name);

        if (!_options.TryGetValue(name, out List<string?>? values))
        {
            return Array.Empty<string>();
        }

        if (values.Any(value => value == null))
        {
            throw new CliException(CliErrorCodes.InvalidArguments, $"Option '--{name}' needs a value.");
        }

        return values!.ToArray()!;
    }

    public bool GetFlag(string name)
    {
        _read.Add(name);

        if (!_options.TryGetValue(name, out List<string?>? values))
        {
            return false;
        }

        if (values.Count > 1 || values[0] != null)
        {
            throw new CliException(CliErrorCodes.InvalidArguments, $"Option '--{name}' is a flag and takes no value.");
        }

        return true;
    }

    // Called once a command has asked for everything it knows about: anything left over is a typo, and
    // silently ignoring it is how a CI job ends up believing it passed --dry-run when it did not.
    public void EnsureNoUnreadOptions()
    {
        string[] unread = _options.Keys
            .Where(name => !_read.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (unread.Length > 0)
        {
            throw new CliException(
                CliErrorCodes.InvalidArguments,
                $"Unknown option '--{unread[0]}' for command '{Command}'.");
        }
    }
}
