using FlowLine.Domain.Entities;

namespace FlowLine.Application.Relay;

/// <summary>
/// The relay: atomic claiming of queued WorkItems and transactional hand-off between
/// stages. This is the central correctness surface of FlowLine — see PRD §6.5 and NFR-4.
/// </summary>
public interface IRelayService
{
    /// <summary>
    /// Atomically claims the oldest unclaimed Queued WorkItem at the station's stage, or
    /// returns null if the queue is empty. Safe for multiple stations on the same stage to
    /// call concurrently — exactly one claims any given WorkItem (FR-13).
    /// </summary>
    Task<WorkItem?> ClaimNextAsync(int stationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records completion of the next outstanding step of the WorkItem's current stage. If
    /// that was the stage's last step, hands the WorkItem off to the next stage's queue (or
    /// marks it Completed if there is no next stage) in the same transaction (FR-12, FR-14).
    /// </summary>
    /// <param name="stationId">
    /// If provided, the call fails with <see cref="RelayOperationException"/> unless this
    /// station currently holds the WorkItem's claim.
    /// </param>
    Task<AdvanceResult> AdvanceAsync(int workItemId, int? stationId, CancellationToken cancellationToken = default);

    /// <summary>Number of WorkItems currently Queued at a stage (FR-15).</summary>
    Task<int> GetQueueDepthAsync(int stageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A station with its Stage, that Stage's Workflow, and that Stage's Steps (with
    /// MediaAssets) loaded — everything a station screen needs to render (FR-9, FR-10).
    /// </summary>
    Task<Station?> GetStationAsync(int stationId, CancellationToken cancellationToken = default);

    /// <summary>All stations, for the station-selection screen — ordered by workflow, then stage, then name.</summary>
    Task<List<Station>> GetStationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The WorkItem currently claimed and InProgress at a station, with StepCompletions
    /// loaded, or null if the station has nothing claimed (e.g. after a page reload mid-stage).
    /// </summary>
    Task<WorkItem?> GetActiveWorkItemAsync(int stationId, CancellationToken cancellationToken = default);
}
