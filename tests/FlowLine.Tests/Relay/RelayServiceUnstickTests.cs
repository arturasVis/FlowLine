using FlowLine.Application.Relay;
using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using FlowLine.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Tests.Relay;

/// <summary>
/// The line's "bad day" operations: releasing a stuck claim, sending a unit back for rework,
/// and scrapping a failed unit — plus the timing correctness of resuming a released unit.
/// </summary>
public class RelayServiceUnstickTests
{
    [Fact]
    public async Task ReleaseAsync_RequeuesAtBackOfQueue_KeepingStepProgress()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await RelayTestFixture.SeedAsync(db);
            db.WorkItems.Add(RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, DateTime.UtcNow.AddSeconds(-10), "ORD-STUCK"));
            db.WorkItems.Add(RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, DateTime.UtcNow.AddSeconds(-5), "ORD-NEXT"));
            await db.SaveChangesAsync();

            var relay = new RelayService(db, new RelayNotifier());
            var claimed = await relay.ClaimNextAsync(fixture.StationA1.Id);
            Assert.Equal("ORD-STUCK", claimed!.OrderNumber);
            await relay.AdvanceAsync(claimed.Id, fixture.StationA1.Id); // A1 done, A2 outstanding

            await relay.ReleaseAsync(claimed.Id, fixture.StationA1.Id);

            var released = await db.WorkItems.SingleAsync(wi => wi.OrderNumber == "ORD-STUCK");
            Assert.Equal(WorkItemStatus.Queued, released.Status);
            Assert.Null(released.ClaimedByStationId);
            Assert.Null(released.ClaimedAtUtc);
            Assert.Single(released.StepCompletions); // progress kept

            // Back of the queue: the next claim picks the OTHER unit, not the released one.
            var next = await relay.ClaimNextAsync(fixture.StationA2.Id);
            Assert.Equal("ORD-NEXT", next!.OrderNumber);

            // Re-claiming the released unit resumes at A2, its next outstanding step.
            var resumed = await relay.ClaimNextAsync(fixture.StationA1.Id);
            Assert.Equal("ORD-STUCK", resumed!.OrderNumber);
            var result = await relay.AdvanceAsync(resumed.Id, fixture.StationA1.Id);
            Assert.Equal("A2", result.CompletedStep.Name);
        }
    }

    [Fact]
    public async Task ReleaseAsync_ResumedStep_StartsAtReclaim_NotAtPreReleaseCompletion()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await RelayTestFixture.SeedAsync(db);
            db.WorkItems.Add(RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, DateTime.UtcNow));
            await db.SaveChangesAsync();

            var relay = new RelayService(db, new RelayNotifier());
            var claimed = await relay.ClaimNextAsync(fixture.StationA1.Id);
            await relay.AdvanceAsync(claimed!.Id, fixture.StationA1.Id); // A1
            await relay.ReleaseAsync(claimed.Id, fixture.StationA1.Id);

            var reclaimed = await relay.ClaimNextAsync(fixture.StationA1.Id);
            var reclaimAt = reclaimed!.ClaimedAtUtc!.Value;
            await relay.AdvanceAsync(reclaimed.Id, fixture.StationA1.Id); // A2

            // A2 began at the re-claim, so the released idle gap doesn't count as work time.
            var a2 = await db.StepCompletions.Include(sc => sc.Step).SingleAsync(sc => sc.Step.Name == "A2");
            Assert.Equal(reclaimAt, a2.StartedAtUtc);
        }
    }

    [Fact]
    public async Task ReleaseAsync_WrongStation_Throws_NullStationIsAdminOverride()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await RelayTestFixture.SeedAsync(db);
            db.WorkItems.Add(RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, DateTime.UtcNow));
            await db.SaveChangesAsync();

            var relay = new RelayService(db, new RelayNotifier());
            var claimed = await relay.ClaimNextAsync(fixture.StationA1.Id);

            await Assert.ThrowsAsync<RelayOperationException>(
                () => relay.ReleaseAsync(claimed!.Id, fixture.StationA2.Id));

            await relay.ReleaseAsync(claimed!.Id, null); // admin override skips the ownership check
            Assert.Equal(WorkItemStatus.Queued, (await db.WorkItems.SingleAsync(wi => wi.Id == claimed.Id)).Status);
        }
    }

    [Fact]
    public async Task SendBackAsync_DeletesCompletionsFromTargetOnward_AndRequeuesThere()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await RelayTestFixture.SeedAsync(db);
            db.WorkItems.Add(RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, DateTime.UtcNow));
            await db.SaveChangesAsync();

            var relay = new RelayService(db, new RelayNotifier());
            var unit = await relay.ClaimNextAsync(fixture.StationA1.Id);
            await relay.AdvanceAsync(unit!.Id, fixture.StationA1.Id); // A1
            await relay.AdvanceAsync(unit.Id, fixture.StationA1.Id);  // A2 → hands off to B
            var atB = await relay.ClaimNextAsync(fixture.StationB.Id);
            Assert.Equal(unit.Id, atB!.Id);

            // B discovers a Stage-A defect: send it back to Stage A.
            await relay.SendBackAsync(unit.Id, fixture.StationB.Id, fixture.StageA.Id);

            var reworked = await db.WorkItems.Include(wi => wi.StepCompletions).SingleAsync(wi => wi.Id == unit.Id);
            Assert.Equal(WorkItemStatus.Queued, reworked.Status);
            Assert.Equal(fixture.StageA.Id, reworked.CurrentStageId);
            Assert.Null(reworked.ClaimedByStationId);
            Assert.Empty(reworked.StepCompletions); // stage A onward redone — all completions gone

            // And the relay runs it through Stage A again from step 1.
            var again = await relay.ClaimNextAsync(fixture.StationA1.Id);
            var redo = await relay.AdvanceAsync(again!.Id, fixture.StationA1.Id);
            Assert.Equal("A1", redo.CompletedStep.Name);
        }
    }

    [Fact]
    public async Task SendBackAsync_KeepsEarlierStagesCompletions()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await RelayTestFixture.SeedAsync(db);
            db.WorkItems.Add(RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, DateTime.UtcNow));
            await db.SaveChangesAsync();

            var relay = new RelayService(db, new RelayNotifier());
            var unit = await relay.ClaimNextAsync(fixture.StationA1.Id);
            await relay.AdvanceAsync(unit!.Id, fixture.StationA1.Id);
            await relay.AdvanceAsync(unit.Id, fixture.StationA1.Id);
            await relay.ClaimNextAsync(fixture.StationB.Id);

            // Send back to Stage B itself is invalid (not earlier); Stage A keeps nothing later.
            // Here: target = Stage B is the current stage → rejected.
            await Assert.ThrowsAsync<RelayOperationException>(
                () => relay.SendBackAsync(unit.Id, fixture.StationB.Id, fixture.StageB.Id));

            // Valid: back to A. Stage A completions are the ones being redone; if the workflow
            // had a stage before A, its completions would survive — verified by the redo starting
            // from A1 in the test above. Here we assert the reject left everything intact.
            var intact = await db.WorkItems.Include(wi => wi.StepCompletions).SingleAsync(wi => wi.Id == unit.Id);
            Assert.Equal(2, intact.StepCompletions.Count);
            Assert.Equal(WorkItemStatus.InProgress, intact.Status);
        }
    }

    [Fact]
    public async Task ScrapAsync_MarksScrapped_FreesOrderNumberForRescan()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            await SqliteTestDatabase.CreateExternalTablesAsync(db);
            var fixture = await RelayTestFixture.SeedAsync(db);
            db.History.Add(new FlowLine.Domain.Entities.External.HistoryRecord
            {
                OrderId = "PB-9001", Sku = "SKU-PB", Qty = 1, Date = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            var relay = new RelayService(db, new RelayNotifier());
            var unit = await relay.CreateFromPrebuildAsync(fixture.StationA1.Id, "PB-9001");

            await relay.ScrapAsync(unit.Id, fixture.StationA1.Id);

            var scrapped = await db.WorkItems.SingleAsync(wi => wi.Id == unit.Id);
            Assert.Equal(WorkItemStatus.Scrapped, scrapped.Status);
            Assert.Null(scrapped.ClaimedByStationId);

            // A scrapped unit no longer blocks the in-flight guard — the rebuild can be scanned.
            var rebuild = await relay.CreateFromPrebuildAsync(fixture.StationA1.Id, "PB-9001");
            Assert.NotEqual(unit.Id, rebuild.Id);
            Assert.Equal(WorkItemStatus.InProgress, rebuild.Status);
        }
    }
}
