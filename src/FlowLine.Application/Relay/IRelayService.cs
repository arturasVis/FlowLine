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
    /// <param name="inputValues">
    /// The operator's answers to the step's configured <see cref="Domain.Entities.StepInput"/>s.
    /// Every required input must have a non-empty value and every Number input must parse, or the
    /// call fails with <see cref="RelayOperationException"/> and nothing is recorded. Values for
    /// ids that aren't inputs of this step are ignored.
    /// </param>
    Task<AdvanceResult> AdvanceAsync(int workItemId, int? stationId, int? staffNumber = null, IReadOnlyCollection<StepInputValue>? inputValues = null, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Routes a unit from a fork stage down the chosen branch, once every step of that stage is
    /// done. The branch's target may be Finish (marks the unit Completed), a later stage, or an
    /// earlier stage (a rework loop) — routing into a stage the unit has already passed resets that
    /// stage so its steps are redone. Requires the station to hold the unit's claim; fails with
    /// <see cref="RelayOperationException"/> otherwise, or if the stage's steps aren't all complete.
    /// </summary>
    Task<WorkItem> RouteAsync(int workItemId, int stationId, int branchId, CancellationToken cancellationToken = default);

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
    /// Claims the oldest queued WorkItem at a scan-required stage whose OrderNumber matches the
    /// scanned code. This is the stage-level right-unit gate: stations on such stages do not
    /// auto-claim; the scan chooses the specific queued unit to work.
    /// </summary>
    Task<WorkItem> ClaimByScanAsync(int stationId, string scannedCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a unit from a scanned ID at a prebuild-requiring workflow's first station. Two
    /// sources, in priority order: a WorkItem already Queued at this entry stage with that
    /// OrderNumber (authored on the Orders screen or imported) is claimed to the scanning
    /// station; otherwise the ID is looked up in the company History table (matched on
    /// OrderId), History.Qty physical-unit WorkItems are created (Quantity = 1 each), and one
    /// is returned already claimed. Fails with <see cref="RelayOperationException"/> if the ID
    /// matches neither, the station isn't the workflow's first stage, or that order is already
    /// being worked on with no entry-queued unit left to claim.
    /// </summary>
    Task<WorkItem> CreateFromPrebuildAsync(int stationId, string prebuildId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a fresh unit at an ad-hoc (free-run) workflow's first station: creates a WorkItem with
    /// a generated run number, already claimed and InProgress at the scanning station, so the
    /// operator begins immediately and the steps are timed like any other unit. For routines/training
    /// workflows (<see cref="Workflow.AllowAdHocStart"/>) that have no premade orders. Fails with
    /// <see cref="RelayOperationException"/> if the station isn't the workflow's first stage or the
    /// workflow isn't in ad-hoc mode.
    /// </summary>
    Task<WorkItem> StartAdHocAsync(int stationId, CancellationToken cancellationToken = default);
}
