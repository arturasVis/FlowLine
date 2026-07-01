using FlowLine.Application.Timing;
using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using FlowLine.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Tests.Timing;

public class TimingServiceTests
{
    [Fact]
    public async Task GetCompletedOrderTimingsAsync_ComputesPerStageDurationsFromCreatedAtAndStepCompletions()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);

            var workflow = new Workflow { Name = "RMA Teardown" };
            var stageA = new Stage { Workflow = workflow, Name = "Inspect", OrderIndex = 0 };
            var stageB = new Stage { Workflow = workflow, Name = "Disassemble", OrderIndex = 1 };
            workflow.Stages.Add(stageA);
            workflow.Stages.Add(stageB);

            var stepA1 = new Step { Stage = stageA, Name = "A1", Instructions = "", OrderIndex = 0 };
            var stepA2 = new Step { Stage = stageA, Name = "A2", Instructions = "", OrderIndex = 1 };
            stageA.Steps.Add(stepA1);
            stageA.Steps.Add(stepA2);

            var stepB1 = new Step { Stage = stageB, Name = "B1", Instructions = "", OrderIndex = 0 };
            stageB.Steps.Add(stepB1);

            db.Workflows.Add(workflow);
            await db.SaveChangesAsync();

            var createdAt = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
            var workItem = new WorkItem
            {
                Workflow = workflow,
                CurrentStage = stageB, // final state irrelevant to timing math, but kept consistent
                Status = WorkItemStatus.Completed,
                OrderNumber = "ORD-1",
                Sku = "SKU-1",
                Quantity = 1,
                CreatedAtUtc = createdAt,
            };
            db.WorkItems.Add(workItem);
            await db.SaveChangesAsync();

            // Stage A: last completion 30 minutes after creation.
            db.StepCompletions.Add(new StepCompletion { WorkItem = workItem, Step = stepA1, CompletedAtUtc = createdAt.AddMinutes(10) });
            db.StepCompletions.Add(new StepCompletion { WorkItem = workItem, Step = stepA2, CompletedAtUtc = createdAt.AddMinutes(30) });
            // Stage B: last (only) completion 50 minutes after creation -> 20 minutes after stage A ended.
            db.StepCompletions.Add(new StepCompletion { WorkItem = workItem, Step = stepB1, CompletedAtUtc = createdAt.AddMinutes(50) });
            await db.SaveChangesAsync();

            var service = new TimingService(db);

            var timings = await service.GetCompletedOrderTimingsAsync();

            var timing = Assert.Single(timings);
            Assert.Equal("ORD-1", timing.OrderNumber);
            Assert.Equal("RMA Teardown", timing.WorkflowName);
            Assert.Equal(createdAt, timing.CreatedAtUtc);
            Assert.Equal(createdAt.AddMinutes(50), timing.CompletedAtUtc);
            Assert.Equal(TimeSpan.FromMinutes(50), timing.TotalDuration);

            Assert.Equal(2, timing.StageDurations.Count);
            Assert.Equal("Inspect", timing.StageDurations[0].StageName);
            Assert.Equal(TimeSpan.FromMinutes(30), timing.StageDurations[0].Duration);
            Assert.Equal("Disassemble", timing.StageDurations[1].StageName);
            Assert.Equal(TimeSpan.FromMinutes(20), timing.StageDurations[1].Duration);
        }
    }

    [Fact]
    public async Task GetCompletedOrderTimingsAsync_ExcludesNonCompletedWorkItems()
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

            db.WorkItems.Add(new WorkItem
            {
                Workflow = workflow,
                CurrentStage = stage,
                Status = WorkItemStatus.InProgress,
                OrderNumber = "ORD-1",
                Sku = "SKU-1",
                Quantity = 1,
            });
            await db.SaveChangesAsync();

            var service = new TimingService(db);
            var timings = await service.GetCompletedOrderTimingsAsync();

            Assert.Empty(timings);
        }
    }

    [Fact]
    public async Task GetCompletedOrderTimingsAsync_ReturnsNewestFirst()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);

            var workflow = new Workflow { Name = "RMA Teardown" };
            var stage = new Stage { Workflow = workflow, Name = "Inspect", OrderIndex = 0 };
            var step = new Step { Stage = stage, Name = "S1", Instructions = "", OrderIndex = 0 };
            stage.Steps.Add(step);
            workflow.Stages.Add(stage);
            db.Workflows.Add(workflow);
            await db.SaveChangesAsync();

            WorkItem MakeCompleted(string orderNumber)
            {
                var wi = new WorkItem
                {
                    Workflow = workflow,
                    CurrentStage = stage,
                    Status = WorkItemStatus.Completed,
                    OrderNumber = orderNumber,
                    Sku = "SKU-1",
                    Quantity = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                };
                return wi;
            }

            var first = MakeCompleted("ORD-1");
            var second = MakeCompleted("ORD-2");
            db.WorkItems.AddRange(first, second);
            await db.SaveChangesAsync();

            db.StepCompletions.Add(new StepCompletion { WorkItem = first, Step = step, CompletedAtUtc = DateTime.UtcNow });
            db.StepCompletions.Add(new StepCompletion { WorkItem = second, Step = step, CompletedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();

            var service = new TimingService(db);
            var timings = await service.GetCompletedOrderTimingsAsync();

            Assert.Equal(["ORD-2", "ORD-1"], timings.Select(t => t.OrderNumber));
        }
    }
}
