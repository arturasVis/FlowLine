using FlowLine.Application.Orders;
using FlowLine.Domain.Entities;
using FlowLine.Domain.Entities.External;
using FlowLine.Infrastructure.Data;
using FlowLine.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Tests.Orders;

public class OrderImportServiceTests
{
    // The company History/StaffTable tables are ExcludeFromMigrations, so neither EnsureCreated
    // nor a migration creates them — production treats them as already existing in the company DB.
    // Delegates to the shared helper, which builds SQLite stand-ins whose columns mirror the real
    // company schema (Orderid/TestStatus/AssignedNumber) so the read-only import queries have
    // something real to run against.
    private static Task CreateExternalTablesAsync(FlowLineDbContext db) =>
        SqliteTestDatabase.CreateExternalTablesAsync(db);

    private static async Task<(Workflow workflow, Stage firstStage)> SeedWorkflowAsync(FlowLineDbContext db)
    {
        var workflow = new Workflow { Name = "Gaming PC Build" };
        var stage1 = new Stage { Workflow = workflow, Name = "Pick", OrderIndex = 0 };
        var stage2 = new Stage { Workflow = workflow, Name = "Assemble", OrderIndex = 1 };
        workflow.Stages.Add(stage1);
        workflow.Stages.Add(stage2);
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();
        return (workflow, stage1);
    }

    [Fact]
    public async Task GetImportableOrders_ExcludesAlreadyImported_AndResolvesAssigneeName()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            await CreateExternalTablesAsync(db);
            var (workflow, firstStage) = await SeedWorkflowAsync(db);

            db.Staff.Add(new StaffMember { StaffNumber = 1, Name = "Alice Turner", TestingPower = 3 });
            db.History.AddRange(
                new HistoryRecord { OrderId = "ORD-1001", Sku = "GPU", Qty = 1, Date = new DateTime(2026, 6, 30), AssigneeNumber = 1 },
                new HistoryRecord { OrderId = "ORD-2001", Sku = "CPU", Qty = 2, Date = new DateTime(2026, 7, 1), AssigneeNumber = 1 },
                new HistoryRecord { OrderId = "ORD-2002", Sku = "RAM", Qty = 4, Date = new DateTime(2026, 7, 2), AssigneeNumber = 99 });
            await db.SaveChangesAsync();

            // ORD-1001 is already a FlowLine WorkItem, so it must not appear as importable.
            db.WorkItems.Add(new WorkItem
            {
                WorkflowId = workflow.Id,
                CurrentStageId = firstStage.Id,
                OrderNumber = "ORD-1001",
                Sku = "GPU",
                Quantity = 1,
            });
            await db.SaveChangesAsync();

            var service = new OrderImportService(db, new OrderService(db, new FlowLine.Application.Relay.RelayNotifier()));
            var importable = await service.GetImportableOrdersAsync();

            // Newest first, ORD-1001 excluded.
            Assert.Equal(["ORD-2002", "ORD-2001"], importable.Select(o => o.OrderId));
            // Assignee 1 resolves to a name; unknown assignee 99 stays null.
            Assert.Equal("Alice Turner", importable.Single(o => o.OrderId == "ORD-2001").AssigneeName);
            Assert.Null(importable.Single(o => o.OrderId == "ORD-2002").AssigneeName);
        }
    }

    [Fact]
    public async Task ImportAsync_CreatesQueuedWorkItemsAtFirstStage_AndSkipsAlreadyImported()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            await CreateExternalTablesAsync(db);
            var (workflow, firstStage) = await SeedWorkflowAsync(db);

            db.History.AddRange(
                new HistoryRecord { OrderId = "ORD-2001", Sku = "CPU", Qty = 2, Channel = "eBay", Date = new DateTime(2026, 7, 1) },
                new HistoryRecord { OrderId = "ORD-2002", Sku = "RAM", Qty = 4, Date = new DateTime(2026, 7, 2) });
            // ORD-2002 already imported — ImportAsync must skip it even if asked to import it.
            db.WorkItems.Add(new WorkItem
            {
                WorkflowId = workflow.Id,
                CurrentStageId = firstStage.Id,
                OrderNumber = "ORD-2002",
                Sku = "RAM",
                Quantity = 4,
            });
            await db.SaveChangesAsync();

            var service = new OrderImportService(db, new OrderService(db, new FlowLine.Application.Relay.RelayNotifier()));
            var count = await service.ImportAsync(workflow.Id, ["ORD-2001", "ORD-2002"]);

            // One History *order* imported (count is orders, not units)...
            Assert.Equal(1, count);
            // ...but its QTY of 2 becomes 2 independent units, each Quantity 1, queued at stage 1.
            var imported = await db.WorkItems.Where(wi => wi.OrderNumber == "ORD-2001").ToListAsync();
            Assert.Equal(2, imported.Count);
            Assert.All(imported, wi => Assert.Equal(firstStage.Id, wi.CurrentStageId));
            Assert.All(imported, wi => Assert.Equal(WorkItemStatus.Queued, wi.Status));
            Assert.All(imported, wi => Assert.Equal(1, wi.Quantity));
            Assert.All(imported, wi => Assert.Equal("eBay", wi.Channel));
            // Still exactly one WorkItem for ORD-2002 (the pre-existing one), not a duplicate.
            Assert.Equal(1, await db.WorkItems.CountAsync(wi => wi.OrderNumber == "ORD-2002"));
        }
    }

    [Fact]
    public async Task ImportAsync_EmptySelection_ImportsNothing()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            await CreateExternalTablesAsync(db);
            var (workflow, _) = await SeedWorkflowAsync(db);

            var service = new OrderImportService(db, new OrderService(db, new FlowLine.Application.Relay.RelayNotifier()));
            var count = await service.ImportAsync(workflow.Id, []);

            Assert.Equal(0, count);
            Assert.Equal(0, await db.WorkItems.CountAsync());
        }
    }
}
