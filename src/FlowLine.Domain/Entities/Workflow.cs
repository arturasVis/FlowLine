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

    public List<Stage> Stages { get; set; } = [];
    public List<WorkItem> WorkItems { get; set; } = [];
    public List<WorkflowAssignment> Assignments { get; set; } = [];
}
