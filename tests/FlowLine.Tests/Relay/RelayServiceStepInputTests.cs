using FlowLine.Application.Relay;
using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using FlowLine.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Tests.Relay;

public class RelayServiceStepInputTests
{
    // Seeds the fixture, attaches the given inputs to step A1, queues one unit at Stage A and
    // claims it at Station A1 — leaving the caller ready to AdvanceAsync through A1.
    private static async Task<(RelayFixtureContext Fixture, Step StepA1, WorkItem Unit)> SetupClaimedAsync(
        FlowLineDbContext db, params StepInput[] inputs)
    {
        var fixture = await RelayTestFixture.SeedAsync(db);
        var stepA1 = await db.Steps.SingleAsync(s => s.Name == "A1");
        foreach (var input in inputs)
        {
            input.StepId = stepA1.Id;
            db.StepInputs.Add(input);
        }
        db.WorkItems.Add(RelayTestFixture.NewQueuedWorkItem(fixture.Workflow, fixture.StageA, DateTime.UtcNow));
        await db.SaveChangesAsync();

        var relay = new RelayService(db, new RelayNotifier());
        var unit = await relay.ClaimNextAsync(fixture.StationA1.Id)
                   ?? throw new InvalidOperationException("claim failed");
        return (fixture, stepA1, unit);
    }

    [Fact]
    public async Task Advance_WithInputs_PersistsEveryAnsweredValue()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var (fixture, stepA1, unit) = await SetupClaimedAsync(db,
                new StepInput { Label = "Serial", Type = StepInputType.Text, Required = true, OrderIndex = 0 },
                new StepInput { Label = "Boot", Type = StepInputType.PassFail, Required = true, OrderIndex = 1 },
                new StepInput { Label = "Temp", Type = StepInputType.Number, OrderIndex = 2 },
                new StepInput { Label = "Checks", Type = StepInputType.Checklist, Options = "a\nb", OrderIndex = 3 });

            var serial = await db.StepInputs.SingleAsync(i => i.Label == "Serial");
            var boot = await db.StepInputs.SingleAsync(i => i.Label == "Boot");
            var temp = await db.StepInputs.SingleAsync(i => i.Label == "Temp");
            var checks = await db.StepInputs.SingleAsync(i => i.Label == "Checks");

            var relay = new RelayService(db, new RelayNotifier());
            await relay.AdvanceAsync(unit.Id, fixture.StationA1.Id, staffNumber: 1001, inputValues:
            [
                new StepInputValue(serial.Id, "SN-123"),
                new StepInputValue(boot.Id, "Pass"),
                new StepInputValue(temp.Id, "42.5"),
                new StepInputValue(checks.Id, "a"),
            ]);

            var completion = await db.StepCompletions
                .Include(sc => sc.Values)
                .SingleAsync(sc => sc.StepId == stepA1.Id && sc.WorkItemId == unit.Id);
            Assert.Equal(4, completion.Values.Count);
            Assert.Equal("SN-123", completion.Values.Single(v => v.StepInputId == serial.Id).Value);
            Assert.Equal("Pass", completion.Values.Single(v => v.StepInputId == boot.Id).Value);
            Assert.Equal("42.5", completion.Values.Single(v => v.StepInputId == temp.Id).Value);
            Assert.Equal("a", completion.Values.Single(v => v.StepInputId == checks.Id).Value);
        }
    }

    [Fact]
    public async Task Advance_RequiredInputEmpty_ThrowsAndRecordsNothing()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var (fixture, stepA1, unit) = await SetupClaimedAsync(db,
                new StepInput { Label = "Serial", Type = StepInputType.Text, Required = true, OrderIndex = 0 });
            var serial = await db.StepInputs.SingleAsync(i => i.Label == "Serial");

            var relay = new RelayService(db, new RelayNotifier());
            await Assert.ThrowsAsync<RelayOperationException>(() =>
                relay.AdvanceAsync(unit.Id, fixture.StationA1.Id, inputValues:
                    [new StepInputValue(serial.Id, "   ")]));

            Assert.Equal(0, await db.StepCompletions.CountAsync());
            Assert.Equal(0, await db.StepCompletionValues.CountAsync());
        }
    }

    [Fact]
    public async Task Advance_NumberInputNotNumeric_Throws()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var (fixture, _, unit) = await SetupClaimedAsync(db,
                new StepInput { Label = "Temp", Type = StepInputType.Number, OrderIndex = 0 });
            var temp = await db.StepInputs.SingleAsync(i => i.Label == "Temp");

            var relay = new RelayService(db, new RelayNotifier());
            await Assert.ThrowsAsync<RelayOperationException>(() =>
                relay.AdvanceAsync(unit.Id, fixture.StationA1.Id, inputValues:
                    [new StepInputValue(temp.Id, "not-a-number")]));

            Assert.Equal(0, await db.StepCompletions.CountAsync());
        }
    }

    [Fact]
    public async Task Advance_OptionalInputsBlank_AllowedAndStoresNothingForThem()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var (fixture, stepA1, unit) = await SetupClaimedAsync(db,
                new StepInput { Label = "Note", Type = StepInputType.Text, Required = false, OrderIndex = 0 });

            var relay = new RelayService(db, new RelayNotifier());
            // No values supplied at all — an optional input just records nothing.
            await relay.AdvanceAsync(unit.Id, fixture.StationA1.Id);

            var completion = await db.StepCompletions
                .Include(sc => sc.Values)
                .SingleAsync(sc => sc.StepId == stepA1.Id);
            Assert.Empty(completion.Values);
        }
    }

    [Fact]
    public async Task Advance_ValuesForUnknownInputIds_AreIgnored()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var (fixture, _, unit) = await SetupClaimedAsync(db,
                new StepInput { Label = "Serial", Type = StepInputType.Text, OrderIndex = 0 });
            var serial = await db.StepInputs.SingleAsync(i => i.Label == "Serial");

            var relay = new RelayService(db, new RelayNotifier());
            await relay.AdvanceAsync(unit.Id, fixture.StationA1.Id, inputValues:
            [
                new StepInputValue(serial.Id, "SN-1"),
                new StepInputValue(99999, "stray"), // not an input of this step
            ]);

            var values = await db.StepCompletionValues.ToListAsync();
            Assert.Equal(new[] { serial.Id }, values.Select(v => v.StepInputId));
        }
    }
}
