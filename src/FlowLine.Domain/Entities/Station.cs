namespace FlowLine.Domain.Entities;

public class Station
{
    public int Id { get; set; }

    public int StageId { get; set; }
    public Stage Stage { get; set; } = null!;

    /// <summary>e.g. "Build Line A — Stage 2", how the physical screen/position is identified.</summary>
    public string Name { get; set; } = string.Empty;

    public List<WorkItem> ClaimedWorkItems { get; set; } = [];
    public List<StepCompletion> Completions { get; set; } = [];
}
