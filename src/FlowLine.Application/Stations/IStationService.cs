using FlowLine.Domain.Entities;

namespace FlowLine.Application.Stations;

/// <summary>
/// Admin station setup (PRD §11.3) — binding a physical station to one stage of one
/// workflow. Without this, a workflow built in the workflow builder has nothing for a
/// worker to actually claim units at; it exists only as definition, never as a runnable line.
/// Deliberately separate from IRelayService, which only ever *reads* stations (for the
/// worker-facing station picker and runtime) and never creates/edits/deletes them.
/// </summary>
public interface IStationService
{
    /// <summary>Every station with its Stage and that Stage's Workflow loaded, ordered by workflow then stage then name.</summary>
    Task<List<Station>> GetStationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Binds a new station to a stage. Throws <see cref="StationServiceException"/> if the stage does not exist.</summary>
    Task<Station> CreateStationAsync(int stageId, string name, CancellationToken cancellationToken = default);

    Task UpdateStationAsync(int stationId, string name, CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="StationServiceException"/> if the station currently has a WorkItem claimed.</summary>
    Task DeleteStationAsync(int stationId, CancellationToken cancellationToken = default);
}
