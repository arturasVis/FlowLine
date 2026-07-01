namespace FlowLine.Application.Relay;

/// <summary>
/// In-process notification that a stage's queue changed (a claim or a hand-off landed a
/// WorkItem there). Station screens subscribe to push live updates without polling — this
/// is the mechanism behind PRD FR-16/NFR-9. It's deliberately not a literal SignalR Hub:
/// Blazor Server already holds one SignalR circuit per browser, so a process-wide C# event
/// that a component handler turns into <c>StateHasChanged</c> achieves the same live update
/// without a second real-time transport. Registered as a singleton — it must outlive any one
/// circuit/scope to fan out across stations on different browsers.
/// </summary>
public interface IRelayNotifier
{
    event Action<int>? StageChanged;

    void NotifyStageChanged(int stageId);
}
