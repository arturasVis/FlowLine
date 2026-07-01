using FlowLine.Domain.Entities;
using FlowLine.Domain.Entities.External;
using FlowLine.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Application.Relay;

public class RelayService(FlowLineDbContext db, IRelayNotifier notifier) : IRelayService
{
    public async Task<WorkItem?> ClaimNextAsync(int stationId, CancellationToken cancellationToken = default)
    {
        var station = await db.Stations.FindAsync([stationId], cancellationToken)
            ?? throw new RelayOperationException($"Station {stationId} does not exist.");

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

    public async Task<AdvanceResult> AdvanceAsync(int workItemId, int? stationId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var workItem = await db.WorkItems
            .Include(wi => wi.StepCompletions)
            .Include(wi => wi.CurrentStage).ThenInclude(s => s.Steps)
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

        db.StepCompletions.Add(new StepCompletion
        {
            WorkItemId = workItem.Id,
            StepId = nextStep.Id,
            StationId = stationId,
            CompletedAtUtc = DateTime.UtcNow,
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
                outcome = AdvanceOutcome.Completed;
            }
            else
            {
                workItem.CurrentStageId = nextStage.Id;
                workItem.Status = WorkItemStatus.Queued;
                workItem.ClaimedByStationId = null;
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

        return new AdvanceResult(outcome, workItem, nextStep);
    }

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

        // The scanned ID is a History OrderId; the new WorkItem inherits that row's SKU/qty/channel.
        var history = await db.History.FirstOrDefaultAsync(h => h.OrderId == prebuildId, cancellationToken)
            ?? throw new RelayOperationException($"No prebuild found in History for '{prebuildId}'.");

        // Guard against scanning the same prebuild into the line twice while it's still in flight.
        var alreadyInFlight = await db.WorkItems.AnyAsync(
            wi => wi.WorkflowId == station.Stage.WorkflowId
                && wi.OrderNumber == prebuildId
                && wi.Status != WorkItemStatus.Completed,
            cancellationToken);
        if (alreadyInFlight)
        {
            throw new RelayOperationException($"Prebuild '{prebuildId}' is already on this line.");
        }

        // Created already claimed and InProgress at the scanning station, so the operator can begin
        // immediately; it then hands off downstream like any other unit.
        var now = DateTime.UtcNow;
        var workItem = new WorkItem
        {
            WorkflowId = station.Stage.WorkflowId,
            CurrentStageId = station.StageId,
            OrderNumber = history.OrderId,
            Sku = history.Sku,
            Quantity = history.Qty,
            Channel = history.Channel,
            Status = WorkItemStatus.InProgress,
            ClaimedByStationId = stationId,
            QueuedAtUtc = now,
        };
        db.WorkItems.Add(workItem);
        await db.SaveChangesAsync(cancellationToken);

        notifier.NotifyStageChanged(station.StageId);
        return workItem;
    }
}
