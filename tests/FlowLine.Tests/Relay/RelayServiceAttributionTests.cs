using FlowLine.Application.Relay;
using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using FlowLine.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Tests.Relay;

/// <summary>
/// Who did each step, and how long it took: CompletedByStaffNumber and StartedAtUtc on
/// StepCompletion, anchored by WorkItem.ClaimedAtUtc — the raw data the timing/stats
/// reporting is built on.
/// </summary>
public class RelayServiceAttributionTests
{
    [Fact]
    public async Task ClaimNextAsync_SetsClaimedAtUtc()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            RelayFixtureContext fixture;
            using (var db = new FlowLineDbContext(options))
            {
                fixture = await RelayTestFixture.SeedAsync(db);
                db.WorkItems.Add(RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, DateTime.UtcNow));
                await db.SaveChangesAsync();
            }

            using (var db = new FlowLineDbContext(options))
            {
                var relay = new RelayService(db, new RelayNotifier());
                var before = DateTime.UtcNow;

                var claimed = await relay.ClaimNextAsync(fixture.StationA1.Id);

                Assert.NotNull(claimed);
                Assert.NotNull(claimed.ClaimedAtUtc);
                Assert.InRange(claimed.ClaimedAtUtc.Value, before, DateTime.UtcNow);
            }
        }
    }

    [Fact]
    public async Task AdvanceAsync_RecordsOperatorStaffNumber()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            RelayFixtureContext fixture;
            WorkItem workItem;
            using (var db = new FlowLineDbContext(options))
            {
                fixture = await RelayTestFixture.SeedAsync(db);
                workItem = RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, DateTime.UtcNow);
                db.WorkItems.Add(workItem);
                await db.SaveChangesAsync();
            }

            using (var db = new FlowLineDbContext(options))
            {
                var relay = new RelayService(db, new RelayNotifier());
                await relay.ClaimNextAsync(fixture.StationA1.Id);
                await relay.AdvanceAsync(workItem.Id, fixture.StationA1.Id, staffNumber: 1001); // A1
                await relay.AdvanceAsync(workItem.Id, fixture.StationA1.Id);                    // A2, no operator known
            }

            using (var db = new FlowLineDbContext(options))
            {
                var completions = await db.StepCompletions
                    .Include(sc => sc.Step)
                    .OrderBy(sc => sc.CompletedAtUtc)
                    .ToListAsync();

                Assert.Equal(2, completions.Count);
                Assert.Equal(1001, completions[0].CompletedByStaffNumber);
                Assert.Null(completions[1].CompletedByStaffNumber);
            }
        }
    }

    [Fact]
    public async Task AdvanceAsync_FirstStepOfStage_StartsAtClaimTime_SubsequentStepsAtPreviousCompletion()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            RelayFixtureContext fixture;
            WorkItem workItem;
            using (var db = new FlowLineDbContext(options))
            {
                fixture = await RelayTestFixture.SeedAsync(db);
                workItem = RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, DateTime.UtcNow);
                db.WorkItems.Add(workItem);
                await db.SaveChangesAsync();
            }

            DateTime claimedAt;
            using (var db = new FlowLineDbContext(options))
            {
                var relay = new RelayService(db, new RelayNotifier());
                var claimed = await relay.ClaimNextAsync(fixture.StationA1.Id);
                claimedAt = claimed!.ClaimedAtUtc!.Value;

                await relay.AdvanceAsync(workItem.Id, fixture.StationA1.Id); // A1
                await relay.AdvanceAsync(workItem.Id, fixture.StationA1.Id); // A2
            }

            using (var db = new FlowLineDbContext(options))
            {
                var completions = await db.StepCompletions
                    .Include(sc => sc.Step)
                    .OrderBy(sc => sc.CompletedAtUtc)
                    .ToListAsync();

                var a1 = Assert.Single(completions, sc => sc.Step.Name == "A1");
                var a2 = Assert.Single(completions, sc => sc.Step.Name == "A2");

                // A1 began when the claim was taken; A2 began the moment A1 finished — so the
                // two durations tile the claim-to-hand-off interval with no gap or overlap.
                Assert.Equal(claimedAt, a1.StartedAtUtc);
                Assert.Equal(a1.CompletedAtUtc, a2.StartedAtUtc);
            }
        }
    }

    [Fact]
    public async Task AdvanceAsync_AfterHandOff_NextStageFirstStep_StartsAtNewClaim_NotPreviousStageCompletion()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            RelayFixtureContext fixture;
            WorkItem workItem;
            using (var db = new FlowLineDbContext(options))
            {
                fixture = await RelayTestFixture.SeedAsync(db);
                workItem = RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, DateTime.UtcNow);
                db.WorkItems.Add(workItem);
                await db.SaveChangesAsync();
            }

            DateTime stageBClaimedAt;
            using (var db = new FlowLineDbContext(options))
            {
                var relay = new RelayService(db, new RelayNotifier());
                await relay.ClaimNextAsync(fixture.StationA1.Id);
                await relay.AdvanceAsync(workItem.Id, fixture.StationA1.Id); // A1
                await relay.AdvanceAsync(workItem.Id, fixture.StationA1.Id); // A2 → hands off to Stage B

                var reloaded = await db.WorkItems.SingleAsync(wi => wi.Id == workItem.Id);
                Assert.Null(reloaded.ClaimedAtUtc); // hand-off cleared the claim timestamp

                var claimedAtB = await relay.ClaimNextAsync(fixture.StationB.Id);
                stageBClaimedAt = claimedAtB!.ClaimedAtUtc!.Value;
                await relay.AdvanceAsync(workItem.Id, fixture.StationB.Id); // B1 → completes
            }

            using (var db = new FlowLineDbContext(options))
            {
                var b1 = await db.StepCompletions.Include(sc => sc.Step).SingleAsync(sc => sc.Step.Name == "B1");
                var a2 = await db.StepCompletions.Include(sc => sc.Step).SingleAsync(sc => sc.Step.Name == "A2");

                // B1's duration must not absorb the queue wait between the stages: it starts at
                // Stage B's claim, not at A2's completion.
                Assert.Equal(stageBClaimedAt, b1.StartedAtUtc);
                Assert.NotEqual(a2.CompletedAtUtc, b1.StartedAtUtc);

                var completed = await db.WorkItems.SingleAsync(wi => wi.Id == workItem.Id);
                Assert.Equal(WorkItemStatus.Completed, completed.Status);
                Assert.Null(completed.ClaimedAtUtc); // completion also clears it
            }
        }
    }
}
