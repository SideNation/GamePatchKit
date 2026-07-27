using Newtonsoft.Json.Linq;

namespace GamePatchKit.Cli;

// What a command produced, in both forms it can be reported in. Commands build the machine-readable object
// and the human lines from the same values so the two can never disagree about what happened.
internal sealed class CommandOutcome
{
    public JObject Result { get; }

    public IReadOnlyList<string> TextLines { get; }

    public CommandOutcome(JObject result, IReadOnlyList<string> textLines)
    {
        Result = result;
        TextLines = textLines;
    }
}
