using FlowLine.Application.Relay;
using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using FlowLine.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Tests.Relay;

public class RelayServiceAdHocTests
{
    [Fact]
    public async Task StartAdHoc_FreeRunWorkflow_CreatesClaimedRunAtFirstStation()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await RelayTestFixture.SeedAsync(db);
            fixture.Workflow.AllowAdHocStart = true;
            await db.SaveChangesAsync();
            var relay = new RelayService(db, new RelayNotifier());

            var run = await relay.StartAdHocAsync(fixture.StationA1.Id);

            Assert.StartsWith("RUN-", run.OrderNumber);
            Assert.Equal(fixture.Workflow.Id, run.WorkflowId);
            Assert.Equal(fixture.StageA.Id, run.CurrentStageId);
            Assert.Equal(WorkItemStatus.InProgress, run.Status);
            Assert.Equal(fixture.StationA1.Id, run.ClaimedByStationId);
            Assert.NotNull(run.ClaimedAtUtc); // anchors the first step's StartedAtUtc

            var reloaded = await db.WorkItems.SingleAsync(wi => wi.Id == run.Id);
            Assert.Equal(WorkItemStatus.InProgress, reloaded.Status);
        }
    }

    [Fact]
    public async Task StartAdHoc_WorkflowNotInAdHocMode_Throws()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await RelayTestFixture.SeedAsync(db); // AllowAdHocStart defaults false
            var relay = new RelayService(db, new RelayNotifier());

            await Assert.ThrowsAsync<RelayOperationException>(
                () => relay.StartAdHocAsync(fixture.StationA1.Id));
            Assert.Equal(0, await db.WorkItems.CountAsync());
        }
    }

    [Fact]
    public async Task StartAdHoc_NotFirstStation_Throws()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await RelayTestFixture.SeedAsync(db);
            fixture.Workflow.AllowAdHocStart = true;
            await db.SaveChangesAsync();
            var relay = new RelayService(db, new RelayNotifier());

            // StationB is on the second stage — a run can only start at the entry stage.
            await Assert.ThrowsAsync<RelayOperationException>(
                () => relay.StartAdHocAsync(fixture.StationB.Id));
            Assert.Equal(0, await db.WorkItems.CountAsync());
        }
    }
}
