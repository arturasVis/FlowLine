namespace FlowLine.Domain.Entities;

public class Step
{
    public int Id { get; set; }

    public int StageId { get; set; }
    public Stage Stage { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public int OrderIndex { get; set; }

    public List<MediaAsset> MediaAssets { get; set; } = [];
    public List<StepCompletion> Completions { get; set; } = [];

    /// <summary>Data the operator must record to complete this step (manager-configured).</summary>
    public List<StepInput> Inputs { get; set; } = [];
}
