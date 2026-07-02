using FlowLine.Application.Line;
using FlowLine.Application.Relay;
using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using FlowLine.Tests.Data;
using FlowLine.Tests.Relay;

namespace FlowLine.Tests.Line;

public class LineStatusServiceTests
{
    [Fact]
    public async Task GetLineStatusAsync_ReportsQueuesActiveUnitsAndCompletedToday()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await RelayTestFixture.SeedAsync(db);
            db.Workflows.Add(new Workflow { Name = "Archived WF", IsActive = false });

            // Two queued at A, one claimed at A; one completed today (single B1 completion).
            db.WorkItems.Add(RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, DateTime.UtcNow, "ORD-Q1"));
            db.WorkItems.Add(RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, DateTime.UtcNow, "ORD-Q2"));
            var active = RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, DateTime.UtcNow, "ORD-ACT");
            active.Sku = "SKU-ACT";
            active.Status = WorkItemStatus.InProgress;
            active.ClaimedByStation = fixture.StationA1;
            active.ClaimedAtUtc = DateTime.UtcNow;
            db.WorkItems.Add(active);

            var done = RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageB, DateTime.UtcNow, "ORD-DONE");
            done.Status = WorkItemStatus.Completed;
            done.StepCompletions.Add(new StepCompletion
            {
                WorkItem = done,
                Step = fixture.StageB.Steps.Single(),
                CompletedAtUtc = DateTime.UtcNow,
            });
            db.WorkItems.Add(done);
            await db.SaveChangesAsync();

            var status = await new LineStatusService(db).GetLineStatusAsync();

            // Only the active workflow appears.
            var wf = Assert.Single(status);
            Assert.Equal(fixture.Workflow.Id, wf.WorkflowId);
            Assert.Equal(1, wf.CompletedToday);
            Assert.Equal(3, wf.UnitsOnLine); // 2 queued + 1 active; completed/terminal excluded

            Assert.Equal(2, wf.Stages.Count);
            var stageA = wf.Stages[0];
            Assert.Equal(2, stageA.QueueDepth);
            var unit = Assert.Single(stageA.Active);
            Assert.Equal("ORD-ACT", unit.OrderNumber);
            Assert.Equal("SKU-ACT", unit.Sku);
            Assert.Equal(fixture.StationA1.Name, unit.StationName);

            var stageB = wf.Stages[1];
            Assert.Equal(0, stageB.QueueDepth);
            Assert.Empty(stageB.Active);
            Assert.Equal(1, stageB.StationCount);
        }
    }
}
