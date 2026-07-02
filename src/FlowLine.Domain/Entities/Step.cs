namespace FlowLine.Domain.Entities;

public class Step
{
    public int Id { get; set; }

    public int StageId { get; set; }
    public Stage Stage { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public int OrderIndex { get; set; }

    /// <summary>
    /// When true, completing this step requires scanning the unit's order-number barcode —
    /// the relay rejects the advance unless the scanned code matches the WorkItem's
    /// OrderNumber (physical right-unit verification, e.g. before a destructive step).
    /// </summary>
    public bool RequiresScan { get; set; }

    public List<MediaAsset> MediaAssets { get; set; } = [];
    public List<StepCompletion> Completions { get; set; } = [];
}
