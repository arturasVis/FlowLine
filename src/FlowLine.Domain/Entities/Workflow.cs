namespace FlowLine.Domain.Entities;

public class Workflow
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public List<Stage> Stages { get; set; } = [];
    public List<WorkItem> WorkItems { get; set; } = [];
}
