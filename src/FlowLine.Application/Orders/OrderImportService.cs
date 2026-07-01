using FlowLine.Application.Orders;
using FlowLine.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Application.Orders;

public class OrderImportService(FlowLineDbContext db, IOrderService orders) : IOrderImportService
{
    public bool ExternalTablesSupported =>
        db.Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer";

    public async Task<List<ImportableOrder>> GetImportableOrdersAsync(CancellationToken cancellationToken = default)
    {
        // Already-imported orders are those whose History.OrderId matches an existing
        // WorkItem.OrderNumber — that mapping is how a History row and its FlowLine WorkItem
        // stay associated without ever writing an "imported" flag back to the company table.
        var existingOrderNumbers = await db.WorkItems
            .Select(wi => wi.OrderNumber)
            .ToListAsync(cancellationToken);
        var existing = existingOrderNumbers.ToHashSet();

        var history = await db.History
            .OrderByDescending(h => h.Date)
            .ToListAsync(cancellationToken);

        // Staff_Table is a small lookup list, so resolve assignee names against an in-memory map
        // rather than a SQL join — a nullable "Assigne Number" -> non-nullable "Staff number" join
        // key doesn't translate to SQL anyway.
        var staffNames = await db.Staff
            .ToDictionaryAsync(s => s.StaffNumber, s => s.Name, cancellationToken);

        return history
            .Where(h => !existing.Contains(h.OrderId))
            .Select(h => new ImportableOrder(
                h.OrderId,
                h.Sku,
                h.Qty,
                h.Channel,
                h.Date,
                h.Status,
                h.AssigneeNumber,
                h.AssigneeNumber is int n && staffNames.TryGetValue(n, out var name) ? name : null))
            .ToList();
    }

    public async Task<int> ImportAsync(int workflowId, IReadOnlyCollection<string> orderIds, CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
        {
            return 0;
        }

        var requested = orderIds.ToHashSet();

        // Re-read straight from History (not the projection) so an import always reflects current
        // company data, and re-check the dedup so a row imported since the page loaded is skipped.
        var alreadyImported = (await db.WorkItems
                .Where(wi => requested.Contains(wi.OrderNumber))
                .Select(wi => wi.OrderNumber)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var historyRows = await db.History
            .Where(h => requested.Contains(h.OrderId))
            .ToListAsync(cancellationToken);

        var imported = 0;
        foreach (var row in historyRows.Where(r => !alreadyImported.Contains(r.OrderId)))
        {
            // Reuse OrderService so the imported order queues at the workflow's first stage
            // exactly like a hand-created one (and throws the same no-stages guard).
            await orders.CreateWorkItemAsync(
                workflowId, row.OrderId, row.Sku, row.Qty, row.Channel, cancellationToken);
            imported++;
        }

        return imported;
    }
}
