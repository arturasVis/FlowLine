namespace FlowLine.Application.Timing;

/// <summary>
/// Timing review (PRD §7.4, FR-17/FR-18, M5). FR-17 (persisting the timestamps) is already
/// satisfied by StepCompletion — this is purely the read side: turning that relational
/// timing data into a per-stage duration breakdown per completed order. No charts, per the
/// PRD ("No charts required for the prototype") — just the numbers.
/// </summary>
public interface ITimingService
{
    /// <summary>Every Completed WorkItem with its per-stage dwell time, newest first.</summary>
    Task<List<CompletedOrderTiming>> GetCompletedOrderTimingsAsync(CancellationToken cancellationToken = default);
}
