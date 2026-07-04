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
    public async Task CreateFromPrebuild_ValidId_CreatesUnitsFromHistoryQuantityAndClaimsOne()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await SeedWithHistoryAsync(db);
            var relay = new RelayService(db, new RelayNotifier());

            var created = await relay.CreateFromPrebuildAsync(fixture.StationA1.Id, "PB-5001");

            // Inherits the History row's SKU/channel, creates one WorkItem per physical unit, and
            // claims one immediately at the scanning station.
            Assert.Equal("PB-5001", created.OrderNumber);
            Assert.Equal("GPU-PREBUILT", created.Sku);
            Assert.Equal(1, created.Quantity);
            Assert.Equal("Internal", created.Channel);
            Assert.Equal(fixture.Workflow.Id, created.WorkflowId);
            Assert.Equal(fixture.StageA.Id, created.CurrentStageId);
            Assert.Equal(WorkItemStatus.InProgress, created.Status);
            Assert.Equal(fixture.StationA1.Id, created.ClaimedByStationId);
            Assert.NotNull(created.ClaimedAtUtc); // anchors the first step's StartedAtUtc

            var units = await db.WorkItems
                .Where(wi => wi.OrderNumber == "PB-5001")
                .OrderBy(wi => wi.Id)
                .ToListAsync();
            Assert.Equal(3, units.Count);
            Assert.All(units, wi => Assert.Equal(1, wi.Quantity));
            Assert.All(units, wi => Assert.Equal("GPU-PREBUILT", wi.Sku));
            Assert.All(units, wi => Assert.Equal("Internal", wi.Channel));
            Assert.Equal(created.Id, units[0].Id);
            Assert.Equal(WorkItemStatus.InProgress, units[0].Status);
            Assert.Equal(fixture.StationA1.Id, units[0].ClaimedByStationId);
            Assert.Equal(WorkItemStatus.Queued, units[1].Status);
            Assert.Equal(WorkItemStatus.Queued, units[2].Status);
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
    public async Task CreateFromPrebuild_HistoryBatch_EachScanClaimsTheNextQueuedUnit()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await SeedWithHistoryAsync(db);
            var relay = new RelayService(db, new RelayNotifier());

            var first = await relay.CreateFromPrebuildAsync(fixture.StationA1.Id, "PB-5001");
            var second = await relay.CreateFromPrebuildAsync(fixture.StationA1.Id, "PB-5001");
            var third = await relay.CreateFromPrebuildAsync(fixture.StationA1.Id, "PB-5001");

            Assert.NotEqual(first.Id, second.Id);
            Assert.NotEqual(second.Id, third.Id);
            Assert.Equal(WorkItemStatus.InProgress, first.Status);
            Assert.Equal(WorkItemStatus.InProgress, second.Status);
            Assert.Equal(WorkItemStatus.InProgress, third.Status);
            await Assert.ThrowsAsync<RelayOperationException>(
                () => relay.CreateFromPrebuildAsync(fixture.StationA1.Id, "PB-5001"));

            Assert.Equal(3, await db.WorkItems.CountAsync(wi => wi.OrderNumber == "PB-5001"));
        }
    }

    [Fact]
    public async Task CreateFromPrebuild_BatchOrder_EachScanClaimsTheNextQueuedUnit()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await SeedWithHistoryAsync(db);

            // A QTY-3 order: three units share the number, all Queued at the entry stage (oldest first).
            var t0 = DateTime.UtcNow;
            var u1 = RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, t0, "BATCH-3");
            var u2 = RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, t0, "BATCH-3");
            var u3 = RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, t0, "BATCH-3");
            db.WorkItems.AddRange(u1, u2, u3);
            await db.SaveChangesAsync();

            var relay = new RelayService(db, new RelayNotifier());

            // Scanning the batch number claims the units one at a time, FIFO, no duplicates created.
            var first = await relay.CreateFromPrebuildAsync(fixture.StationA1.Id, "BATCH-3");
            var second = await relay.CreateFromPrebuildAsync(fixture.StationA1.Id, "BATCH-3");
            Assert.Equal(u1.Id, first.Id);
            Assert.Equal(u2.Id, second.Id);
            Assert.Equal(WorkItemStatus.InProgress, first.Status);
            Assert.Equal(WorkItemStatus.InProgress, second.Status);

            // One still queued at entry — a third scan claims it.
            var third = await relay.CreateFromPrebuildAsync(fixture.StationA1.Id, "BATCH-3");
            Assert.Equal(u3.Id, third.Id);

            // All three started (none completed yet), so a fourth scan has nothing left to claim.
            await Assert.ThrowsAsync<RelayOperationException>(
                () => relay.CreateFromPrebuildAsync(fixture.StationA1.Id, "BATCH-3"));
            Assert.Equal(3, await db.WorkItems.CountAsync(wi => wi.OrderNumber == "BATCH-3"));
        }
    }
}
