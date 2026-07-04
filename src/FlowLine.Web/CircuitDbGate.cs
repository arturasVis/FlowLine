namespace FlowLine.Web;

/// <summary>
/// Serializes DbContext-touching work within a single Blazor circuit/component. The scoped
/// <c>FlowLineDbContext</c> is shared between user actions and the fire-and-forget refresh that
/// <c>IRelayNotifier.StageChanged</c> kicks off; on a higher-latency provider (SQL Server) those
/// two can overlap and EF throws "a second operation was started on this context instance". A
/// component that both subscribes to the notifier and issues DbContext work owns one of these and
/// routes every such sequence through <see cref="RunAsync"/>, so at most one runs at a time.
/// </summary>
public sealed class CircuitDbGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task RunAsync(Func<Task> work)
    {
        await _gate.WaitAsync();
        try
        {
            await work();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
