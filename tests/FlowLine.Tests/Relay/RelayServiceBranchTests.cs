using FlowLine.Application.Relay;
using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using FlowLine.Tests.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowLine.Tests.Relay;

public class RelayServiceBranchTests
{
    [Fact]
    public async Task AdvanceAsync_AtForkStage_ReturnsAwaitingRoute_AndStaysClaimed()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            int workItemId;
            int stationId;
            int branchId;

            using (var db = new FlowLineDbContext(options))
            {
                var workflow = new Workflow { Name = "Branching Workflow" };
                var stageA = new Stage { Workflow = workflow, Name = "Stage A", OrderIndex = 0 };
                var stageB = new Stage { Workflow = workflow, Name = "Stage B", OrderIndex = 1 };
                workflow.Stages.Add(stageA);
                workflow.Stages.Add(stageB);

                var stepA1 = new Step { Stage = stageA, Name = "A1", Instructions = "do a1", OrderIndex = 0 };
                stageA.Steps.Add(stepA1);

                var branch = new StageBranch { Stage = stageA, Label = "Go to B", TargetStage = stageB, OrderIndex = 0 };
                stageA.Branches.Add(branch);

                var stationA = new Station { Stage = stageA, Name = "Station A" };

                db.Workflows.Add(workflow);
                db.Stations.Add(stationA);
                await db.SaveChangesAsync();

                var workItem = new WorkItem
                {
                    Workflow = workflow,
                    CurrentStage = stageA,
                    OrderNumber = "ORD-BRANCH",
                    Sku = "SKU-1",
                    Quantity = 1,
                    Status = WorkItemStatus.InProgress,
                    ClaimedByStationId = stationA.Id,
                    ClaimedAtUtc = DateTime.UtcNow,
                    QueuedAtUtc = DateTime.UtcNow
                };
                db.WorkItems.Add(workItem);
                await db.SaveChangesAsync();

                workItemId = workItem.Id;
                stationId = stationA.Id;
                branchId = branch.Id;
            }

            using (var db = new FlowLineDbContext(options))
            {
                var relay = new RelayService(db, new RelayNotifier());
                var result = await relay.AdvanceAsync(workItemId, stationId);

                Assert.Equal(AdvanceOutcome.AwaitingRoute, result.Outcome);
                Assert.Equal("A1", result.CompletedStep.Name);

                var reloaded = await db.WorkItems.SingleAsync(wi => wi.Id == workItemId);
                Assert.Equal(WorkItemStatus.InProgress, reloaded.Status);
                Assert.Equal(stationId, reloaded.ClaimedByStationId);
                Assert.NotNull(reloaded.ClaimedAtUtc);
            }
        }
    }

    [Fact]
    public async Task RouteAsync_NotFinishedSteps_Throws()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            int workItemId;
            int stationId;
            int branchId;

            using (var db = new FlowLineDbContext(options))
            {
                var workflow = new Workflow { Name = "Branching Workflow" };
                var stageA = new Stage { Workflow = workflow, Name = "Stage A", OrderIndex = 0 };
                var stageB = new Stage { Workflow = workflow, Name = "Stage B", OrderIndex = 1 };
                workflow.Stages.Add(stageA);
                workflow.Stages.Add(stageB);

                // Two steps: A1, A2
                var stepA1 = new Step { Stage = stageA, Name = "A1", Instructions = "do a1", OrderIndex = 0 };
                var stepA2 = new Step { Stage = stageA, Name = "A2", Instructions = "do a2", OrderIndex = 1 };
                stageA.Steps.Add(stepA1);
                stageA.Steps.Add(stepA2);

                var branch = new StageBranch { Stage = stageA, Label = "Go to B", TargetStage = stageB, OrderIndex = 0 };
                stageA.Branches.Add(branch);

                var stationA = new Station { Stage = stageA, Name = "Station A" };

                db.Workflows.Add(workflow);
                db.Stations.Add(stationA);
                await db.SaveChangesAsync();

                var workItem = new WorkItem
                {
                    Workflow = workflow,
                    CurrentStage = stageA,
                    OrderNumber = "ORD-BRANCH",
                    Sku = "SKU-1",
                    Quantity = 1,
                    Status = WorkItemStatus.InProgress,
                    ClaimedByStationId = stationA.Id,
                    ClaimedAtUtc = DateTime.UtcNow,
                    QueuedAtUtc = DateTime.UtcNow
                };
                db.WorkItems.Add(workItem);
                await db.SaveChangesAsync();

                workItemId = workItem.Id;
                stationId = stationA.Id;
                branchId = branch.Id;
            }

            using (var db = new FlowLineDbContext(options))
            {
                var relay = new RelayService(db, new RelayNotifier());
                var ex = await Assert.ThrowsAsync<RelayOperationException>(() => relay.RouteAsync(workItemId, stationId, branchId));
                Assert.Equal("Finish the stage's steps before choosing a branch.", ex.Message);
            }
        }
    }

    [Fact]
    public async Task RouteAsync_ValidBranchTargetStage_MovesToTargetStageQueue_AndClearsClaim()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            int workItemId;
            int stationId;
            int branchId;
            int targetStageId;

            using (var db = new FlowLineDbContext(options))
            {
                var workflow = new Workflow { Name = "Branching Workflow" };
                var stageA = new Stage { Workflow = workflow, Name = "Stage A", OrderIndex = 0 };
                var stageB = new Stage { Workflow = workflow, Name = "Stage B", OrderIndex = 1 };
                workflow.Stages.Add(stageA);
                workflow.Stages.Add(stageB);

                var stepA1 = new Step { Stage = stageA, Name = "A1", Instructions = "do a1", OrderIndex = 0 };
                stageA.Steps.Add(stepA1);

                var branch = new StageBranch { Stage = stageA, Label = "Go to B", TargetStage = stageB, OrderIndex = 0 };
                stageA.Branches.Add(branch);

                var stationA = new Station { Stage = stageA, Name = "Station A" };

                db.Workflows.Add(workflow);
                db.Stations.Add(stationA);
                await db.SaveChangesAsync();

                var workItem = new WorkItem
                {
                    Workflow = workflow,
                    CurrentStage = stageA,
                    OrderNumber = "ORD-BRANCH",
                    Sku = "SKU-1",
                    Quantity = 1,
                    Status = WorkItemStatus.InProgress,
                    ClaimedByStationId = stationA.Id,
                    ClaimedAtUtc = DateTime.UtcNow,
                    QueuedAtUtc = DateTime.UtcNow
                };
                db.WorkItems.Add(workItem);
                await db.SaveChangesAsync();

                // Add step completion for A1 so we can route
                db.StepCompletions.Add(new StepCompletion
                {
                    WorkItem = workItem,
                    Step = stepA1,
                    StationId = stationA.Id,
                    CompletedAtUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();

                workItemId = workItem.Id;
                stationId = stationA.Id;
                branchId = branch.Id;
                targetStageId = stageB.Id;
            }

            using (var db = new FlowLineDbContext(options))
            {
                var relay = new RelayService(db, new RelayNotifier());
                var result = await relay.RouteAsync(workItemId, stationId, branchId);

                Assert.Equal(WorkItemStatus.Queued, result.Status);
                Assert.Null(result.ClaimedByStationId);
                Assert.Null(result.ClaimedAtUtc);
                Assert.Equal(targetStageId, result.CurrentStageId);

                var reloaded = await db.WorkItems.SingleAsync(wi => wi.Id == workItemId);
                Assert.Equal(WorkItemStatus.Queued, reloaded.Status);
                Assert.Null(reloaded.ClaimedByStationId);
                Assert.Equal(targetStageId, reloaded.CurrentStageId);
            }
        }
    }

    [Fact]
    public async Task RouteAsync_ValidBranchTargetNull_CompletesWorkItem_AndClearsClaim()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            int workItemId;
            int stationId;
            int branchId;

            using (var db = new FlowLineDbContext(options))
            {
                var workflow = new Workflow { Name = "Branching Workflow" };
                var stageA = new Stage { Workflow = workflow, Name = "Stage A", OrderIndex = 0 };
                workflow.Stages.Add(stageA);

                var stepA1 = new Step { Stage = stageA, Name = "A1", Instructions = "do a1", OrderIndex = 0 };
                stageA.Steps.Add(stepA1);

                var branch = new StageBranch { Stage = stageA, Label = "Finish Work", TargetStageId = null, OrderIndex = 0 };
                stageA.Branches.Add(branch);

                var stationA = new Station { Stage = stageA, Name = "Station A" };

                db.Workflows.Add(workflow);
                db.Stations.Add(stationA);
                await db.SaveChangesAsync();

                var workItem = new WorkItem
                {
                    Workflow = workflow,
                    CurrentStage = stageA,
                    OrderNumber = "ORD-BRANCH",
                    Sku = "SKU-1",
                    Quantity = 1,
                    Status = WorkItemStatus.InProgress,
                    ClaimedByStationId = stationA.Id,
                    ClaimedAtUtc = DateTime.UtcNow,
                    QueuedAtUtc = DateTime.UtcNow
                };
                db.WorkItems.Add(workItem);
                await db.SaveChangesAsync();

                db.StepCompletions.Add(new StepCompletion
                {
                    WorkItem = workItem,
                    Step = stepA1,
                    StationId = stationA.Id,
                    CompletedAtUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();

                workItemId = workItem.Id;
                stationId = stationA.Id;
                branchId = branch.Id;
            }

            using (var db = new FlowLineDbContext(options))
            {
                var relay = new RelayService(db, new RelayNotifier());
                var result = await relay.RouteAsync(workItemId, stationId, branchId);

                Assert.Equal(WorkItemStatus.Completed, result.Status);
                Assert.Null(result.ClaimedByStationId);
                Assert.Null(result.ClaimedAtUtc);

                var reloaded = await db.WorkItems.SingleAsync(wi => wi.Id == workItemId);
                Assert.Equal(WorkItemStatus.Completed, reloaded.Status);
                Assert.Null(reloaded.ClaimedByStationId);
            }
        }
    }

    [Fact]
    public async Task RouteAsync_InvalidBranchId_Throws()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            int workItemId;
            int stationId;

            using (var db = new FlowLineDbContext(options))
            {
                var workflow = new Workflow { Name = "Branching Workflow" };
                var stageA = new Stage { Workflow = workflow, Name = "Stage A", OrderIndex = 0 };
                workflow.Stages.Add(stageA);

                var stepA1 = new Step { Stage = stageA, Name = "A1", Instructions = "do a1", OrderIndex = 0 };
                stageA.Steps.Add(stepA1);

                var stationA = new Station { Stage = stageA, Name = "Station A" };

                db.Workflows.Add(workflow);
                db.Stations.Add(stationA);
                await db.SaveChangesAsync();

                var workItem = new WorkItem
                {
                    Workflow = workflow,
                    CurrentStage = stageA,
                    OrderNumber = "ORD-BRANCH",
                    Sku = "SKU-1",
                    Quantity = 1,
                    Status = WorkItemStatus.InProgress,
                    ClaimedByStationId = stationA.Id,
                    ClaimedAtUtc = DateTime.UtcNow,
                    QueuedAtUtc = DateTime.UtcNow
                };
                db.WorkItems.Add(workItem);
                await db.SaveChangesAsync();

                db.StepCompletions.Add(new StepCompletion
                {
                    WorkItem = workItem,
                    Step = stepA1,
                    StationId = stationA.Id,
                    CompletedAtUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();

                workItemId = workItem.Id;
                stationId = stationA.Id;
            }

            using (var db = new FlowLineDbContext(options))
            {
                var relay = new RelayService(db, new RelayNotifier());
                var ex = await Assert.ThrowsAsync<RelayOperationException>(() => relay.RouteAsync(workItemId, stationId, 9999));
                Assert.Equal("That branch isn't offered at this stage.", ex.Message);
            }
        }
    }

    [Fact]
    public async Task RouteAsync_ReworkLoop_ResetsTargetStageCompletions()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            int workItemId;
            int stationBId;
            int branchId;
            int stageAId;
            int stepA1Id;

            using (var db = new FlowLineDbContext(options))
            {
                var workflow = new Workflow { Name = "Branching Workflow" };
                var stageA = new Stage { Workflow = workflow, Name = "Stage A", OrderIndex = 0 };
                var stageB = new Stage { Workflow = workflow, Name = "Stage B", OrderIndex = 1 };
                workflow.Stages.Add(stageA);
                workflow.Stages.Add(stageB);

                var stepA1 = new Step { Stage = stageA, Name = "A1", Instructions = "do a1", OrderIndex = 0 };
                stageA.Steps.Add(stepA1);

                var stepB1 = new Step { Stage = stageB, Name = "B1", Instructions = "do b1", OrderIndex = 0 };
                stageB.Steps.Add(stepB1);

                // Rework branch at B pointing back to A
                var branch = new StageBranch { Stage = stageB, Label = "Send to rework", TargetStage = stageA, OrderIndex = 0 };
                stageB.Branches.Add(branch);

                var stationA = new Station { Stage = stageA, Name = "Station A" };
                var stationB = new Station { Stage = stageB, Name = "Station B" };

                db.Workflows.Add(workflow);
                db.Stations.AddRange(stationA, stationB);
                await db.SaveChangesAsync();

                var workItem = new WorkItem
                {
                    Workflow = workflow,
                    CurrentStage = stageB, // Currently at Stage B
                    OrderNumber = "ORD-BRANCH",
                    Sku = "SKU-1",
                    Quantity = 1,
                    Status = WorkItemStatus.InProgress,
                    ClaimedByStationId = stationB.Id,
                    ClaimedAtUtc = DateTime.UtcNow,
                    QueuedAtUtc = DateTime.UtcNow
                };
                db.WorkItems.Add(workItem);
                await db.SaveChangesAsync();

                // Completes A1 (from the past pass)
                var compA1 = new StepCompletion
                {
                    WorkItem = workItem,
                    Step = stepA1,
                    StationId = stationA.Id,
                    CompletedAtUtc = DateTime.UtcNow.AddMinutes(-5)
                };
                // Completes B1 (just done now)
                var compB1 = new StepCompletion
                {
                    WorkItem = workItem,
                    Step = stepB1,
                    StationId = stationB.Id,
                    CompletedAtUtc = DateTime.UtcNow
                };
                db.StepCompletions.AddRange(compA1, compB1);
                await db.SaveChangesAsync();

                workItemId = workItem.Id;
                stationBId = stationB.Id;
                branchId = branch.Id;
                stageAId = stageA.Id;
                stepA1Id = stepA1.Id;
            }

            using (var db = new FlowLineDbContext(options))
            {
                var relay = new RelayService(db, new RelayNotifier());
                var result = await relay.RouteAsync(workItemId, stationBId, branchId);

                Assert.Equal(WorkItemStatus.Queued, result.Status);
                Assert.Equal(stageAId, result.CurrentStageId);

                // Verify that Stage A's step completions have been deleted (so it resets)
                var completions = await db.StepCompletions.Where(sc => sc.WorkItemId == workItemId).ToListAsync();
                Assert.DoesNotContain(completions, sc => sc.StepId == stepA1Id);
                
                // Note: B's completion is not deleted *yet*, until we enter B again.
                Assert.Contains(completions, sc => sc.StepId != stepA1Id);
            }
        }
    }
}
