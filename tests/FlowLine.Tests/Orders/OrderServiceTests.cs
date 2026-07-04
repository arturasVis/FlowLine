using FlowLine.Application.Orders;
using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using FlowLine.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Tests.Orders;

public class OrderServiceTests
{
    [Fact]
    public async Task CreateWorkItemAsync_QueuesAtTheWorkflowsFirstStage()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var workflow = new Workflow { Name = "RMA Teardown" };
            var stage1 = new Stage { Workflow = workflow, Name = "Inspect", OrderIndex = 0 };
            var stage2 = new Stage { Workflow = workflow, Name = "Disassemble", OrderIndex = 1 };
            workflow.Stages.Add(stage1);
            workflow.Stages.Add(stage2);
            db.Workflows.Add(workflow);
            await db.SaveChangesAsync();

            var service = new OrderService(db, new FlowLine.Application.Relay.RelayNotifier());

            var units = await service.CreateWorkItemAsync(workflow.Id, "ORD-1", "SKU-1", 1, "Retail");

            var workItem = Assert.Single(units);
            Assert.Equal(stage1.Id, workItem.CurrentStageId);
            Assert.Equal(WorkItemStatus.Queued, workItem.Status);
            Assert.Equal(1, workItem.Quantity);
            Assert.Equal("Retail", workItem.Channel);

            var reloaded = await db.WorkItems.SingleAsync(wi => wi.Id == workItem.Id);
            Assert.Equal(stage1.Id, reloaded.CurrentStageId);
        }
    }

    [Fact]
    public async Task CreateWorkItemAsync_QuantityN_CreatesNIndependentUnitsSharingTheOrderNumber()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var workflow = new Workflow { Name = "Build" };
            var stage = new Stage { Workflow = workflow, Name = "Assemble", OrderIndex = 0 };
            workflow.Stages.Add(stage);
            db.Workflows.Add(workflow);
            await db.SaveChangesAsync();

            var service = new OrderService(db, new FlowLine.Application.Relay.RelayNotifier());

            var units = await service.CreateWorkItemAsync(workflow.Id, "ORD-50", "SKU-X", 50, "Amazon");

            Assert.Equal(50, units.Count);
            Assert.All(units, u => Assert.Equal("ORD-50", u.OrderNumber));
            Assert.All(units, u => Assert.Equal(1, u.Quantity));
            Assert.All(units, u => Assert.Equal(WorkItemStatus.Queued, u.Status));
            Assert.All(units, u => Assert.Equal(stage.Id, u.CurrentStageId));
            // 50 distinct WorkItem rows actually persisted for this order.
            Assert.Equal(50, await db.WorkItems.CountAsync(wi => wi.OrderNumber == "ORD-50"));
        }
    }

    [Fact]
    public async Task CreateWorkItemAsync_QuantityBelowOne_CreatesASingleUnit()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var workflow = new Workflow { Name = "Build" };
            var stage = new Stage { Workflow = workflow, Name = "Assemble", OrderIndex = 0 };
            workflow.Stages.Add(stage);
            db.Workflows.Add(workflow);
            await db.SaveChangesAsync();

            var service = new OrderService(db, new FlowLine.Application.Relay.RelayNotifier());

            var units = await service.CreateWorkItemAsync(workflow.Id, "ORD-0", "SKU", 0, null);

            Assert.Single(units);
        }
    }

    [Fact]
    public async Task CreateWorkItemAsync_WorkflowWithNoStages_Throws()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var workflow = new Workflow { Name = "Empty Workflow" };
            db.Workflows.Add(workflow);
            await db.SaveChangesAsync();

            var service = new OrderService(db, new FlowLine.Application.Relay.RelayNotifier());

            await Assert.ThrowsAsync<OrderServiceException>(
                () => service.CreateWorkItemAsync(workflow.Id, "ORD-1", "SKU-1", 1, null));
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_ReturnsNewestFirst_WithRelatedDataLoaded()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var workflow = new Workflow { Name = "RMA Teardown" };
            var stage = new Stage { Workflow = workflow, Name = "Inspect", OrderIndex = 0 };
            workflow.Stages.Add(stage);
            db.Workflows.Add(workflow);
            await db.SaveChangesAsync();

            var service = new OrderService(db, new FlowLine.Application.Relay.RelayNotifier());
            var first = (await service.CreateWorkItemAsync(workflow.Id, "ORD-1", "SKU-1", 1, null)).Single();
            var second = (await service.CreateWorkItemAsync(workflow.Id, "ORD-2", "SKU-2", 1, null)).Single();

            var workItems = await service.GetWorkItemsAsync();

            Assert.Equal([second.Id, first.Id], workItems.Select(wi => wi.Id));
            Assert.Equal("RMA Teardown", workItems[0].Workflow.Name);
            Assert.Equal("Inspect", workItems[0].CurrentStage.Name);
        }
    }

    [Fact]
    public async Task CancelWorkItemAsync_QueuedOrInProgress_BecomesCancelledAndUnclaimed()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var workflow = new Workflow { Name = "WF" };
            var stage = new Stage { Workflow = workflow, Name = "S1", OrderIndex = 0 };
            workflow.Stages.Add(stage);
            var station = new Station { Stage = stage, Name = "St1" };
            db.Workflows.Add(workflow);
            db.Stations.Add(station);
            await db.SaveChangesAsync();

            var service = new OrderService(db, new FlowLine.Application.Relay.RelayNotifier());
            var queued = (await service.CreateWorkItemAsync(workflow.Id, "ORD-Q", "SKU", 1, null)).Single();
            var claimed = (await service.CreateWorkItemAsync(workflow.Id, "ORD-C", "SKU", 1, null)).Single();
            claimed.Status = WorkItemStatus.InProgress;
            claimed.ClaimedByStationId = station.Id;
            claimed.ClaimedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await service.CancelWorkItemAsync(queued.Id);
            await service.CancelWorkItemAsync(claimed.Id);

            var q = await db.WorkItems.SingleAsync(wi => wi.Id == queued.Id);
            var c = await db.WorkItems.SingleAsync(wi => wi.Id == claimed.Id);
            Assert.Equal(WorkItemStatus.Cancelled, q.Status);
            Assert.Equal(WorkItemStatus.Cancelled, c.Status);
            Assert.Null(c.ClaimedByStationId);
            Assert.Null(c.ClaimedAtUtc);

            // Terminal states can't be cancelled again.
            await Assert.ThrowsAsync<OrderServiceException>(() => service.CancelWorkItemAsync(queued.Id));
        }
    }
}
