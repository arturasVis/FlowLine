namespace FlowLine.Domain.Entities;

public class StepCompletion
{
    public int Id { get; set; }

    public int WorkItemId { get; set; }
    public WorkItem WorkItem { get; set; } = null!;

    public int StepId { get; set; }
    public Step Step { get; set; } = null!;

    /// <summary>Which station the step was completed at.</summary>
    public int? StationId { get; set; }
    public Station? Station { get; set; }

    /// <summary>
    /// Staff number of the operator signed in at the station when the step was completed.
    /// A plain int, deliberately not an FK — StaffTable is company-owned/external, same as
    /// WorkflowAssignment.StaffNumber. Null on rows written before this was tracked.
    /// </summary>
    public int? CompletedByStaffNumber { get; set; }

    /// <summary>
    /// When work on this step actually began: the previous StepCompletion at the same stage,
    /// or the WorkItem's ClaimedAtUtc for the stage's first step — so CompletedAtUtc minus
    /// this is the step's true duration, excluding queue wait. Null on rows written before
    /// this was tracked.
    /// </summary>
    public DateTime? StartedAtUtc { get; set; }

    // DateTime, not DateTimeOffset: SQLite's EF Core provider can't translate ORDER BY
    // on DateTimeOffset columns (needed for timing-review queries, PRD M5). Always UTC.
    public DateTime CompletedAtUtc { get; set; }
}
