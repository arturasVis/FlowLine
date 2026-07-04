using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Application.Stats;

public class StatsService(FlowLineDbContext db) : IStatsService
{
    public async Task<List<WorkflowStatsSummary>> GetWorkflowSummariesAsync(
        DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken = default)
    {
        var workflows = await db.Workflows
            .OrderByDescending(w => w.IsActive)
            .ThenBy(w => w.Name)
            .Select(w => new { w.Id, w.Name, w.IsActive })
            .ToListAsync(cancellationToken);

        var units = await LoadCompletedUnitsAsync(null, fromUtc, toUtc, includeSteps: false, cancellationToken);
        var byWorkflow = units.GroupBy(u => u.WorkflowId).ToDictionary(g => g.Key, g => g.ToList());

        return workflows.Select(w =>
        {
            var workflowUnits = byWorkflow.GetValueOrDefault(w.Id, []);
            return new WorkflowStatsSummary(
                w.Id, w.Name, w.IsActive,
                workflowUnits.Count,
                Average(workflowUnits.Select(HandsOnTime).OfType<TimeSpan>()),
                Average(workflowUnits.Select(LeadTime)));
        }).ToList();
    }

    public async Task<WorkflowStats?> GetWorkflowStatsAsync(
        int workflowId, DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken = default)
    {
        var workflow = await db.Workflows
            .AsNoTracking()
            .SingleOrDefaultAsync(w => w.Id == workflowId, cancellationToken);
        if (workflow is null)
        {
            return null;
        }

        var units = await LoadCompletedUnitsAsync(workflowId, fromUtc, toUtc, includeSteps: true, cancellationToken);
        var staffNames = await GetStaffNamesAsync(cancellationToken);

        var staff = units
            .SelectMany(u => u.StepCompletions)
            .Where(sc => sc.CompletedByStaffNumber is not null)
            .GroupBy(sc => sc.CompletedByStaffNumber!.Value)
            .Select(g => new StaffSummary(
                g.Key,
                staffNames.GetValueOrDefault(g.Key),
                g.Count(),
                g.Select(sc => sc.WorkItemId).Distinct().Count(),
                Average(TimedDurations(g))))
            .OrderByDescending(s => s.StepsCompleted)
            .ThenBy(s => s.StaffNumber)
            .ToList();

        var skus = units
            .GroupBy(u => u.Sku)
            .Select(g => new SkuStats(
                g.Key,
                g.Count(),
                Average(g.Select(HandsOnTime).OfType<TimeSpan>()),
                Average(g.Select(LeadTime))))
            .OrderByDescending(s => s.UnitsCompleted)
            .ThenBy(s => s.Sku)
            .ToList();

        return new WorkflowStats(
            workflow.Id, workflow.Name,
            units.Count,
            Average(units.Select(HandsOnTime).OfType<TimeSpan>()),
            Average(units.Select(LeadTime)),
            ComputeStepAverages(units),
            skus,
            ComputeUnitsPerDay(units),
            staff);
    }

    public async Task<StaffStats?> GetStaffStatsAsync(
        int workflowId, int staffNumber, DateTime? fromUtc, DateTime? toUtc,
        CancellationToken cancellationToken = default)
    {
        var workflow = await db.Workflows
            .AsNoTracking()
            .SingleOrDefaultAsync(w => w.Id == workflowId, cancellationToken);
        if (workflow is null)
        {
            return null;
        }

        var units = await LoadCompletedUnitsAsync(workflowId, fromUtc, toUtc, includeSteps: true, cancellationToken);
        var staffNames = await GetStaffNamesAsync(cancellationToken);

        var workflowStepAverages = ComputeStepAverages(units).ToDictionary(sa => sa.StepId);
        var own = units
            .SelectMany(u => u.StepCompletions)
            .Where(sc => sc.CompletedByStaffNumber == staffNumber)
            .ToList();

        var comparisons = own
            .GroupBy(sc => sc.StepId)
            .Select(g =>
            {
                var step = g.First().Step;
                var staffAvg = Average(TimedDurations(g));
                var workflowAvg = workflowStepAverages.GetValueOrDefault(g.Key)?.AvgDuration;
                return new StaffStepComparison(
                    g.Key, step.Name, step.Stage.Name,
                    step.Stage.OrderIndex, step.OrderIndex,
                    staffAvg, workflowAvg,
                    DeltaPercent(staffAvg, workflowAvg),
                    g.Count());
            })
            .OrderBy(c => c.StageOrderIndex)
            .ThenBy(c => c.StepOrderIndex)
            .ToList();

        // "Up or down" overall, adjusted for which steps this person actually does: their
        // total timed working time against what the workflow-wide averages predict for the
        // exact same mix of steps. A raw avg-vs-avg would penalise whoever mans the slow steps.
        TimeSpan? actualTotal = null, expectedTotal = null;
        foreach (var comparison in comparisons)
        {
            var timedCount = own.Count(sc => sc.StepId == comparison.StepId && sc.StartedAtUtc is not null);
            if (timedCount == 0 || comparison.StaffAvg is null || comparison.WorkflowAvg is null)
            {
                continue;
            }
            actualTotal = (actualTotal ?? TimeSpan.Zero) + comparison.StaffAvg.Value * timedCount;
            expectedTotal = (expectedTotal ?? TimeSpan.Zero) + comparison.WorkflowAvg.Value * timedCount;
        }

        var unitsBySkuTouched = own
            .GroupBy(sc => sc.WorkItemId)
            .Select(g => new { Unit = units.First(u => u.Id == g.Key), Steps = g.Count() })
            .GroupBy(x => x.Unit.Sku)
            .Select(g => new StaffSkuStats(g.Key, g.Count(), g.Sum(x => x.Steps)))
            .OrderByDescending(s => s.UnitsTouched)
            .ThenBy(s => s.Sku)
            .ToList();

        var allTimed = units.SelectMany(u => u.StepCompletions).ToList();
        return new StaffStats(
            staffNumber,
            staffNames.GetValueOrDefault(staffNumber),
            workflow.Name,
            own.Count,
            own.Select(sc => sc.WorkItemId).Distinct().Count(),
            Average(TimedDurations(own)),
            Average(TimedDurations(allTimed)),
            DeltaPercent(actualTotal, expectedTotal),
            comparisons,
            unitsBySkuTouched);
    }

    /// <summary>
    /// The population every stat is computed over: Completed WorkItems whose completion
    /// moment (latest StepCompletion) falls inside the range. Aggregation happens in memory —
    /// fine at prototype volume; revisit with server-side grouping if completed history grows large.
    /// </summary>
    private async Task<List<WorkItem>> LoadCompletedUnitsAsync(
        int? workflowId, DateTime? fromUtc, DateTime? toUtc, bool includeSteps, CancellationToken cancellationToken)
    {
        var query = db.WorkItems
            .AsNoTracking()
            .Where(wi => wi.Status == WorkItemStatus.Completed && wi.StepCompletions.Count != 0);

        if (workflowId is int id)
        {
            query = query.Where(wi => wi.WorkflowId == id);
        }
        if (fromUtc is DateTime from)
        {
            query = query.Where(wi => wi.StepCompletions.Max(sc => sc.CompletedAtUtc) >= from);
        }
        if (toUtc is DateTime to)
        {
            query = query.Where(wi => wi.StepCompletions.Max(sc => sc.CompletedAtUtc) < to);
        }

        query = includeSteps
            ? query.Include(wi => wi.StepCompletions).ThenInclude(sc => sc.Step).ThenInclude(s => s.Stage)
            : query.Include(wi => wi.StepCompletions);

        return await query.AsSplitQuery().ToListAsync(cancellationToken);
    }

    private async Task<Dictionary<int, string>> GetStaffNamesAsync(CancellationToken cancellationToken) =>
        // Small lookup list — same in-memory map pattern as OrderImportService. The real StaffTable
        // has no enforced unique key and can contain duplicate staff numbers, so group before mapping
        // (last row wins) rather than ToDictionary, which would throw on a duplicate key.
        (await db.Staff.ToListAsync(cancellationToken))
            .GroupBy(s => s.StaffNumber)
            .ToDictionary(g => g.Key, g => g.Last().Name);

    private static List<StepAverage> ComputeStepAverages(List<WorkItem> units) =>
        units
            .SelectMany(u => u.StepCompletions)
            .GroupBy(sc => sc.StepId)
            .Select(g =>
            {
                var step = g.First().Step;
                var timed = TimedDurations(g).ToList();
                return new StepAverage(
                    g.Key, step.Name, step.Stage.Name,
                    step.Stage.OrderIndex, step.OrderIndex,
                    Average(timed), timed.Count, g.Count());
            })
            .OrderBy(sa => sa.StageOrderIndex)
            .ThenBy(sa => sa.StepOrderIndex)
            .ToList();

    private static List<DailyCount> ComputeUnitsPerDay(List<WorkItem> units)
    {
        if (units.Count == 0)
        {
            return [];
        }

        // Server-local calendar days — on a LAN deployment the server's clock is the line's clock.
        var byDay = units
            .GroupBy(u => DateOnly.FromDateTime(CompletedAt(u).ToLocalTime()))
            .ToDictionary(g => g.Key, g => g.Count());

        // Fill the gaps so the trend chart shows zero-output days instead of skipping them.
        var days = new List<DailyCount>();
        for (var day = byDay.Keys.Min(); day <= byDay.Keys.Max(); day = day.AddDays(1))
        {
            days.Add(new DailyCount(day, byDay.GetValueOrDefault(day)));
        }
        return days;
    }

    private static DateTime CompletedAt(WorkItem unit) =>
        unit.StepCompletions.Max(sc => sc.CompletedAtUtc);

    private static TimeSpan LeadTime(WorkItem unit) =>
        CompletedAt(unit) - unit.CreatedAtUtc;

    /// <summary>Total working time, or null if none of the unit's completions carry a start time.</summary>
    private static TimeSpan? HandsOnTime(WorkItem unit)
    {
        var timed = TimedDurations(unit.StepCompletions).ToList();
        return timed.Count == 0 ? null : TimeSpan.FromTicks(timed.Sum(t => t.Ticks));
    }

    private static IEnumerable<TimeSpan> TimedDurations(IEnumerable<StepCompletion> completions) =>
        completions
            .Where(sc => sc.StartedAtUtc is not null)
            .Select(sc => sc.CompletedAtUtc - sc.StartedAtUtc!.Value);

    private static TimeSpan? Average(IEnumerable<TimeSpan> spans)
    {
        var list = spans as ICollection<TimeSpan> ?? spans.ToList();
        return list.Count == 0 ? null : TimeSpan.FromTicks((long)list.Average(t => t.Ticks));
    }

    private static double? DeltaPercent(TimeSpan? actual, TimeSpan? baseline) =>
        actual is null || baseline is null || baseline.Value <= TimeSpan.Zero
            ? null
            : (actual.Value - baseline.Value) / baseline.Value * 100.0;
}
