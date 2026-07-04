namespace FlowLine.Domain.Entities;

/// <summary>
/// A piece of data the manager configures a <see cref="Step"/> to collect from the operator
/// (part of the workflow *template*). A step can have any number of these; the operator's answers
/// are recorded as <see cref="StepCompletionValue"/> rows when the step is completed.
/// </summary>
public class StepInput
{
    public int Id { get; set; }

    public int StepId { get; set; }
    public Step Step { get; set; } = null!;

    /// <summary>Prompt shown to the operator (e.g. "SSD #1 serial").</summary>
    public string Label { get; set; } = string.Empty;

    public StepInputType Type { get; set; }

    /// <summary>Whether the operator must fill this in before the step can advance.</summary>
    public bool Required { get; set; }

    public int OrderIndex { get; set; }

    /// <summary>
    /// For <see cref="StepInputType.Checklist"/>: the tick items, one per line. Null/ignored for
    /// the other types.
    /// </summary>
    public string? Options { get; set; }

    public List<StepCompletionValue> Values { get; set; } = [];
}
