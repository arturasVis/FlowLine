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

            var service = new OrderService(db);

            var workItem = await service.CreateWorkItemAsync(workflow.Id, "ORD-1", "SKU-1", 2, "Retail");

            Assert.Equal(stage1.Id, workItem.CurrentStageId);
            Assert.Equal(WorkItemStatus.Queued, workItem.Status);
            Assert.Equal(2, workItem.Quantity);
            Assert.Equal("Retail", workItem.Channel);

            var reloaded = await db.WorkItems.SingleAsync(wi => wi.Id == workItem.Id);
            Assert.Equal(stage1.Id, reloaded.CurrentStageId);
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

            var service = new OrderService(db);

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

            var service = new OrderService(db);
            var first = await service.CreateWorkItemAsync(workflow.Id, "ORD-1", "SKU-1", 1, null);
            var second = await service.CreateWorkItemAsync(workflow.Id, "ORD-2", "SKU-2", 1, null);

            var workItems = await service.GetWorkItemsAsync();

            Assert.Equal([second.Id, first.Id], workItems.Select(wi => wi.Id));
            Assert.Equal("RMA Teardown", workItems[0].Workflow.Name);
            Assert.Equal("Inspect", workItems[0].CurrentStage.Name);
        }
    }
}
