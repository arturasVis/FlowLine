namespace FlowLine.Domain.Entities;

public class Stage
{
    public int Id { get; set; }

    public int WorkflowId { get; set; }
    public Workflow Workflow { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public int OrderIndex { get; set; }

    /// <summary>
    /// When true, stations at this stage do not auto-claim the next queued unit. The operator must
    /// scan a unit's order number, and the relay claims the matching queued WorkItem at this stage.
    /// </summary>
    public bool RequiresScan { get; set; }

    public List<Step> Steps { get; set; } = [];
    public List<Station> Stations { get; set; } = [];

    /// <summary>Routing choices offered at the end of this stage. Empty = linear (next stage by OrderIndex).</summary>
    public List<StageBranch> Branches { get; set; } = [];

    /// <summary>WorkItems currently queued or in progress at this stage (CurrentStageId = this stage).</summary>
    public List<WorkItem> CurrentWorkItems { get; set; } = [];
}
