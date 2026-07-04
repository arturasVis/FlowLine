namespace FlowLine.Domain.Entities;

/// <summary>
/// A manager-configured routing choice offered at the end of a <see cref="Stage"/> (part of the
/// workflow *template*). When a stage has one or more branches it's a "fork": rather than
/// auto-advancing to the next stage, the operator picks a branch and the unit routes to its target.
/// A stage with no branches stays linear (hands off to the next stage by OrderIndex).
/// </summary>
public class StageBranch
{
    public int Id { get; set; }

    /// <summary>The fork stage this branch is offered at.</summary>
    public int StageId { get; set; }
    public Stage Stage { get; set; } = null!;

    /// <summary>The button label shown to the operator (e.g. "Pass", "Send to rework").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// The stage the unit moves to when this branch is chosen. Null means <b>Finish</b> — the unit
    /// is marked Completed. May target an earlier stage (a rework loop), which resets that stage.
    /// </summary>
    public int? TargetStageId { get; set; }
    public Stage? TargetStage { get; set; }

    public int OrderIndex { get; set; }
}
