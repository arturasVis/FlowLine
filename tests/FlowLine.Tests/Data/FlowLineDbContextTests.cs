using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Tests.Data;

public class FlowLineDbContextTests
{
    [Fact]
    public async Task SavesWorkflowStageStepAndWorkItem_WithRelationshipsIntact()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            var workflow = new Workflow { Name = "Gaming PC Build" };
            var stage = new Stage { Name = "Stage 1", OrderIndex = 0, Workflow = workflow };
            var step = new Step { Name = "Pick Case", Instructions = "Grab a case from the rack.", OrderIndex = 0, Stage = stage };
            var workItem = new WorkItem
            {
                Workflow = workflow,
                CurrentStage = stage,
                OrderNumber = "ORD-1",
                Sku = "SKU-1",
                Quantity = 1,
            };

            using (var context = new FlowLineDbContext(options))
            {
                context.Steps.Add(step);
                context.WorkItems.Add(workItem);
                await context.SaveChangesAsync();
            }

            using (var context = new FlowLineDbContext(options))
            {
                var loaded = await context.WorkItems
                    .Include(wi => wi.Workflow)
                    .Include(wi => wi.CurrentStage)
                    .ThenInclude(s => s.Steps)
                    .SingleAsync();

                Assert.Equal("ORD-1", loaded.OrderNumber);
                Assert.Equal(WorkItemStatus.Queued, loaded.Status);
                Assert.Equal("Gaming PC Build", loaded.Workflow.Name);
                Assert.Single(loaded.CurrentStage.Steps);
                Assert.NotEqual(Guid.Empty, loaded.RowVersion);
            }
        }
    }

    [Fact]
    public async Task ConcurrentUpdate_OfSameWorkItem_ThrowsConcurrencyException()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            var workflow = new Workflow { Name = "Gaming PC Build" };
            var stage = new Stage { Name = "Stage 1", OrderIndex = 0, Workflow = workflow };
            var workItem = new WorkItem
            {
                Workflow = workflow,
                CurrentStage = stage,
                OrderNumber = "ORD-1",
                Sku = "SKU-1",
                Quantity = 1,
            };

            int workItemId;
            using (var context = new FlowLineDbContext(options))
            {
                context.WorkItems.Add(workItem);
                await context.SaveChangesAsync();
                workItemId = workItem.Id;
            }

            // Two separate contexts simulate two stations loading the same unit
            // before either one writes back — exactly the race the atomic claim
            // (PRD §6.5) must prevent.
            using var contextA = new FlowLineDbContext(options);
            using var contextB = new FlowLineDbContext(options);

            var workItemA = await contextA.WorkItems.SingleAsync(wi => wi.Id == workItemId);
            var workItemB = await contextB.WorkItems.SingleAsync(wi => wi.Id == workItemId);

            workItemA.Status = WorkItemStatus.InProgress;
            await contextA.SaveChangesAsync();

            workItemB.Status = WorkItemStatus.InProgress;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => contextB.SaveChangesAsync());
        }
    }
}
