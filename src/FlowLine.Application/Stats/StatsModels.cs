namespace FlowLine.Application.Stats;

/// <summary>One workflow's headline numbers for the stats picker page.</summary>
public record WorkflowStatsSummary(
    int WorkflowId,
    string WorkflowName,
    bool IsActive,
    int UnitsCompleted,
    TimeSpan? AvgHandsOnPerUnit,
    TimeSpan? AvgLeadTimePerUnit);

/// <summary>
/// Average time for one step across the completed units in range. AvgDuration comes only
/// from completions that have a StartedAtUtc (TimedCount of them); TotalCount includes
/// legacy rows recorded before start times were tracked.
/// </summary>
public record StepAverage(
    int StepId,
    string StepName,
    string StageName,
    int StageOrderIndex,
    int StepOrderIndex,
    TimeSpan? AvgDuration,
    int TimedCount,
    int TotalCount);

/// <summary>What was built: per-SKU unit counts and averages.</summary>
public record SkuStats(
    string Sku,
    int UnitsCompleted,
    TimeSpan? AvgHandsOnPerUnit,
    TimeSpan? AvgLeadTimePerUnit);

/// <summary>Units completed on one (server-local) calendar day, for the trend chart.</summary>
public record DailyCount(DateOnly Date, int Count);

/// <summary>One operator's row in the workflow dashboard's staff section.</summary>
public record StaffSummary(
    int StaffNumber,
    string? Name,
    int StepsCompleted,
    int UnitsTouched,
    TimeSpan? AvgStepDuration);

/// <summary>Everything the workflow stats dashboard shows for one date range.</summary>
public record WorkflowStats(
    int WorkflowId,
    string WorkflowName,
    int UnitsCompleted,
    TimeSpan? AvgHandsOnPerUnit,
    TimeSpan? AvgLeadTimePerUnit,
    IReadOnlyList<StepAverage> StepAverages,
    IReadOnlyList<SkuStats> Skus,
    IReadOnlyList<DailyCount> UnitsPerDay,
    IReadOnlyList<StaffSummary> Staff);

/// <summary>
/// One step, one operator, against the workflow-wide average. DeltaPercent is
/// (staff − average) / average: negative means faster than average.
/// </summary>
public record StaffStepComparison(
    int StepId,
    string StepName,
    string StageName,
    int StageOrderIndex,
    int StepOrderIndex,
    TimeSpan? StaffAvg,
    TimeSpan? WorkflowAvg,
    double? DeltaPercent,
    int TimesDone);

/// <summary>One operator's SKU breakdown: units they worked on and steps they did on them.</summary>
public record StaffSkuStats(string Sku, int UnitsTouched, int StepsCompleted);

/// <summary>Everything the per-operator drill-down shows for one date range.</summary>
public record StaffStats(
    int StaffNumber,
    string? Name,
    string WorkflowName,
    int StepsCompleted,
    int UnitsTouched,
    TimeSpan? AvgStepDuration,
    TimeSpan? WorkflowAvgStepDuration,
    double? OverallDeltaPercent,
    IReadOnlyList<StaffStepComparison> StepComparisons,
    IReadOnlyList<StaffSkuStats> Skus);
