namespace FlowLine.Web;

/// <summary>
/// The stats pages carry their date filter as `from`/`to` query params (yyyy-MM-dd, local
/// calendar days) so the range survives drill-down navigation. These helpers parse the
/// params and convert the inclusive local-day range into the UTC instant range
/// [StartUtc, EndUtcExclusive) that IStatsService filters on.
/// </summary>
public static class StatsDateRange
{
    public static DateOnly? Parse(string? value) =>
        DateOnly.TryParse(value, out var date) ? date : null;

    public static DateTime? StartUtc(DateOnly? from) =>
        from?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();

    public static DateTime? EndUtcExclusive(DateOnly? to) =>
        to?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();

    /// <summary>Query suffix ("?from=…&to=…", or "") for links that should keep the current range.</summary>
    public static string QuerySuffix(DateOnly? from, DateOnly? to)
    {
        var parts = new List<string>(2);
        if (from is DateOnly f)
        {
            parts.Add($"from={f:yyyy-MM-dd}");
        }
        if (to is DateOnly t)
        {
            parts.Add($"to={t:yyyy-MM-dd}");
        }
        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }
}
