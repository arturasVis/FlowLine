namespace FlowLine.Application.Relay;

public class RelayNotifier : IRelayNotifier
{
    public event Action<int>? StageChanged;

    public void NotifyStageChanged(int stageId) => StageChanged?.Invoke(stageId);
}
