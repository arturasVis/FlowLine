using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Application.Line;

public class LineStatusService(FlowLineDbContext db) : ILineStatusService
{
    public async Task<List<LineWorkflowStatus>> GetLineStatusAsync(CancellationToken cancellationToken = default)
    {
        // Stage-less workflows can't carry units, so they'd only be empty cards here.
        var workflows = await db.Workflows
            .AsNoTracking()
            .Where(w => w.IsActive && w.Stages.Count != 0)
            .Include(w => w.Stages)
            .OrderBy(w => w.Name)
            .ToListAsync(cancellationToken);

        var stationsByStage = (await db.Stations.AsNoTracking().ToListAsync(cancellationToken))
            .ToLookup(s => s.StageId);

        // Everything currently on the line, in one query — grouped in memory per stage below.
        var onLine = await db.WorkItems
            .AsNoTracking()
            .Where(wi => wi.Status == WorkItemStatus.Queued || wi.Status == WorkItemStatus.InProgress)
            .Include(wi => wi.ClaimedByStation)
            .ToListAsync(cancellationToken);
        var byStage = onLine.ToLookup(wi => wi.CurrentStageId);

        // "Today" is the server's calendar day — on a LAN deployment that's the line's day too.
        var todayStartUtc = DateTime.Today.ToUniversalTime();
        var completedToday = (await db.WorkItems
                .AsNoTracking()
                .Where(wi => wi.Status == WorkItemStatus.Completed
                    && wi.StepCompletions.Max(sc => sc.CompletedAtUtc) >= todayStartUtc)
                .GroupBy(wi => wi.WorkflowId)
                .Select(g => new { WorkflowId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.WorkflowId, x => x.Count);

        return workflows.Select(workflow =>
        {
            var stages = workflow.Stages
                .OrderBy(s => s.OrderIndex)
                .Select(stage => new LineStageStatus(
                    stage.Id,
                    stage.Name,
                    stationsByStage[stage.Id].Count(),
                    byStage[stage.Id].Count(wi => wi.Status == WorkItemStatus.Queued),
                    byStage[stage.Id]
                        .Where(wi => wi.Status == WorkItemStatus.InProgress)
                        .OrderBy(wi => wi.ClaimedAtUtc)
                        .Select(wi => new LineActiveUnit(
                            wi.Id, wi.OrderNumber, wi.Sku,
                            wi.ClaimedByStation?.Name ?? "—"))
                        .ToList()))
                .ToList();

            return new LineWorkflowStatus(
                workflow.Id,
                workflow.Name,
                completedToday.GetValueOrDefault(workflow.Id),
                stages.Sum(s => s.QueueDepth + s.Active.Count),
                stages);
        }).ToList();
    }
}
