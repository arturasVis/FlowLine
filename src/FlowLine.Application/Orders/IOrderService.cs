using FlowLine.Domain.Entities;

namespace FlowLine.Application.Orders;

/// <summary>
/// Admin order/WorkItem management — PRD §7.2 (FR-6, FR-8). Manual WorkItem creation queues a
/// unit at a workflow's first stage; from there IRelayService's claim/hand-off takes over.
/// </summary>
public interface IOrderService
{
    /// <summary>
    /// Creates <paramref name="quantity"/> WorkItems (one physical unit each, Quantity = 1) for the
    /// order, all Queued at the chosen workflow's first stage (by OrderIndex) and sharing the order
    /// number so the Orders screen groups them. Returns the created units. Throws
    /// <see cref="OrderServiceException"/> if the workflow has no stages yet.
    /// </summary>
    Task<List<WorkItem>> CreateWorkItemAsync(
        int workflowId, string orderNumber, string sku, int quantity, string? channel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// All WorkItems with Workflow, CurrentStage, and ClaimedByStation loaded, newest first —
    /// for the order overview (FR-8: queue and current position of every WorkItem).
    /// </summary>
    Task<List<WorkItem>> GetWorkItemsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// One WorkItem with its completed steps and the operator's captured input values loaded
    /// (each value's <see cref="StepInput"/> label included) — for the per-unit detail view.
    /// </summary>
    Task<WorkItem?> GetWorkItemDetailAsync(int workItemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Voids a Queued or InProgress WorkItem (admin action, e.g. created by mistake): status
    /// becomes Cancelled, any claim is cleared, step history is kept. Terminal — the order
    /// number becomes importable/scannable again. Throws <see cref="OrderServiceException"/>
    /// for Completed/Cancelled/Scrapped items.
    /// </summary>
    Task CancelWorkItemAsync(int workItemId, CancellationToken cancellationToken = default);
}
