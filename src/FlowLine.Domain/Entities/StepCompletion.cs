namespace FlowLine.Domain.Entities;

public class StepCompletion
{
    public int Id { get; set; }

    public int WorkItemId { get; set; }
    public WorkItem WorkItem { get; set; } = null!;

    public int StepId { get; set; }
    public Step Step { get; set; } = null!;

    /// <summary>Which station the step was completed at. Operator identity is not tracked (see PRD §11.2).</summary>
    public int? StationId { get; set; }
    public Station? Station { get; set; }

    // DateTime, not DateTimeOffset: SQLite's EF Core provider can't translate ORDER BY
    // on DateTimeOffset columns (needed for timing-review queries, PRD M5). Always UTC.
    public DateTime CompletedAtUtc { get; set; }
}
