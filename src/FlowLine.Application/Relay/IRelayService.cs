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
    /// <param name="staffNumber">
    /// Staff number of the operator completing the step, recorded on the StepCompletion for
    /// timing/attribution reporting. Null if no operator identity is available.
    /// </param>
    /// <param name="scannedCode">
    /// The barcode scanned by the operator. Required to match the WorkItem's OrderNumber
    /// (case-insensitive) when the step being completed has <c>RequiresScan</c>; ignored otherwise.
    /// </param>
    Task<AdvanceResult> AdvanceAsync(int workItemId, int? stationId, int? staffNumber = null, string? scannedCode = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases an InProgress WorkItem's claim back to the *end* of its current stage's queue
    /// (partial step progress is kept; a later claim resumes at the next outstanding step).
    /// For unsticking a claim — wrong scan, blocked unit, operator gone home. Pass a station
    /// ID to require that station holds the claim, or null for an admin override.
    /// </summary>
    Task ReleaseAsync(int workItemId, int? stationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an InProgress WorkItem back to an earlier stage for rework: deletes the
    /// StepCompletions of the target stage and every stage after it (that work is being
    /// redone), re-queues the unit at the target stage, and clears the claim. Earlier stages'
    /// completions and timing are untouched.
    /// </summary>
    Task SendBackAsync(int workItemId, int stationId, int targetStageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an InProgress WorkItem as Scrapped (failed and physically junked) and clears the
    /// claim. Terminal: it leaves the line and is excluded from stats; its order number can be
    /// scanned or imported again for a rebuild. Null station = admin override.
    /// </summary>
    Task ScrapAsync(int workItemId, int? stationId, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Starts a unit from a scanned ID at a prebuild-requiring workflow's first station. Two
    /// sources, in priority order: a WorkItem already Queued at this entry stage with that
    /// OrderNumber (authored on the Orders screen or imported) is claimed to the scanning
    /// station; otherwise the ID is looked up in the company History table (matched on
    /// OrderId) and a new WorkItem inheriting that row's SKU/qty/channel is created already
    /// claimed. Fails with <see cref="RelayOperationException"/> if the ID matches neither, the
    /// station isn't the workflow's first stage, or that order is already being worked on.
    /// </summary>
    Task<WorkItem> CreateFromPrebuildAsync(int stationId, string prebuildId, CancellationToken cancellationToken = default);
}
