using FlowLine.Application.Relay;
using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using FlowLine.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Tests.Relay;

/// <summary>
/// Scan-required stages are fed by a scan: they do not auto-claim, and the scan claims the
/// matching queued WorkItem at that stage before normal step advancement begins.
/// </summary>
public class RelayServiceScanStageTests
{
    private static async Task<(RelayFixtureContext Fixture, WorkItem Unit)> SeedQueuedAtScanStageAsync(FlowLineDbContext db)
    {
        var fixture = await RelayTestFixture.SeedAsync(db);
        fixture.StageA.RequiresScan = true;
        var unit = RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, DateTime.UtcNow, "ORD-SCAN");
        db.WorkItems.Add(unit);
        await db.SaveChangesAsync();
        return (fixture, unit);
    }

    [Fact]
    public async Task ClaimNextAsync_ScanRequiredStage_ReturnsNullAndLeavesUnitQueued()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var (fixture, unit) = await SeedQueuedAtScanStageAsync(db);
            var relay = new RelayService(db, new RelayNotifier());

            var claimed = await relay.ClaimNextAsync(fixture.StationA1.Id);

            Assert.Null(claimed);
            var reloaded = await db.WorkItems.SingleAsync(wi => wi.Id == unit.Id);
            Assert.Equal(WorkItemStatus.Queued, reloaded.Status);
            Assert.Null(reloaded.ClaimedByStationId);
        }
    }

    [Fact]
    public async Task ClaimByScan_MatchingQueuedOrder_ClaimsTheUnit()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var (fixture, unit) = await SeedQueuedAtScanStageAsync(db);
            var relay = new RelayService(db, new RelayNotifier());

            var claimed = await relay.ClaimByScanAsync(fixture.StationA1.Id, "  ORD-SCAN  ");

            Assert.Equal(unit.Id, claimed.Id);
            Assert.Equal(WorkItemStatus.InProgress, claimed.Status);
            Assert.Equal(fixture.StationA1.Id, claimed.ClaimedByStationId);
            Assert.NotNull(claimed.ClaimedAtUtc);
        }
    }

    [Fact]
    public async Task ClaimByScan_BatchOrder_EachScanClaimsTheNextQueuedUnit()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await RelayTestFixture.SeedAsync(db);
            fixture.StageA.RequiresScan = true;
            var t0 = DateTime.UtcNow;
            var firstUnit = RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, t0, "BATCH-2");
            var secondUnit = RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, t0, "BATCH-2");
            db.WorkItems.AddRange(firstUnit, secondUnit);
            await db.SaveChangesAsync();

            var relay = new RelayService(db, new RelayNotifier());

            var first = await relay.ClaimByScanAsync(fixture.StationA1.Id, "BATCH-2");
            var second = await relay.ClaimByScanAsync(fixture.StationA1.Id, "BATCH-2");

            Assert.Equal(firstUnit.Id, first.Id);
            Assert.Equal(secondUnit.Id, second.Id);
            Assert.Equal(2, await db.WorkItems.CountAsync(wi => wi.OrderNumber == "BATCH-2"));
            await Assert.ThrowsAsync<RelayOperationException>(
                () => relay.ClaimByScanAsync(fixture.StationA1.Id, "BATCH-2"));
        }
    }

    [Fact]
    public async Task ClaimByScan_NoMatchingUnitAtThisStage_ThrowsAndRecordsNothing()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var (fixture, unit) = await SeedQueuedAtScanStageAsync(db);
            var relay = new RelayService(db, new RelayNotifier());

            var ex = await Assert.ThrowsAsync<RelayOperationException>(
                () => relay.ClaimByScanAsync(fixture.StationA1.Id, "ORD-OTHER"));

            Assert.Contains("ORD-OTHER", ex.Message);
            var reloaded = await db.WorkItems.SingleAsync(wi => wi.Id == unit.Id);
            Assert.Equal(WorkItemStatus.Queued, reloaded.Status);
            Assert.Null(reloaded.ClaimedByStationId);
        }
    }

    [Fact]
    public async Task ClaimByScan_NonScanRequiredStage_Throws()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await RelayTestFixture.SeedAsync(db);
            db.WorkItems.Add(RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, DateTime.UtcNow, "ORD-SCAN"));
            await db.SaveChangesAsync();
            var relay = new RelayService(db, new RelayNotifier());

            await Assert.ThrowsAsync<RelayOperationException>(
                () => relay.ClaimByScanAsync(fixture.StationA1.Id, "ORD-SCAN"));
        }
    }

    [Fact]
    public async Task ClaimByScan_ThenAdvance_DoesNotRequireAnotherScan()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var (fixture, unit) = await SeedQueuedAtScanStageAsync(db);
            var relay = new RelayService(db, new RelayNotifier());

            await relay.ClaimByScanAsync(fixture.StationA1.Id, "ORD-SCAN");
            var result = await relay.AdvanceAsync(unit.Id, fixture.StationA1.Id);

            Assert.Equal("A1", result.CompletedStep.Name);
            Assert.Equal(1, await db.StepCompletions.CountAsync());
        }
    }
}
