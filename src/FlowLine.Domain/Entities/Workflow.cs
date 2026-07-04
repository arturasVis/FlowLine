namespace FlowLine.Domain.Entities;

public class Workflow
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When true, the operator must scan/enter a prebuild ID at the first step before the unit can
    /// advance; that ID (looked up in the company History table) then follows the WorkItem to every
    /// downstream station. See <see cref="WorkItem.PrebuildId"/>.
    /// </summary>
    public bool RequiresPrebuild { get; set; }

    /// <summary>
    /// When true, the workflow needs no premade order: the entry station shows a "Start run"
    /// button that creates (and claims) a WorkItem on the spot with a generated run number.
    /// For routines/training lines where the point is doing and timing the steps, not tracking
    /// an order. Mutually exclusive with <see cref="RequiresPrebuild"/> — both change how the
    /// entry stage gets work (see WorkflowBuilderService's setters).
    /// </summary>
    public bool AllowAdHocStart { get; set; }

    public List<Stage> Stages { get; set; } = [];
    public List<WorkItem> WorkItems { get; set; } = [];
    public List<WorkflowAssignment> Assignments { get; set; } = [];
}
