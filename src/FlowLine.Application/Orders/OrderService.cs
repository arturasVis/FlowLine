using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Application.Orders;

public class OrderService(FlowLineDbContext db) : IOrderService
{
    public async Task<WorkItem> CreateWorkItemAsync(
        int workflowId, string orderNumber, string sku, int quantity, string? channel,
        CancellationToken cancellationToken = default)
    {
        var firstStage = await db.Stages
            .Where(s => s.WorkflowId == workflowId)
            .OrderBy(s => s.OrderIndex)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new OrderServiceException(
                "This workflow has no stages yet — add one in the workflow builder before creating orders.");

        var workItem = new WorkItem
        {
            WorkflowId = workflowId,
            CurrentStageId = firstStage.Id,
            OrderNumber = orderNumber,
            Sku = sku,
            Quantity = quantity,
            Channel = channel,
            Status = WorkItemStatus.Queued,
            QueuedAtUtc = DateTime.UtcNow,
        };

        db.WorkItems.Add(workItem);
        await db.SaveChangesAsync(cancellationToken);
        return workItem;
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
