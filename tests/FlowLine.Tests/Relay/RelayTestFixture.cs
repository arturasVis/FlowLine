using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;

namespace FlowLine.Tests.Relay;

/// <summary>
/// A 2-stage workflow (Stage A: steps A1, A2 — Stage B: step B1) with two stations on
/// Stage A (for claim-race tests) and one on Stage B (for hand-off destination tests).
/// </summary>
internal record RelayFixtureContext(
    Workflow Workflow,
    Stage StageA,
    Stage StageB,
    Station StationA1,
    Station StationA2,
    Station StationB);

internal static class RelayTestFixture
{
    public static async Task<RelayFixtureContext> SeedAsync(FlowLineDbContext db)
    {
        var workflow = new Workflow { Name = "Test Workflow" };
        var stageA = new Stage { Workflow = workflow, Name = "Stage A", OrderIndex = 0 };
        var stageB = new Stage { Workflow = workflow, Name = "Stage B", OrderIndex = 1 };
        workflow.Stages.Add(stageA);
        workflow.Stages.Add(stageB);

        stageA.Steps.Add(new Step { Stage = stageA, Name = "A1", Instructions = "do a1", OrderIndex = 0 });
        stageA.Steps.Add(new Step { Stage = stageA, Name = "A2", Instructions = "do a2", OrderIndex = 1 });
        stageB.Steps.Add(new Step { Stage = stageB, Name = "B1", Instructions = "do b1", OrderIndex = 0 });

        var stationA1 = new Station { Stage = stageA, Name = "Station A1" };
        var stationA2 = new Station { Stage = stageA, Name = "Station A2" };
        var stationB = new Station { Stage = stageB, Name = "Station B" };

        db.Workflows.Add(workflow);
        db.Stations.AddRange(stationA1, stationA2, stationB);
        await db.SaveChangesAsync();

        return new RelayFixtureContext(workflow, stageA, stageB, stationA1, stationA2, stationB);
    }

    public static WorkItem NewQueuedWorkItem(
        Workflow workflow, Stage stage, DateTime queuedAtUtc, string orderNumber = "ORD-1") => new()
    {
        Workflow = workflow,
        CurrentStage = stage,
        OrderNumber = orderNumber,
        Sku = "SKU-1",
        Quantity = 1,
        QueuedAtUtc = queuedAtUtc,
    };
}
