using FlowLine.Application.Stats;
using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using FlowLine.Tests.Data;
using FlowLine.Tests.Relay;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Tests.Stats;

/// <summary>
/// Exact-value tests over hand-crafted timestamps: the stats math (per-unit hands-on/lead
/// time, per-step and per-SKU averages, staff attribution and vs-average deltas) and the
/// completed-units-only population rule.
/// </summary>
public class StatsServiceTests
{
    // Midday UTC so the local-date grouping of UnitsPerDay can't straddle midnight in any
    // test-runner timezone within ±10h of UTC.
    private static readonly DateTime T0 = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetWorkflowStatsAsync_ComputesExactAverages_PerUnitStepSkuAndStaff()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            RelayFixtureContext fixture;
            using (var db = new FlowLineDbContext(options))
            {
                fixture = await RelayTestFixture.SeedAsync(db);
                await SqliteTestDatabase.CreateExternalTablesAsync(db);
                await db.Database.ExecuteSqlRawAsync(
                    """INSERT INTO "StaffTable" ("StaffNumber", "Name", "TestingPower") VALUES (1001, 'Alice', 1), (1002, 'Bob', 1);""");

                var (a1, a2) = (StepOf(fixture.StageA, "A1"), StepOf(fixture.StageA, "A2"));
                var b1 = StepOf(fixture.StageB, "B1");

                // Unit 1 (SKU-X): A1 2m + A2 3m by Alice, B1 4m by Bob → hands-on 9m, lead 14m.
                db.WorkItems.Add(CompletedUnit(fixture, "ORD-1", "SKU-X", T0,
                    (a1, T0.AddMinutes(1), T0.AddMinutes(3), 1001),
                    (a2, T0.AddMinutes(3), T0.AddMinutes(6), 1001),
                    (b1, T0.AddMinutes(10), T0.AddMinutes(14), 1002)));

                // Unit 2 (SKU-Y): A1 4m by Alice, A2 5m by nobody, B1 6m by Bob → hands-on 15m, lead 18m.
                db.WorkItems.Add(CompletedUnit(fixture, "ORD-2", "SKU-Y", T0,
                    (a1, T0.AddMinutes(2), T0.AddMinutes(6), 1001),
                    (a2, T0.AddMinutes(6), T0.AddMinutes(11), null),
                    (b1, T0.AddMinutes(12), T0.AddMinutes(18), 1002)));

                await db.SaveChangesAsync();
            }

            using (var db = new FlowLineDbContext(options))
            {
                var stats = await new StatsService(db).GetWorkflowStatsAsync(fixture.Workflow.Id, null, null);

                Assert.NotNull(stats);
                Assert.Equal(2, stats.UnitsCompleted);
                Assert.Equal(TimeSpan.FromMinutes(12), stats.AvgHandsOnPerUnit); // (9 + 15) / 2
                Assert.Equal(TimeSpan.FromMinutes(16), stats.AvgLeadTimePerUnit); // (14 + 18) / 2

                Assert.Equal(["A1", "A2", "B1"], stats.StepAverages.Select(sa => sa.StepName));
                Assert.Equal(TimeSpan.FromMinutes(3), stats.StepAverages[0].AvgDuration); // (2 + 4) / 2
                Assert.Equal(TimeSpan.FromMinutes(4), stats.StepAverages[1].AvgDuration); // (3 + 5) / 2
                Assert.Equal(TimeSpan.FromMinutes(5), stats.StepAverages[2].AvgDuration); // (4 + 6) / 2
                Assert.All(stats.StepAverages, sa => Assert.Equal(2, sa.TotalCount));

                Assert.Equal(2, stats.Skus.Count);
                var skuX = Assert.Single(stats.Skus, s => s.Sku == "SKU-X");
                Assert.Equal(1, skuX.UnitsCompleted);
                Assert.Equal(TimeSpan.FromMinutes(9), skuX.AvgHandsOnPerUnit);
                Assert.Equal(TimeSpan.FromMinutes(14), skuX.AvgLeadTimePerUnit);

                var alice = Assert.Single(stats.Staff, s => s.StaffNumber == 1001);
                Assert.Equal("Alice", alice.Name);
                Assert.Equal(3, alice.StepsCompleted); // A1 twice + A2 once
                Assert.Equal(2, alice.UnitsTouched);
                Assert.Equal(TimeSpan.FromMinutes(3), alice.AvgStepDuration); // (2 + 3 + 4) / 3

                var bob = Assert.Single(stats.Staff, s => s.StaffNumber == 1002);
                Assert.Equal(2, bob.StepsCompleted);
                Assert.Equal(TimeSpan.FromMinutes(5), bob.AvgStepDuration); // (4 + 6) / 2

                var day = Assert.Single(stats.UnitsPerDay);
                Assert.Equal(2, day.Count);
                Assert.Equal(DateOnly.FromDateTime(T0.AddMinutes(18).ToLocalTime()), day.Date);
            }
        }
    }

    [Fact]
    public async Task GetWorkflowStatsAsync_OnlyCompletedUnitsInsideRangeCount()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            RelayFixtureContext fixture;
            using (var db = new FlowLineDbContext(options))
            {
                fixture = await RelayTestFixture.SeedAsync(db);
                await SqliteTestDatabase.CreateExternalTablesAsync(db);

                var a1 = StepOf(fixture.StageA, "A1");

                // Completed on day 0, day 4, and one unit still in progress with a completion.
                db.WorkItems.Add(CompletedUnit(fixture, "ORD-EARLY", "SKU-X", T0,
                    (a1, T0, T0.AddMinutes(5), 1001)));
                db.WorkItems.Add(CompletedUnit(fixture, "ORD-LATE", "SKU-X", T0.AddDays(4),
                    (a1, T0.AddDays(4), T0.AddDays(4).AddMinutes(5), 1001)));
                var inProgress = CompletedUnit(fixture, "ORD-WIP", "SKU-X", T0.AddDays(4),
                    (a1, T0.AddDays(4), T0.AddDays(4).AddMinutes(5), 1001));
                inProgress.Status = WorkItemStatus.InProgress;
                db.WorkItems.Add(inProgress);

                await db.SaveChangesAsync();
            }

            using (var db = new FlowLineDbContext(options))
            {
                var service = new StatsService(db);

                var all = await service.GetWorkflowStatsAsync(fixture.Workflow.Id, null, null);
                Assert.Equal(2, all!.UnitsCompleted); // in-progress unit invisible even unbounded

                var windowed = await service.GetWorkflowStatsAsync(
                    fixture.Workflow.Id, T0.AddDays(3), T0.AddDays(5));
                Assert.Equal(1, windowed!.UnitsCompleted);
                Assert.Equal("SKU-X", Assert.Single(windowed.Skus).Sku);

                var empty = await service.GetWorkflowStatsAsync(
                    fixture.Workflow.Id, T0.AddDays(10), T0.AddDays(11));
                Assert.Equal(0, empty!.UnitsCompleted);
                Assert.Empty(empty.StepAverages);
                Assert.Empty(empty.UnitsPerDay);
            }
        }
    }

    [Fact]
    public async Task GetWorkflowSummariesAsync_ListsEveryWorkflow_IncludingOnesWithNoData()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            RelayFixtureContext fixture;
            using (var db = new FlowLineDbContext(options))
            {
                fixture = await RelayTestFixture.SeedAsync(db);
                db.Workflows.Add(new Workflow { Name = "Empty Workflow" });
                db.WorkItems.Add(CompletedUnit(fixture, "ORD-1", "SKU-X", T0,
                    (StepOf(fixture.StageA, "A1"), T0, T0.AddMinutes(5), 1001)));
                await db.SaveChangesAsync();
            }

            using (var db = new FlowLineDbContext(options))
            {
                var summaries = await new StatsService(db).GetWorkflowSummariesAsync(null, null);

                Assert.Equal(2, summaries.Count);
                var active = Assert.Single(summaries, s => s.WorkflowName == "Test Workflow");
                Assert.Equal(1, active.UnitsCompleted);
                Assert.Equal(TimeSpan.FromMinutes(5), active.AvgHandsOnPerUnit);

                var empty = Assert.Single(summaries, s => s.WorkflowName == "Empty Workflow");
                Assert.Equal(0, empty.UnitsCompleted);
                Assert.Null(empty.AvgHandsOnPerUnit);
                Assert.Null(empty.AvgLeadTimePerUnit);
            }
        }
    }

    [Fact]
    public async Task GetStaffStatsAsync_ComparesAgainstWorkflowAverage_MixAdjusted()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            RelayFixtureContext fixture;
            using (var db = new FlowLineDbContext(options))
            {
                fixture = await RelayTestFixture.SeedAsync(db);
                await SqliteTestDatabase.CreateExternalTablesAsync(db);
                await db.Database.ExecuteSqlRawAsync(
                    """INSERT INTO "StaffTable" ("StaffNumber", "Name", "TestingPower") VALUES (1001, 'Alice', 1);""");

                var a1 = StepOf(fixture.StageA, "A1");

                // A1 across three units: Alice 2m and 4m, staff 1002 6m → workflow avg 4m,
                // Alice's avg 3m → 25% faster.
                db.WorkItems.Add(CompletedUnit(fixture, "ORD-1", "SKU-X", T0, (a1, T0, T0.AddMinutes(2), 1001)));
                db.WorkItems.Add(CompletedUnit(fixture, "ORD-2", "SKU-X", T0, (a1, T0, T0.AddMinutes(4), 1001)));
                db.WorkItems.Add(CompletedUnit(fixture, "ORD-3", "SKU-Y", T0, (a1, T0, T0.AddMinutes(6), 1002)));

                await db.SaveChangesAsync();
            }

            using (var db = new FlowLineDbContext(options))
            {
                var stats = await new StatsService(db).GetStaffStatsAsync(fixture.Workflow.Id, 1001, null, null);

                Assert.NotNull(stats);
                Assert.Equal("Alice", stats.Name);
                Assert.Equal(2, stats.StepsCompleted);
                Assert.Equal(2, stats.UnitsTouched);
                Assert.Equal(TimeSpan.FromMinutes(3), stats.AvgStepDuration);
                Assert.Equal(TimeSpan.FromMinutes(4), stats.WorkflowAvgStepDuration);

                var comparison = Assert.Single(stats.StepComparisons);
                Assert.Equal("A1", comparison.StepName);
                Assert.Equal(TimeSpan.FromMinutes(3), comparison.StaffAvg);
                Assert.Equal(TimeSpan.FromMinutes(4), comparison.WorkflowAvg);
                Assert.NotNull(comparison.DeltaPercent);
                Assert.Equal(-25.0, comparison.DeltaPercent!.Value, precision: 5);
                Assert.Equal(2, comparison.TimesDone);

                // Mix-adjusted overall: actual 6m vs expected 2 × 4m = 8m → also -25%.
                Assert.Equal(-25.0, stats.OverallDeltaPercent!.Value, precision: 5);

                var sku = Assert.Single(stats.Skus);
                Assert.Equal("SKU-X", sku.Sku);
                Assert.Equal(2, sku.UnitsTouched);
                Assert.Equal(2, sku.StepsCompleted);

                // Unknown operator on the same workflow: zero counts, not an error.
                var stranger = await new StatsService(db).GetStaffStatsAsync(fixture.Workflow.Id, 9999, null, null);
                Assert.NotNull(stranger);
                Assert.Equal(0, stranger.StepsCompleted);
                Assert.Null(stranger.OverallDeltaPercent);
            }
        }
    }

    [Fact]
    public async Task GetWorkflowStatsAsync_LegacyCompletionsWithoutStartTime_CountButDontSkewAverages()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            RelayFixtureContext fixture;
            using (var db = new FlowLineDbContext(options))
            {
                fixture = await RelayTestFixture.SeedAsync(db);
                await SqliteTestDatabase.CreateExternalTablesAsync(db);

                // A1 has no recorded start (pre-tracking row); A2 is timed at 3m.
                db.WorkItems.Add(CompletedUnit(fixture, "ORD-1", "SKU-X", T0,
                    (StepOf(fixture.StageA, "A1"), null, T0.AddMinutes(5), 1001),
                    (StepOf(fixture.StageA, "A2"), T0.AddMinutes(5), T0.AddMinutes(8), 1001)));
                await db.SaveChangesAsync();
            }

            using (var db = new FlowLineDbContext(options))
            {
                var stats = await new StatsService(db).GetWorkflowStatsAsync(fixture.Workflow.Id, null, null);

                Assert.NotNull(stats);
                Assert.Equal(TimeSpan.FromMinutes(3), stats.AvgHandsOnPerUnit); // only the timed step

                var a1 = Assert.Single(stats.StepAverages, sa => sa.StepName == "A1");
                Assert.Null(a1.AvgDuration);
                Assert.Equal(0, a1.TimedCount);
                Assert.Equal(1, a1.TotalCount);

                var alice = Assert.Single(stats.Staff);
                Assert.Equal(2, alice.StepsCompleted); // untimed step still counts as work done
                Assert.Equal(TimeSpan.FromMinutes(3), alice.AvgStepDuration);
            }
        }
    }

    [Fact]
    public async Task UnknownWorkflow_ReturnsNull()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var service = new StatsService(db);

            Assert.Null(await service.GetWorkflowStatsAsync(12345, null, null));
            Assert.Null(await service.GetStaffStatsAsync(12345, 1001, null, null));
        }
    }

    private static Step StepOf(Stage stage, string name) => stage.Steps.Single(s => s.Name == name);

    /// <summary>A Completed WorkItem with hand-set completions — exact timestamps in, exact averages out.</summary>
    private static WorkItem CompletedUnit(
        RelayFixtureContext fixture, string orderNumber, string sku, DateTime createdAtUtc,
        params (Step Step, DateTime? StartedAtUtc, DateTime CompletedAtUtc, int? StaffNumber)[] completions)
    {
        var unit = new WorkItem
        {
            Workflow = fixture.Workflow,
            CurrentStage = fixture.StageB,
            OrderNumber = orderNumber,
            Sku = sku,
            Quantity = 1,
            Status = WorkItemStatus.Completed,
            CreatedAtUtc = createdAtUtc,
            QueuedAtUtc = createdAtUtc,
        };
        foreach (var (step, startedAtUtc, completedAtUtc, staffNumber) in completions)
        {
            unit.StepCompletions.Add(new StepCompletion
            {
                WorkItem = unit,
                Step = step,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc,
                CompletedByStaffNumber = staffNumber,
            });
        }
        return unit;
    }
}
