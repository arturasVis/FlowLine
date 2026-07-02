using FlowLine.Application.Relay;
using FlowLine.Domain.Entities;
using FlowLine.Domain.Entities.External;
using FlowLine.Infrastructure.Data;
using FlowLine.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Tests.Relay;

public class RelayServicePrebuildTests
{
    private static async Task<RelayFixtureContext> SeedWithHistoryAsync(FlowLineDbContext db)
    {
        await SqliteTestDatabase.CreateExternalTablesAsync(db);
        var fixture = await RelayTestFixture.SeedAsync(db);
        db.History.Add(new HistoryRecord
        {
            OrderId = "PB-5001", Sku = "GPU-PREBUILT", Qty = 3, Channel = "Internal", Date = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return fixture;
    }

    [Fact]
    public async Task CreateFromPrebuild_ValidId_CreatesClaimedWorkItemFromHistory()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await SeedWithHistoryAsync(db);
            var relay = new RelayService(db, new RelayNotifier());

            var created = await relay.CreateFromPrebuildAsync(fixture.StationA1.Id, "PB-5001");

            // Inherits the History row's SKU/qty/channel, queued into this workflow at stage 1,
            // claimed and InProgress at the scanning station.
            Assert.Equal("PB-5001", created.OrderNumber);
            Assert.Equal("GPU-PREBUILT", created.Sku);
            Assert.Equal(3, created.Quantity);
            Assert.Equal("Internal", created.Channel);
            Assert.Equal(fixture.Workflow.Id, created.WorkflowId);
            Assert.Equal(fixture.StageA.Id, created.CurrentStageId);
            Assert.Equal(WorkItemStatus.InProgress, created.Status);
            Assert.Equal(fixture.StationA1.Id, created.ClaimedByStationId);
            Assert.NotNull(created.ClaimedAtUtc); // anchors the first step's StartedAtUtc

            var reloaded = await db.WorkItems.SingleAsync(wi => wi.OrderNumber == "PB-5001");
            Assert.Equal("GPU-PREBUILT", reloaded.Sku);
        }
    }

    [Fact]
    public async Task CreateFromPrebuild_UnknownId_ThrowsAndCreatesNothing()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await SeedWithHistoryAsync(db);
            var relay = new RelayService(db, new RelayNotifier());

            await Assert.ThrowsAsync<RelayOperationException>(
                () => relay.CreateFromPrebuildAsync(fixture.StationA1.Id, "PB-NOPE"));

            Assert.Equal(0, await db.WorkItems.CountAsync());
        }
    }

    [Fact]
    public async Task CreateFromPrebuild_NotFirstStage_Throws()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await SeedWithHistoryAsync(db);
            var relay = new RelayService(db, new RelayNotifier());

            // StationB is on Stage B (second stage) — a prebuild can only start at the first stage.
            await Assert.ThrowsAsync<RelayOperationException>(
                () => relay.CreateFromPrebuildAsync(fixture.StationB.Id, "PB-5001"));
        }
    }

    [Fact]
    public async Task CreateFromPrebuild_QueuedOrderNotInHistory_IsClaimedByTheScan()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await SeedWithHistoryAsync(db);

            // Authored on the Orders screen (not a History row): queued at the entry stage.
            var order = RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, DateTime.UtcNow, "ORD-LOCAL");
            db.WorkItems.Add(order);
            await db.SaveChangesAsync();

            var relay = new RelayService(db, new RelayNotifier());
            var started = await relay.CreateFromPrebuildAsync(fixture.StationA1.Id, "ORD-LOCAL");

            // The existing WorkItem is claimed — no duplicate created, its authored data kept.
            Assert.Equal(order.Id, started.Id);
            Assert.Equal("SKU-1", started.Sku);
            Assert.Equal(WorkItemStatus.InProgress, started.Status);
            Assert.Equal(fixture.StationA1.Id, started.ClaimedByStationId);
            Assert.NotNull(started.ClaimedAtUtc);
            Assert.Equal(1, await db.WorkItems.CountAsync(wi => wi.OrderNumber == "ORD-LOCAL"));
        }
    }

    [Fact]
    public async Task CreateFromPrebuild_OrderInHistoryAndAlreadyQueued_ClaimsTheQueuedOne_NoDuplicate()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await SeedWithHistoryAsync(db);

            // Imported from History into the queue earlier — the History row still exists.
            var imported = RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, DateTime.UtcNow, "PB-5001");
            imported.Sku = "SKU-IMPORTED";
            db.WorkItems.Add(imported);
            await db.SaveChangesAsync();

            var relay = new RelayService(db, new RelayNotifier());
            var started = await relay.CreateFromPrebuildAsync(fixture.StationA1.Id, "PB-5001");

            Assert.Equal(imported.Id, started.Id);
            Assert.Equal("SKU-IMPORTED", started.Sku); // not overwritten from History
            Assert.Equal(1, await db.WorkItems.CountAsync(wi => wi.OrderNumber == "PB-5001"));
        }
    }

    [Fact]
    public async Task CreateFromPrebuild_QueuedAtALaterStage_Throws()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await SeedWithHistoryAsync(db);

            // Mid-line: queued at Stage B, so a scan at the entry station is a duplicate, not a start.
            db.WorkItems.Add(RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageB, DateTime.UtcNow, "ORD-MIDLINE"));
            await db.SaveChangesAsync();

            var relay = new RelayService(db, new RelayNotifier());
            await Assert.ThrowsAsync<RelayOperationException>(
                () => relay.CreateFromPrebuildAsync(fixture.StationA1.Id, "ORD-MIDLINE"));
        }
    }

    [Fact]
    public async Task CreateFromPrebuild_SamePrebuildTwiceWhileInFlight_Throws()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await SeedWithHistoryAsync(db);
            var relay = new RelayService(db, new RelayNotifier());

            await relay.CreateFromPrebuildAsync(fixture.StationA1.Id, "PB-5001");

            await Assert.ThrowsAsync<RelayOperationException>(
                () => relay.CreateFromPrebuildAsync(fixture.StationA1.Id, "PB-5001"));

            Assert.Equal(1, await db.WorkItems.CountAsync(wi => wi.OrderNumber == "PB-5001"));
        }
    }
}
