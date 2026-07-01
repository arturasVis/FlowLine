namespace FlowLine.Domain.Entities;

public class MediaAsset
{
    public int Id { get; set; }

    public int StepId { get; set; }
    public Step Step { get; set; } = null!;

    /// <summary>Path to the file under the server's local media folder, relative to its root.</summary>
    public string FilePath { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}
