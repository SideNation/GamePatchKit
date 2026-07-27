using System.Diagnostics;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Cli;

// Per-stage wall-clock time, one of the PRD's observability numbers. Whole milliseconds because canonical
// JSON carries integers only, and because a sub-millisecond stage timing is noise either way.
internal sealed class StageTimings
{
    private readonly List<(string Stage, long Milliseconds)> _stages = new List<(string, long)>();

    public async Task<T> MeasureAsync<T>(string stage, Func<Task<T>> action)
    {
        long startedAt = Stopwatch.GetTimestamp();

        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            // Recorded even when the stage threw: knowing a package spent four minutes before failing is the
            // point of stage timings.
            _stages.Add((stage, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds));
        }
    }

    public JObject ToJson()
    {
        var durations = new JObject();

        foreach ((string stage, long milliseconds) in _stages)
        {
            durations[stage] = milliseconds;
        }

        return durations;
    }

    public IEnumerable<string> ToTextLines()
    {
        foreach ((string stage, long milliseconds) in _stages)
        {
            yield return $"  {stage}: {milliseconds.ToString(CultureInfo.InvariantCulture)} ms";
        }
    }
}
