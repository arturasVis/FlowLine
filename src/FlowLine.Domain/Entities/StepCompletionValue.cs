namespace FlowLine.Domain.Entities;

/// <summary>
/// The operator's recorded answer to one <see cref="StepInput"/>, captured when a step was
/// completed (the *instance* side of step data capture). One row per input the step defines.
/// </summary>
public class StepCompletionValue
{
    public int Id { get; set; }

    public int StepCompletionId { get; set; }
    public StepCompletion StepCompletion { get; set; } = null!;

    public int StepInputId { get; set; }
    public StepInput StepInput { get; set; } = null!;

    /// <summary>
    /// The entered value, serialised to string: text as-is, the number as text, "Pass"/"Fail",
    /// or the checked items joined with a newline for a checklist.
    /// </summary>
    public string Value { get; set; } = string.Empty;
}
