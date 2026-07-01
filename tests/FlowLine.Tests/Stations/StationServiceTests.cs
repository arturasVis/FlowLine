using FlowLine.Application.Stations;
using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using FlowLine.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Tests.Stations;

public class StationServiceTests
{
    [Fact]
    public async Task CreateStationAsync_BindsStationToStage()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var workflow = new Workflow { Name = "RMA Teardown" };
            var stage = new Stage { Workflow = workflow, Name = "Inspect", OrderIndex = 0 };
            workflow.Stages.Add(stage);
            db.Workflows.Add(workflow);
            await db.SaveChangesAsync();

            var service = new StationService(db);

            var station = await service.CreateStationAsync(stage.Id, "Inspection Bench 1");

            Assert.Equal(stage.Id, station.StageId);
            var reloaded = await db.Stations.SingleAsync(s => s.Id == station.Id);
            Assert.Equal("Inspection Bench 1", reloaded.Name);
        }
    }

    [Fact]
    public async Task CreateStationAsync_NonexistentStage_Throws()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var service = new StationService(db);

            await Assert.ThrowsAsync<StationServiceException>(
                () => service.CreateStationAsync(999, "Ghost Station"));
        }
    }

    [Fact]
    public async Task UpdateStationAsync_ChangesName()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var workflow = new Workflow { Name = "RMA Teardown" };
            var stage = new Stage { Workflow = workflow, Name = "Inspect", OrderIndex = 0 };
            workflow.Stages.Add(stage);
            db.Workflows.Add(workflow);
            await db.SaveChangesAsync();

            var service = new StationService(db);
            var station = await service.CreateStationAsync(stage.Id, "Bench 1");

            await service.UpdateStationAsync(station.Id, "Bench 1 (Renamed)");

            var reloaded = await db.Stations.SingleAsync(s => s.Id == station.Id);
            Assert.Equal("Bench 1 (Renamed)", reloaded.Name);
        }
    }

    [Fact]
    public async Task DeleteStationAsync_UnclaimedStation_Succeeds()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var workflow = new Workflow { Name = "RMA Teardown" };
            var stage = new Stage { Workflow = workflow, Name = "Inspect", OrderIndex = 0 };
            workflow.Stages.Add(stage);
            db.Workflows.Add(workflow);
            await db.SaveChangesAsync();

            var service = new StationService(db);
            var station = await service.CreateStationAsync(stage.Id, "Bench 1");

            await service.DeleteStationAsync(station.Id);

            Assert.False(await db.Stations.AnyAsync(s => s.Id == station.Id));
        }
    }

    [Fact]
    public async Task DeleteStationAsync_StationWithClaimedWorkItem_ThrowsAndLeavesItIntact()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var workflow = new Workflow { Name = "RMA Teardown" };
            var stage = new Stage { Workflow = workflow, Name = "Inspect", OrderIndex = 0 };
            workflow.Stages.Add(stage);
            db.Workflows.Add(workflow);
            await db.SaveChangesAsync();

            var service = new StationService(db);
            var station = await service.CreateStationAsync(stage.Id, "Bench 1");

            // Separate DbContext, mirroring how a different request actually claimed this
            // unit — sharing `db`'s tracker would make EF's in-memory relationship-severed
            // check mask the real FK Restrict guard this test means to exercise (see
            // WorkflowBuilderServiceTests for the same pattern and why).
            using (var seedDb = new FlowLineDbContext(options))
            {
                seedDb.WorkItems.Add(new WorkItem
                {
                    WorkflowId = workflow.Id,
                    CurrentStageId = stage.Id,
                    OrderNumber = "ORD-1",
                    Sku = "SKU-1",
                    Quantity = 1,
                    Status = WorkItemStatus.InProgress,
                    ClaimedByStationId = station.Id,
                });
                await seedDb.SaveChangesAsync();
            }

            await Assert.ThrowsAsync<StationServiceException>(() => service.DeleteStationAsync(station.Id));

            Assert.True(await db.Stations.AnyAsync(s => s.Id == station.Id));
        }
    }

    [Fact]
    public async Task GetStationsAsync_ReturnsStationsWithStageAndWorkflowLoaded()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var workflow = new Workflow { Name = "RMA Teardown" };
            var stage = new Stage { Workflow = workflow, Name = "Inspect", OrderIndex = 0 };
            workflow.Stages.Add(stage);
            db.Workflows.Add(workflow);
            await db.SaveChangesAsync();

            var service = new StationService(db);
            await service.CreateStationAsync(stage.Id, "Bench 1");

            var stations = await service.GetStationsAsync();

            var loaded = Assert.Single(stations);
            Assert.Equal("Inspect", loaded.Stage.Name);
            Assert.Equal("RMA Teardown", loaded.Stage.Workflow.Name);
        }
    }
}
