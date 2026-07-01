using FlowLine.Domain.Entities;

namespace FlowLine.Application.Orders;

/// <summary>
/// Admin order/WorkItem management — PRD §7.2 (FR-6, FR-8). Manual WorkItem creation queues a
/// unit at a workflow's first stage; from there IRelayService's claim/hand-off takes over.
/// </summary>
public interface IOrderService
{
    /// <summary>
    /// Creates a WorkItem, Queued at the chosen workflow's first stage (by OrderIndex).
    /// Throws <see cref="OrderServiceException"/> if the workflow has no stages yet.
    /// </summary>
    Task<WorkItem> CreateWorkItemAsync(
        int workflowId, string orderNumber, string sku, int quantity, string? channel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// All WorkItems with Workflow, CurrentStage, and ClaimedByStation loaded, newest first —
    /// for the order overview (FR-8: queue and current position of every WorkItem).
    /// </summary>
    Task<List<WorkItem>> GetWorkItemsAsync(CancellationToken cancellationToken = default);
}
