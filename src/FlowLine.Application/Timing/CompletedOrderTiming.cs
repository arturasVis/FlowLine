namespace FlowLine.Application.Timing;

public record StageDuration(string StageName, int StageOrderIndex, TimeSpan Duration);

public record CompletedOrderTiming(
    int WorkItemId,
    string OrderNumber,
    string Sku,
    string WorkflowName,
    DateTime CreatedAtUtc,
    DateTime CompletedAtUtc,
    TimeSpan TotalDuration,
    IReadOnlyList<StageDuration> StageDurations);
