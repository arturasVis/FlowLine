using FlowLine.Application.Relay;
using FlowLine.Domain.Entities;
using FlowLine.Domain.Entities.External;
using FlowLine.Infrastructure.Data;
using FlowLine.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Tests.Relay;

public class RelayServicePrebuildTests
{
    private static async Task<(RelayFixtureContext Fixture, WorkItem Claimed)> SeedClaimedWithHistoryAsync(FlowLineDbContext db)
    {
        await SqliteTestDatabase.CreateExternalTablesAsync(db);
        var fixture = await RelayTestFixture.SeedAsync(db);

        db.History.Add(new HistoryRecord { OrderId = "PB-5001", Sku = "GPU-PREBUILT", Qty = 1, Date = DateTime.UtcNow });
        db.WorkItems.Add(RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, DateTime.UtcNow));
        await db.SaveChangesAsync();

        var relay = new RelayService(db, new RelayNotifier());
        var claimed = await relay.ClaimNextAsync(fixture.StationA1.Id);
        return (fixture, claimed!);
    }

    [Fact]
    public async Task SetPrebuild_ValidId_StoresOnWorkItem()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var (fixture, claimed) = await SeedClaimedWithHistoryAsync(db);
            var relay = new RelayService(db, new RelayNotifier());

            await relay.SetPrebuildAsync(claimed.Id, fixture.StationA1.Id, "PB-5001");

            var reloaded = await db.WorkItems.SingleAsync(wi => wi.Id == claimed.Id);
            Assert.Equal("PB-5001", reloaded.PrebuildId);

            var info = await relay.GetPrebuildInfoAsync("PB-5001");
            Assert.Equal("GPU-PREBUILT", info!.Sku);
        }
    }

    [Fact]
    public async Task SetPrebuild_UnknownId_ThrowsAndLeavesWorkItemUnchanged()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var (fixture, claimed) = await SeedClaimedWithHistoryAsync(db);
            var relay = new RelayService(db, new RelayNotifier());

            await Assert.ThrowsAsync<RelayOperationException>(
                () => relay.SetPrebuildAsync(claimed.Id, fixture.StationA1.Id, "PB-NOPE"));

            var reloaded = await db.WorkItems.SingleAsync(wi => wi.Id == claimed.Id);
            Assert.Null(reloaded.PrebuildId);
        }
    }

    [Fact]
    public async Task SetPrebuild_StationNotHoldingClaim_Throws()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var (fixture, claimed) = await SeedClaimedWithHistoryAsync(db);
            var relay = new RelayService(db, new RelayNotifier());

            // StationA2 didn't claim this unit — it can't attach the prebuild.
            await Assert.ThrowsAsync<RelayOperationException>(
                () => relay.SetPrebuildAsync(claimed.Id, fixture.StationA2.Id, "PB-5001"));
        }
    }
}
