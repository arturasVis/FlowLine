using FlowLine.Application.Relay;
using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Application.Orders;

public class OrderService(FlowLineDbContext db, IRelayNotifier notifier) : IOrderService
{
    public async Task CancelWorkItemAsync(int workItemId, CancellationToken cancellationToken = default)
    {
        var workItem = await db.WorkItems.FindAsync([workItemId], cancellationToken)
            ?? throw new OrderServiceException($"WorkItem {workItemId} does not exist.");
        if (workItem.Status is not (WorkItemStatus.Queued or WorkItemStatus.InProgress))
        {
            throw new OrderServiceException(
                $"Only queued or in-progress orders can be cancelled (status: {workItem.Status}).");
        }

        workItem.Status = WorkItemStatus.Cancelled;
        workItem.ClaimedByStationId = null;
        workItem.ClaimedAtUtc = null;
        await db.SaveChangesAsync(cancellationToken);

        // A station holding (or queued behind) this unit should move on without a manual refresh.
        notifier.NotifyStageChanged(workItem.CurrentStageId);
    }

    public async Task<List<WorkItem>> CreateWorkItemAsync(
        int workflowId, string orderNumber, string sku, int quantity, string? channel,
        CancellationToken cancellationToken = default)
    {
        // Quantity is the *number of physical units* to build for this order — each becomes its own
        // WorkItem (Quantity = 1) so it flows through the line independently, sharing the order number
        // so the Orders screen can group them. (Historically Quantity sat unused on a single WorkItem.)
        var units = quantity < 1 ? 1 : quantity;

        var firstStage = await db.Stages
            .Where(s => s.WorkflowId == workflowId)
            .OrderBy(s => s.OrderIndex)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new OrderServiceException(
                "This workflow has no stages yet — add one in the workflow builder before creating orders.");

        // One shared timestamp so the units order together and read as a single batch.
        var now = DateTime.UtcNow;
        var workItems = new List<WorkItem>(units);
        for (var i = 0; i < units; i++)
        {
            workItems.Add(new WorkItem
            {
                WorkflowId = workflowId,
                CurrentStageId = firstStage.Id,
                OrderNumber = orderNumber,
                Sku = sku,
                Quantity = 1,
                Channel = channel,
                Status = WorkItemStatus.Queued,
                QueuedAtUtc = now,
            });
        }

        db.WorkItems.AddRange(workItems);
        await db.SaveChangesAsync(cancellationToken);
        return workItems;
    }

    public Task<List<WorkItem>> GetWorkItemsAsync(CancellationToken cancellationToken = default)
    {
        // AsNoTracking: this is a read-only display list, and (crucially) the circuit-scoped
        // DbContext lives for the whole page — a tracked re-query would return stale values from
        // the identity map, so the live-refresh (see Orders.razor) wouldn't reflect status/stage
        // changes. AsNoTracking always materialises fresh rows from the database.
        return db.WorkItems
            .AsNoTracking()
            .Include(wi => wi.Workflow)
            .Include(wi => wi.CurrentStage)
            .Include(wi => wi.ClaimedByStation)
            .OrderByDescending(wi => wi.Id)
            .ToListAsync(cancellationToken);
    }
}
