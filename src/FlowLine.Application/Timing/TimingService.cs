using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Application.Timing;

public class TimingService(FlowLineDbContext db) : ITimingService
{
    public async Task<List<CompletedOrderTiming>> GetCompletedOrderTimingsAsync(CancellationToken cancellationToken = default)
    {
        var workItems = await db.WorkItems
            .Where(wi => wi.Status == WorkItemStatus.Completed)
            .Include(wi => wi.Workflow).ThenInclude(w => w.Stages).ThenInclude(s => s.Steps)
            .Include(wi => wi.StepCompletions)
            .AsSplitQuery()
            .OrderByDescending(wi => wi.Id)
            .ToListAsync(cancellationToken);

        return workItems.Select(BuildTiming).ToList();
    }

    private static CompletedOrderTiming BuildTiming(WorkItem workItem)
    {
        var stepToStage = workItem.Workflow.Stages
            .SelectMany(stage => stage.Steps.Select(step => (step.Id, Stage: stage)))
            .ToDictionary(x => x.Id, x => x.Stage);

        // The last StepCompletion timestamp within a stage is exactly when AdvanceAsync
        // performed the hand-off out of it (PRD §6.5) — i.e. when the WorkItem left that
        // stage. CreatedAtUtc stands in for "when it left stage 0" so stage 1's dwell time
        // (including any wait before the first claim) is captured the same way as every
        // other stage's, instead of being a special case.
        var lastCompletionByStage = workItem.StepCompletions
            .GroupBy(sc => stepToStage[sc.StepId])
            .ToDictionary(g => g.Key, g => g.Max(sc => sc.CompletedAtUtc));

        var stageDurations = new List<StageDuration>();
        var previousStageEnd = workItem.CreatedAtUtc;

        foreach (var stage in workItem.Workflow.Stages.OrderBy(s => s.OrderIndex))
        {
            if (!lastCompletionByStage.TryGetValue(stage, out var stageEnd))
            {
                continue;
            }

            stageDurations.Add(new StageDuration(stage.Name, stage.OrderIndex, stageEnd - previousStageEnd));
            previousStageEnd = stageEnd;
        }

        var completedAtUtc = previousStageEnd;

        return new CompletedOrderTiming(
            workItem.Id,
            workItem.OrderNumber,
            workItem.Sku,
            workItem.Workflow.Name,
            workItem.CreatedAtUtc,
            completedAtUtc,
            completedAtUtc - workItem.CreatedAtUtc,
            stageDurations);
    }
}
