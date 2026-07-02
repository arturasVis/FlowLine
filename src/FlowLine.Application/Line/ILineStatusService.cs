namespace FlowLine.Application.Line;

/// <summary>A unit currently being worked: what and where.</summary>
public record LineActiveUnit(int WorkItemId, string OrderNumber, string Sku, string StationName);

/// <summary>One stage's live picture: how many are waiting and what's on the bench.</summary>
public record LineStageStatus(
    int StageId,
    string StageName,
    int StationCount,
    int QueueDepth,
    IReadOnlyList<LineActiveUnit> Active);

/// <summary>One workflow's live picture across its stages, plus today's output.</summary>
public record LineWorkflowStatus(
    int WorkflowId,
    string WorkflowName,
    int CompletedToday,
    int UnitsOnLine,
    IReadOnlyList<LineStageStatus> Stages);

/// <summary>
/// The live line overview (wall-screen view): per active workflow, per stage — queue depth
/// and the units currently claimed, plus units completed today (server-local day). Pure
/// read; freshness comes from the caller re-querying on IRelayNotifier.StageChanged.
/// </summary>
public interface ILineStatusService
{
    Task<List<LineWorkflowStatus>> GetLineStatusAsync(CancellationToken cancellationToken = default);
}
