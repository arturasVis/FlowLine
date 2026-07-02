namespace FlowLine.Web;

/// <summary>Display formatting shared by the stats pages.</summary>
public static class StatsFormatting
{
    /// <summary>"1h 03m", "4m 05s", "42s" — or "—" when there's no timed data.</summary>
    public static string Duration(TimeSpan? duration)
    {
        if (duration is null)
        {
            return "—";
        }

        var d = duration.Value;
        if (d.TotalHours >= 1)
        {
            return $"{(int)d.TotalHours}h {d.Minutes:00}m";
        }
        return d.TotalMinutes >= 1 ? $"{d.Minutes}m {d.Seconds:00}s" : $"{Math.Round(d.TotalSeconds)}s";
    }

    /// <summary>Delta vs. average as "12% slower" / "8% faster" / "on average" / "—".</summary>
    public static string Delta(double? deltaPercent)
    {
        if (deltaPercent is null)
        {
            return "—";
        }

        var rounded = Math.Round(Math.Abs(deltaPercent.Value));
        return rounded == 0
            ? "on average"
            : $"{rounded:0}% {(deltaPercent.Value > 0 ? "slower" : "faster")}";
    }

    /// <summary>CSS class for a delta: slower is bad (red), faster is good (green).</summary>
    public static string DeltaClass(double? deltaPercent) => deltaPercent switch
    {
        null => "delta-none",
        > 0.5 => "delta-slower",
        < -0.5 => "delta-faster",
        _ => "delta-even",
    };
}
