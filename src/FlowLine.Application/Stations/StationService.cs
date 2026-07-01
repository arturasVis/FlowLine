using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Application.Stations;

public class StationService(FlowLineDbContext db) : IStationService
{
    public Task<List<Station>> GetStationsAsync(CancellationToken cancellationToken = default)
    {
        return db.Stations
            .Include(s => s.Stage).ThenInclude(st => st.Workflow)
            .OrderBy(s => s.Stage.Workflow.Name)
            .ThenBy(s => s.Stage.OrderIndex)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Station> CreateStationAsync(int stageId, string name, CancellationToken cancellationToken = default)
    {
        _ = await db.Stages.FindAsync([stageId], cancellationToken)
            ?? throw new StationServiceException($"Stage {stageId} does not exist.");

        var station = new Station { StageId = stageId, Name = name };
        db.Stations.Add(station);
        await db.SaveChangesAsync(cancellationToken);
        return station;
    }

    public async Task UpdateStationAsync(int stationId, string name, CancellationToken cancellationToken = default)
    {
        var station = await db.Stations.FindAsync([stationId], cancellationToken)
            ?? throw new StationServiceException($"Station {stationId} does not exist.");
        station.Name = name;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteStationAsync(int stationId, CancellationToken cancellationToken = default)
    {
        var station = await db.Stations.FindAsync([stationId], cancellationToken)
            ?? throw new StationServiceException($"Station {stationId} does not exist.");

        db.Stations.Remove(station);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new StationServiceException(
                "Can't delete this station — it currently has a work item claimed.");
        }
    }
}
