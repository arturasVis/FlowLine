using FlowLine.Application.Relay;
using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using FlowLine.Tests.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Tests.Relay;

public class RelayServiceTests
{
    [Fact]
    public async Task ClaimNextAsync_EmptyQueue_ReturnsNull()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await RelayTestFixture.SeedAsync(db);
            var relay = new RelayService(db, new RelayNotifier());

            var claimed = await relay.ClaimNextAsync(fixture.StationA1.Id);

            Assert.Null(claimed);
        }
    }

    [Fact]
    public async Task ClaimNextAsync_ClaimsOldestQueuedItemAtStation_AndSetsInProgress()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            RelayFixtureContext fixture;
            WorkItem older, newer;
            using (var db = new FlowLineDbContext(options))
            {
                fixture = await RelayTestFixture.SeedAsync(db);
                var now = DateTime.UtcNow;
                older = RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, now, "ORD-OLDER");
                newer = RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, now.AddSeconds(5), "ORD-NEWER");
                db.WorkItems.AddRange(newer, older); // insert order shouldn't matter, QueuedAtUtc should win
                await db.SaveChangesAsync();
            }

            using (var db = new FlowLineDbContext(options))
            {
                var relay = new RelayService(db, new RelayNotifier());

                var claimed = await relay.ClaimNextAsync(fixture.StationA1.Id);

                Assert.NotNull(claimed);
                Assert.Equal("ORD-OLDER", claimed.OrderNumber);
                Assert.Equal(WorkItemStatus.InProgress, claimed.Status);
                Assert.Equal(fixture.StationA1.Id, claimed.ClaimedByStationId);
            }

            using (var db = new FlowLineDbContext(options))
            {
                var reloaded = await db.WorkItems.SingleAsync(wi => wi.OrderNumber == "ORD-OLDER");
                Assert.Equal(WorkItemStatus.InProgress, reloaded.Status);
                Assert.Equal(fixture.StationA1.Id, reloaded.ClaimedByStationId);

                var untouched = await db.WorkItems.SingleAsync(wi => wi.OrderNumber == "ORD-NEWER");
                Assert.Equal(WorkItemStatus.Queued, untouched.Status);
                Assert.Null(untouched.ClaimedByStationId);
            }
        }
    }

    [Fact]
    public async Task ClaimNextAsync_AlreadyClaimedItem_IsNotOfferedToAnotherStation()
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
                var first = await relay.ClaimNextAsync(fixture.StationA1.Id);
                Assert.NotNull(first);

                var second = await relay.ClaimNextAsync(fixture.StationA2.Id);
                Assert.Null(second);
            }
        }
    }

    [Fact]
    public async Task ClaimNextAsync_TwoStationsRaceForOneItem_ExactlyOneWins()
    {
        // Genuine concurrency (not sequential calls) is needed to exercise the
        // optimistic-concurrency retry path (FR-13), so this uses a SQLite shared-cache
        // in-memory database — distinct real connections, same underlying data — rather
        // than the single shared SqliteConnection the other tests use.
        var dbName = $"relaytest_{Guid.NewGuid():N}";
        var connectionString = $"Data Source=file:{dbName}?mode=memory&cache=shared;Default Timeout=5";

        using var keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();

        var options = new DbContextOptionsBuilder<FlowLineDbContext>().UseSqlite(connectionString).Options;

        RelayFixtureContext fixture;
        using (var setup = new FlowLineDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            fixture = await RelayTestFixture.SeedAsync(setup);
            setup.WorkItems.Add(RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, DateTime.UtcNow));
            await setup.SaveChangesAsync();
        }

        using var contextA = new FlowLineDbContext(options);
        using var contextB = new FlowLineDbContext(options);
        var relayA = new RelayService(contextA, new RelayNotifier());
        var relayB = new RelayService(contextB, new RelayNotifier());

        var results = await Task.WhenAll(
            relayA.ClaimNextAsync(fixture.StationA1.Id),
            relayB.ClaimNextAsync(fixture.StationA2.Id));

        Assert.Single(results, r => r is not null);

        using var verify = new FlowLineDbContext(options);
        var workItem = await verify.WorkItems.SingleAsync();
        Assert.Equal(WorkItemStatus.InProgress, workItem.Status);
        Assert.NotNull(workItem.ClaimedByStationId);
    }

    [Fact]
    public async Task AdvanceAsync_NotLastStepOfStage_RecordsCompletionAndStaysInProgress()
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
                workItem.Status = WorkItemStatus.InProgress;
                workItem.ClaimedByStationId = fixture.StationA1.Id;
                db.WorkItems.Add(workItem);
                await db.SaveChangesAsync();
            }

            using (var db = new FlowLineDbContext(options))
            {
                var relay = new RelayService(db, new RelayNotifier());
                var result = await relay.AdvanceAsync(workItem.Id, fixture.StationA1.Id);

                Assert.Equal(AdvanceOutcome.Advanced, result.Outcome);
                Assert.Equal("A1", result.CompletedStep.Name);
            }

            using (var db = new FlowLineDbContext(options))
            {
                var reloaded = await db.WorkItems
                    .Include(wi => wi.StepCompletions)
                    .SingleAsync(wi => wi.Id == workItem.Id);

                Assert.Equal(WorkItemStatus.InProgress, reloaded.Status);
                Assert.Equal(fixture.StageA.Id, reloaded.CurrentStageId);
                Assert.Single(reloaded.StepCompletions);
            }
        }
    }

    [Fact]
    public async Task AdvanceAsync_LastStepOfNonFinalStage_HandsOffToNextStageQueue()
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
                workItem.Status = WorkItemStatus.InProgress;
                workItem.ClaimedByStationId = fixture.StationA1.Id;
                db.WorkItems.Add(workItem);
                await db.SaveChangesAsync();
            }

            using (var db = new FlowLineDbContext(options))
            {
                var relay = new RelayService(db, new RelayNotifier());
                await relay.AdvanceAsync(workItem.Id, fixture.StationA1.Id); // completes A1
                var result = await relay.AdvanceAsync(workItem.Id, fixture.StationA1.Id); // completes A2 (last)

                Assert.Equal(AdvanceOutcome.HandedOff, result.Outcome);
                Assert.Equal("A2", result.CompletedStep.Name);
            }

            using (var db = new FlowLineDbContext(options))
            {
                var reloaded = await db.WorkItems
                    .Include(wi => wi.StepCompletions)
                    .SingleAsync(wi => wi.Id == workItem.Id);

                Assert.Equal(fixture.StageB.Id, reloaded.CurrentStageId);
                Assert.Equal(WorkItemStatus.Queued, reloaded.Status);
                Assert.Null(reloaded.ClaimedByStationId);
                Assert.Equal(2, reloaded.StepCompletions.Count);

                var queueDepth = await new RelayService(db, new RelayNotifier()).GetQueueDepthAsync(fixture.StageB.Id);
                Assert.Equal(1, queueDepth);
            }
        }
    }

    [Fact]
    public async Task AdvanceAsync_LastStepOfFinalStage_MarksWorkItemCompleted()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            RelayFixtureContext fixture;
            WorkItem workItem;
            using (var db = new FlowLineDbContext(options))
            {
                fixture = await RelayTestFixture.SeedAsync(db);
                workItem = RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageB, DateTime.UtcNow);
                workItem.Status = WorkItemStatus.InProgress;
                workItem.ClaimedByStationId = fixture.StationB.Id;
                db.WorkItems.Add(workItem);
                await db.SaveChangesAsync();
            }

            using (var db = new FlowLineDbContext(options))
            {
                var relay = new RelayService(db, new RelayNotifier());
                var result = await relay.AdvanceAsync(workItem.Id, fixture.StationB.Id); // completes B1 (only + last step)

                Assert.Equal(AdvanceOutcome.Completed, result.Outcome);
            }

            using (var db = new FlowLineDbContext(options))
            {
                var reloaded = await db.WorkItems.SingleAsync(wi => wi.Id == workItem.Id);
                Assert.Equal(WorkItemStatus.Completed, reloaded.Status);
                Assert.Null(reloaded.ClaimedByStationId);
            }
        }
    }

    [Fact]
    public async Task AdvanceAsync_WorkItemNotClaimedByGivenStation_Throws()
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
                workItem.Status = WorkItemStatus.InProgress;
                workItem.ClaimedByStationId = fixture.StationA1.Id;
                db.WorkItems.Add(workItem);
                await db.SaveChangesAsync();
            }

            using var db2 = new FlowLineDbContext(options);
            var relay = new RelayService(db2, new RelayNotifier());

            await Assert.ThrowsAsync<RelayOperationException>(
                () => relay.AdvanceAsync(workItem.Id, fixture.StationA2.Id));
        }
    }

    [Fact]
    public async Task AdvanceAsync_WorkItemNotInProgress_Throws()
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
                db.WorkItems.Add(workItem); // still Queued, never claimed
                await db.SaveChangesAsync();
            }

            using var db2 = new FlowLineDbContext(options);
            var relay = new RelayService(db2, new RelayNotifier());

            await Assert.ThrowsAsync<RelayOperationException>(
                () => relay.AdvanceAsync(workItem.Id, fixture.StationA1.Id));
        }
    }

    [Fact]
    public async Task ClaimNextAsync_SuccessfulClaim_NotifiesTheStation_Stage()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var fixture = await RelayTestFixture.SeedAsync(db);
            db.WorkItems.Add(RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, DateTime.UtcNow));
            await db.SaveChangesAsync();

            var notifier = new RelayNotifier();
            var notified = new List<int>();
            notifier.StageChanged += stageId => notified.Add(stageId);
            var relay = new RelayService(db, notifier);

            await relay.ClaimNextAsync(fixture.StationA1.Id);

            Assert.Equal([fixture.StageA.Id], notified);
        }
    }

    [Fact]
    public async Task AdvanceAsync_HandOff_NotifiesTheNextStage_ButNotOnNonFinalStep()
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
                workItem.Status = WorkItemStatus.InProgress;
                workItem.ClaimedByStationId = fixture.StationA1.Id;
                db.WorkItems.Add(workItem);
                await db.SaveChangesAsync();
            }

            using var db2 = new FlowLineDbContext(options);
            var notifier = new RelayNotifier();
            var notified = new List<int>();
            notifier.StageChanged += stageId => notified.Add(stageId);
            var relay = new RelayService(db2, notifier);

            await relay.AdvanceAsync(workItem.Id, fixture.StationA1.Id); // A1: not last step
            Assert.Empty(notified);

            await relay.AdvanceAsync(workItem.Id, fixture.StationA1.Id); // A2: last step, hands off to Stage B
            Assert.Equal([fixture.StageB.Id], notified);
        }
    }
}
