using FlowLine.Domain.Entities;
using FlowLine.Domain.Entities.External;
using FlowLine.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Application.Relay;

public class RelayService(FlowLineDbContext db, IRelayNotifier notifier) : IRelayService
{
    public async Task<WorkItem?> ClaimNextAsync(int stationId, CancellationToken cancellationToken = default)
    {
        var station = await db.Stations
            .Include(s => s.Stage)
            .FirstOrDefaultAsync(s => s.Id == stationId, cancellationToken)
            ?? throw new RelayOperationException($"Station {stationId} does not exist.");

        if (station.Stage.RequiresScan)
        {
            return null;
        }

        while (true)
        {
            var candidate = await db.WorkItems
                .Where(wi => wi.CurrentStageId == station.StageId
                    && wi.Status == WorkItemStatus.Queued
                    && wi.ClaimedByStationId == null)
                .OrderBy(wi => wi.QueuedAtUtc)
                .ThenBy(wi => wi.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (candidate is null)
            {
                return null;
            }

            candidate.ClaimedByStationId = stationId;
            candidate.ClaimedAtUtc = DateTime.UtcNow;
            candidate.Status = WorkItemStatus.InProgress;

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                notifier.NotifyStageChanged(station.StageId);
                return candidate;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another station's claim won the race between our read and write — the
                // guarded conditional update FR-13 requires. Drop the stale entity and
                // retry against whatever is now the oldest unclaimed candidate.
                db.Entry(candidate).State = EntityState.Detached;
            }
        }
    }

    public async Task<AdvanceResult> AdvanceAsync(int workItemId, int? stationId, int? staffNumber = null, IReadOnlyCollection<StepInputValue>? inputValues = null, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var workItem = await db.WorkItems
            .Include(wi => wi.StepCompletions)
            .Include(wi => wi.CurrentStage).ThenInclude(s => s.Steps).ThenInclude(st => st.Inputs)
            .Include(wi => wi.Workflow).ThenInclude(w => w.Stages)
            .SingleOrDefaultAsync(wi => wi.Id == workItemId, cancellationToken)
            ?? throw new RelayOperationException($"WorkItem {workItemId} does not exist.");

        if (workItem.Status != WorkItemStatus.InProgress)
        {
            throw new RelayOperationException(
                $"WorkItem {workItemId} is not in progress (status: {workItem.Status}).");
        }

        if (stationId is not null && workItem.ClaimedByStationId != stationId)
        {
            throw new RelayOperationException(
                $"WorkItem {workItemId} is not claimed by station {stationId}.");
        }

        var stage = workItem.CurrentStage;
        var orderedSteps = stage.Steps.OrderBy(s => s.OrderIndex).ToList();
        var completedStepIds = workItem.StepCompletions.Select(sc => sc.StepId).ToHashSet();
        var nextStep = orderedSteps.FirstOrDefault(s => !completedStepIds.Contains(s.Id))
            ?? throw new RelayOperationException(
                $"Stage {stage.Id} has no remaining steps for WorkItem {workItemId}.");

        // Validate the operator's answers against the step's configured inputs and turn them into
        // StepCompletionValue rows. Only values for this step's own inputs are kept; a required
        // input that's blank, or a Number that doesn't parse, aborts the whole advance.
        var providedByInputId = (inputValues ?? [])
            .GroupBy(v => v.StepInputId)
            .ToDictionary(g => g.Key, g => g.Last().Value ?? string.Empty);
        var capturedValues = new List<StepCompletionValue>();
        foreach (var input in nextStep.Inputs.OrderBy(i => i.OrderIndex))
        {
            var value = (providedByInputId.GetValueOrDefault(input.Id) ?? string.Empty).Trim();
            if (input.Required && value.Length == 0)
            {
                throw new RelayOperationException($"'{input.Label}' is required.");
            }
            if (value.Length > 0 && input.Type == StepInputType.Number
                && !decimal.TryParse(value, out _))
            {
                throw new RelayOperationException($"'{input.Label}' must be a number.");
            }
            if (value.Length > 0)
            {
                capturedValues.Add(new StepCompletionValue { StepInputId = input.Id, Value = value });
            }
        }

        // The step began when the previous step at this stage finished — or, for the stage's
        // first step, when the claim was taken. Taking the LATER of the two matters after a
        // release/re-claim: the first step worked after resuming began at the new claim, not at
        // the pre-release completion (else the idle gap would count as work time). QueuedAtUtc
        // is the last-resort fallback for WorkItems claimed before ClaimedAtUtc existed; it
        // overstates by the queue wait.
        var stageStepIds = orderedSteps.Select(s => s.Id).ToHashSet();
        var lastStageCompletion = workItem.StepCompletions
            .Where(sc => stageStepIds.Contains(sc.StepId))
            .Select(sc => (DateTime?)sc.CompletedAtUtc)
            .Max();
        var startedAtUtc = Max(lastStageCompletion, workItem.ClaimedAtUtc) ?? workItem.QueuedAtUtc;

        db.StepCompletions.Add(new StepCompletion
        {
            WorkItemId = workItem.Id,
            StepId = nextStep.Id,
            StationId = stationId,
            CompletedByStaffNumber = staffNumber,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTime.UtcNow,
            Values = capturedValues,
        });

        var outcome = AdvanceOutcome.Advanced;

        if (nextStep.Id == orderedSteps[^1].Id)
        {
            var nextStage = workItem.Workflow.Stages
                .Where(s => s.OrderIndex > stage.OrderIndex)
                .OrderBy(s => s.OrderIndex)
                .FirstOrDefault();

            if (nextStage is null)
            {
                workItem.Status = WorkItemStatus.Completed;
                workItem.ClaimedByStationId = null;
                workItem.ClaimedAtUtc = null;
                outcome = AdvanceOutcome.Completed;
            }
            else
            {
                workItem.CurrentStageId = nextStage.Id;
                workItem.Status = WorkItemStatus.Queued;
                workItem.ClaimedByStationId = null;
                workItem.ClaimedAtUtc = null;
                workItem.QueuedAtUtc = DateTime.UtcNow;
                outcome = AdvanceOutcome.HandedOff;
            }
        }

        // One SaveChanges call writing both the new StepCompletion and the WorkItem's
        // status/stage/claim is already one implicit transaction; the explicit transaction
        // here just makes that guarantee (FR-14) visible at a glance rather than relying on
        // EF Core's default behavior.
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (outcome is AdvanceOutcome.HandedOff)
        {
            notifier.NotifyStageChanged(workItem.CurrentStageId);
        }
        else if (outcome is AdvanceOutcome.Completed)
        {
            // Nothing queues anywhere on completion, but the live line overview counts
            // finished units — give it (and anything else listening) a nudge.
            notifier.NotifyStageChanged(workItem.CurrentStageId);
        }

        return new AdvanceResult(outcome, workItem, nextStep);
    }

    public async Task ReleaseAsync(int workItemId, int? stationId, CancellationToken cancellationToken = default)
    {
        var workItem = await GetClaimedWorkItemAsync(workItemId, stationId, cancellationToken);

        // Back of the current stage's queue, not the front: the whole point of a release is
        // "someone/something else next" — re-queuing at the front would just hand the same unit
        // straight back to whichever station refreshes first.
        workItem.Status = WorkItemStatus.Queued;
        workItem.ClaimedByStationId = null;
        workItem.ClaimedAtUtc = null;
        workItem.QueuedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        notifier.NotifyStageChanged(workItem.CurrentStageId);
    }

    public async Task SendBackAsync(int workItemId, int stationId, int targetStageId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var workItem = await GetClaimedWorkItemAsync(workItemId, stationId, cancellationToken);
        await db.Entry(workItem).Reference(wi => wi.CurrentStage).LoadAsync(cancellationToken);

        var targetStage = await db.Stages
            .FirstOrDefaultAsync(s => s.Id == targetStageId && s.WorkflowId == workItem.WorkflowId, cancellationToken)
            ?? throw new RelayOperationException("The target stage doesn't belong to this workflow.");
        if (targetStage.OrderIndex >= workItem.CurrentStage.OrderIndex)
        {
            throw new RelayOperationException("A unit can only be sent back to an earlier stage.");
        }

        // The work from the target stage onward is being redone, so those StepCompletions are
        // deleted — otherwise AdvanceAsync would see the stages as already done. Earlier stages'
        // completions (and their timing) are untouched, and the redo writes fresh, later
        // timestamps, so the timing review's per-stage math stays monotonic and correct.
        var redoneStepIds = await db.Steps
            .Where(s => s.Stage.WorkflowId == workItem.WorkflowId && s.Stage.OrderIndex >= targetStage.OrderIndex)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
        var redoneCompletions = await db.StepCompletions
            .Where(sc => sc.WorkItemId == workItem.Id && redoneStepIds.Contains(sc.StepId))
            .ToListAsync(cancellationToken);
        db.StepCompletions.RemoveRange(redoneCompletions);

        workItem.CurrentStageId = targetStage.Id;
        workItem.Status = WorkItemStatus.Queued;
        workItem.ClaimedByStationId = null;
        workItem.ClaimedAtUtc = null;
        workItem.QueuedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        notifier.NotifyStageChanged(targetStage.Id);
    }

    public async Task ScrapAsync(int workItemId, int? stationId, CancellationToken cancellationToken = default)
    {
        var workItem = await GetClaimedWorkItemAsync(workItemId, stationId, cancellationToken);

        workItem.Status = WorkItemStatus.Scrapped;
        workItem.ClaimedByStationId = null;
        workItem.ClaimedAtUtc = null;
        await db.SaveChangesAsync(cancellationToken);

        notifier.NotifyStageChanged(workItem.CurrentStageId);
    }

    /// <summary>Loads an InProgress WorkItem, verifying the caller's station holds its claim (when given).</summary>
    private async Task<WorkItem> GetClaimedWorkItemAsync(int workItemId, int? stationId, CancellationToken cancellationToken)
    {
        var workItem = await db.WorkItems.FindAsync([workItemId], cancellationToken)
            ?? throw new RelayOperationException($"WorkItem {workItemId} does not exist.");
        if (workItem.Status != WorkItemStatus.InProgress)
        {
            throw new RelayOperationException(
                $"WorkItem {workItemId} is not in progress (status: {workItem.Status}).");
        }
        if (stationId is not null && workItem.ClaimedByStationId != stationId)
        {
            throw new RelayOperationException(
                $"WorkItem {workItemId} is not claimed by station {stationId}.");
        }
        return workItem;
    }

    private static DateTime? Max(DateTime? a, DateTime? b) =>
        a is null ? b : b is null ? a : (a > b ? a : b);

    public Task<int> GetQueueDepthAsync(int stageId, CancellationToken cancellationToken = default)
    {
        return db.WorkItems.CountAsync(
            wi => wi.CurrentStageId == stageId && wi.Status == WorkItemStatus.Queued,
            cancellationToken);
    }

    public Task<Station?> GetStationAsync(int stationId, CancellationToken cancellationToken = default)
    {
        return db.Stations
            .Include(s => s.Stage).ThenInclude(st => st.Workflow).ThenInclude(w => w.Stages)
            .Include(s => s.Stage).ThenInclude(st => st.Steps).ThenInclude(step => step.MediaAssets)
            .Include(s => s.Stage).ThenInclude(st => st.Steps).ThenInclude(step => step.Inputs)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Id == stationId, cancellationToken);
    }

    public Task<List<Station>> GetStationsAsync(CancellationToken cancellationToken = default)
    {
        return db.Stations
            .Include(s => s.Stage).ThenInclude(st => st.Workflow)
            .OrderBy(s => s.Stage.Workflow.Name)
            .ThenBy(s => s.Stage.OrderIndex)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<WorkItem?> GetActiveWorkItemAsync(int stationId, CancellationToken cancellationToken = default)
    {
        return db.WorkItems
            .Include(wi => wi.StepCompletions)
            .SingleOrDefaultAsync(
                wi => wi.ClaimedByStationId == stationId && wi.Status == WorkItemStatus.InProgress,
                cancellationToken);
    }

    public async Task<WorkItem> ClaimByScanAsync(int stationId, string scannedCode, CancellationToken cancellationToken = default)
    {
        scannedCode = scannedCode?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(scannedCode))
        {
            throw new RelayOperationException("Scan or enter an order number.");
        }

        var station = await db.Stations
            .Include(s => s.Stage)
            .FirstOrDefaultAsync(s => s.Id == stationId, cancellationToken)
            ?? throw new RelayOperationException($"Station {stationId} does not exist.");

        if (!station.Stage.RequiresScan)
        {
            throw new RelayOperationException("This stage doesn't require scan-to-claim.");
        }

        return await TryClaimQueuedAtStationByOrderNumberAsync(station, scannedCode, cancellationToken)
            ?? throw new RelayOperationException(
                $"No queued unit with order '{scannedCode}' is waiting at {station.Stage.Name}.");
    }

    public async Task<WorkItem> CreateFromPrebuildAsync(int stationId, string prebuildId, CancellationToken cancellationToken = default)
    {
        prebuildId = prebuildId?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(prebuildId))
        {
            throw new RelayOperationException("Enter a prebuild ID.");
        }

        var station = await db.Stations
            .Include(s => s.Stage)
            .FirstOrDefaultAsync(s => s.Id == stationId, cancellationToken)
            ?? throw new RelayOperationException($"Station {stationId} does not exist.");

        // A prebuild is scanned to *start* a unit, so this must be the workflow's first stage.
        var firstStageId = await db.Stages
            .Where(s => s.WorkflowId == station.Stage.WorkflowId)
            .OrderBy(s => s.OrderIndex)
            .Select(s => s.Id)
            .FirstAsync(cancellationToken);
        if (station.StageId != firstStageId)
        {
            throw new RelayOperationException("A prebuild can only be scanned at the workflow's first station.");
        }

        // An order already on this line takes priority over History: claim a unit of it that's
        // sitting Queued at THIS entry stage instead of creating a duplicate — that's how orders
        // authored on the Orders screen (or imported from History) get started on a prebuild
        // workflow. A QTY-N order is N such units sharing one number, so each scan claims the next
        // one (oldest first, FIFO); the operator scans the batch number once per unit.
        var queuedHere = await TryClaimQueuedAtStationByOrderNumberAsync(station, prebuildId, cancellationToken);
        if (queuedHere is not null)
        {
            return queuedHere;
        }

        // Nothing of this order is waiting at the entry stage. If a unit of it is nonetheless in
        // flight (claimed here, or downstream), there's nothing left to start — a duplicate scan.
        var inFlight = await db.WorkItems.AnyAsync(
            wi => wi.WorkflowId == station.Stage.WorkflowId
                && wi.OrderNumber == prebuildId
                && wi.Status != WorkItemStatus.Completed
                && wi.Status != WorkItemStatus.Cancelled
                && wi.Status != WorkItemStatus.Scrapped,
            cancellationToken);
        if (inFlight)
        {
            throw new RelayOperationException($"'{prebuildId}' is already being worked on this line.");
        }

        // Not on the line yet — the scanned ID must then be a History OrderId. History.Qty is the
        // number of physical units, so create that many WorkItems sharing the order
        // number: this first scan claims one immediately and leaves the rest queued for later scans.
        var history = await db.History.FirstOrDefaultAsync(h => h.OrderId == prebuildId, cancellationToken)
            ?? throw new RelayOperationException($"'{prebuildId}' is not in History and doesn't match a queued order.");

        var now = DateTime.UtcNow;
        var units = history.Qty < 1 ? 1 : history.Qty;
        var workItems = new List<WorkItem>(units);
        for (var i = 0; i < units; i++)
        {
            workItems.Add(new WorkItem
            {
                WorkflowId = station.Stage.WorkflowId,
                CurrentStageId = station.StageId,
                OrderNumber = history.OrderId,
                Sku = history.Sku,
                Quantity = 1,
                Channel = history.Channel,
                Status = i == 0 ? WorkItemStatus.InProgress : WorkItemStatus.Queued,
                ClaimedByStationId = i == 0 ? stationId : null,
                ClaimedAtUtc = i == 0 ? now : null,
                QueuedAtUtc = now,
            });
        }

        var workItem = workItems[0];
        db.WorkItems.AddRange(workItems);
        await db.SaveChangesAsync(cancellationToken);

        notifier.NotifyStageChanged(station.StageId);
        return workItem;
    }

    private async Task<WorkItem?> TryClaimQueuedAtStationByOrderNumberAsync(
        Station station,
        string orderNumber,
        CancellationToken cancellationToken)
    {
        var queued = await db.WorkItems
            .Where(wi => wi.WorkflowId == station.Stage.WorkflowId
                && wi.OrderNumber == orderNumber
                && wi.Status == WorkItemStatus.Queued
                && wi.CurrentStageId == station.StageId)
            .OrderBy(wi => wi.QueuedAtUtc).ThenBy(wi => wi.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (queued is null)
        {
            return null;
        }

        queued.ClaimedByStationId = station.Id;
        queued.ClaimedAtUtc = DateTime.UtcNow;
        queued.Status = WorkItemStatus.InProgress;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new RelayOperationException($"'{orderNumber}' was just claimed by another station.");
        }

        notifier.NotifyStageChanged(station.StageId);
        return queued;
    }

    public async Task<WorkItem> StartAdHocAsync(int stationId, CancellationToken cancellationToken = default)
    {
        var station = await db.Stations
            .Include(s => s.Stage).ThenInclude(st => st.Workflow)
            .FirstOrDefaultAsync(s => s.Id == stationId, cancellationToken)
            ?? throw new RelayOperationException($"Station {stationId} does not exist.");

        if (!station.Stage.Workflow.AllowAdHocStart)
        {
            throw new RelayOperationException("This workflow doesn't allow starting a run without an order.");
        }

        // A run starts a unit, so this must be the workflow's first stage.
        var firstStageId = await db.Stages
            .Where(s => s.WorkflowId == station.Stage.WorkflowId)
            .OrderBy(s => s.OrderIndex)
            .Select(s => s.Id)
            .FirstAsync(cancellationToken);
        if (station.StageId != firstStageId)
        {
            throw new RelayOperationException("A run can only be started at the workflow's first station.");
        }

        // Generated run number — timestamp-based so it's human-readable, sortable, and unique per
        // second. SKU "RUN"/qty 1 stand in for "training run"; there's no real order behind it.
        var now = DateTime.UtcNow;
        var workItem = new WorkItem
        {
            WorkflowId = station.Stage.WorkflowId,
            CurrentStageId = station.StageId,
            OrderNumber = $"RUN-{now:yyyyMMdd-HHmmss}",
            Sku = "RUN",
            Quantity = 1,
            Channel = null,
            Status = WorkItemStatus.InProgress,
            ClaimedByStationId = stationId,
            ClaimedAtUtc = now,
            QueuedAtUtc = now,
        };
        db.WorkItems.Add(workItem);
        await db.SaveChangesAsync(cancellationToken);

        notifier.NotifyStageChanged(station.StageId);
        return workItem;
    }
}
