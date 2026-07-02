using FlowLine.Application.Relay;
using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using FlowLine.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Tests.Relay;

/// <summary>
/// Scan-required steps: AdvanceAsync refuses to complete a RequiresScan step unless the
/// scanned code matches the WorkItem's OrderNumber — enforced server-side, not just in the UI.
/// </summary>
public class RelayServiceScanStepTests
{
    private static async Task<(RelayFixtureContext Fixture, WorkItem Unit)> SeedClaimedWithScanStepAsync(FlowLineDbContext db)
    {
        var fixture = await RelayTestFixture.SeedAsync(db);
        // A1 (the stage's first step) requires the right-unit scan.
        fixture.StageA.Steps.Single(s => s.Name == "A1").RequiresScan = true;
        var unit = RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, DateTime.UtcNow, "ORD-SCAN");
        db.WorkItems.Add(unit);
        await db.SaveChangesAsync();

        var relay = new RelayService(db, new RelayNotifier());
        await relay.ClaimNextAsync(fixture.StationA1.Id);
        return (fixture, unit);
    }

    [Fact]
    public async Task Advance_ScanStep_NoCode_ThrowsAndRecordsNothing()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var (fixture, unit) = await SeedClaimedWithScanStepAsync(db);
            var relay = new RelayService(db, new RelayNotifier());

            await Assert.ThrowsAsync<RelayOperationException>(
                () => relay.AdvanceAsync(unit.Id, fixture.StationA1.Id));

            Assert.Equal(0, await db.StepCompletions.CountAsync());
        }
    }

    [Fact]
    public async Task Advance_ScanStep_WrongCode_Throws()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var (fixture, unit) = await SeedClaimedWithScanStepAsync(db);
            var relay = new RelayService(db, new RelayNotifier());

            var ex = await Assert.ThrowsAsync<RelayOperationException>(
                () => relay.AdvanceAsync(unit.Id, fixture.StationA1.Id, scannedCode: "ORD-OTHER"));
            Assert.Contains("ORD-OTHER", ex.Message);
            Assert.Contains("ORD-SCAN", ex.Message);
        }
    }

    [Fact]
    public async Task Advance_ScanStep_MatchingCode_Advances_CaseAndWhitespaceInsensitive()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var (fixture, unit) = await SeedClaimedWithScanStepAsync(db);
            var relay = new RelayService(db, new RelayNotifier());

            var result = await relay.AdvanceAsync(unit.Id, fixture.StationA1.Id, scannedCode: "  ord-scan  ");
            Assert.Equal("A1", result.CompletedStep.Name);

            // A2 doesn't require a scan — plain advance still works and the code is ignored there.
            var next = await relay.AdvanceAsync(unit.Id, fixture.StationA1.Id);
            Assert.Equal("A2", next.CompletedStep.Name);
        }
    }
}
