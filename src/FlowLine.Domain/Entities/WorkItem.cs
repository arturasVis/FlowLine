namespace FlowLine.Domain.Entities;

public class WorkItem : IConcurrencyAware
{
    public int Id { get; set; }

    public int WorkflowId { get; set; }
    public Workflow Workflow { get; set; } = null!;

    public string OrderNumber { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string? Channel { get; set; }

    /// <summary>
    /// Set once at creation, never updated. Unlike QueuedAtUtc (overwritten on every
    /// hand-off), this is the only durable record of when a WorkItem entered stage 1 —
    /// needed to compute stage 1's dwell time for the timing review (PRD FR-18, M5).
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public int CurrentStageId { get; set; }
    public Stage CurrentStage { get; set; } = null!;
    public WorkItemStatus Status { get; set; } = WorkItemStatus.Queued;

    /// <summary>
    /// When this WorkItem became Queued at its CurrentStage — set at creation and again on
    /// every hand-off. Determines FIFO order within a stage's queue (PRD FR-13); Id alone
    /// can't be used since it reflects creation order, not arrival at the *current* stage.
    /// DateTime, not DateTimeOffset: SQLite's EF Core provider can't translate ORDER BY on
    /// DateTimeOffset columns. Always UTC.
    /// </summary>
    public DateTime QueuedAtUtc { get; set; } = DateTime.UtcNow;

    public int? ClaimedByStationId { get; set; }
    public Station? ClaimedByStation { get; set; }

    /// <summary>
    /// When the current claim was taken — set on claim (or prebuild scan), cleared on
    /// hand-off/completion. Anchors the StartedAtUtc of each stage's *first* StepCompletion,
    /// which otherwise has no start marker (QueuedAtUtc includes queue wait). Always UTC.
    /// </summary>
    public DateTime? ClaimedAtUtc { get; set; }

    public Guid RowVersion { get; set; } = Guid.NewGuid();

    public List<StepCompletion> StepCompletions { get; set; } = [];
}
