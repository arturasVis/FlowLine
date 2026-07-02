namespace FlowLine.Application.Stats;

/// <summary>
/// Aggregated production statistics for the stats dashboards (workflow picker → workflow
/// dashboard → per-operator drill-down). All numbers are computed from *completed* units
/// only: a WorkItem counts when its Status is Completed and its completion moment (the
/// latest StepCompletion timestamp) falls inside the requested UTC range. Units still on
/// the line are invisible here until they finish.
/// </summary>
public interface IStatsService
{
    /// <summary>Every workflow (active first) with its headline numbers for the range.</summary>
    Task<List<WorkflowStatsSummary>> GetWorkflowSummariesAsync(
        DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken = default);

    /// <summary>Full dashboard for one workflow, or null if the workflow doesn't exist.</summary>
    Task<WorkflowStats?> GetWorkflowStatsAsync(
        int workflowId, DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// One operator's numbers on one workflow, with per-step comparison against the
    /// workflow-wide averages over the same range. Null if the workflow doesn't exist;
    /// an operator with no completions in range comes back with zero counts.
    /// </summary>
    Task<StaffStats?> GetStaffStatsAsync(
        int workflowId, int staffNumber, DateTime? fromUtc, DateTime? toUtc,
        CancellationToken cancellationToken = default);
}
