using FlowLine.Domain.Entities;

namespace FlowLine.Application.Relay;

public enum AdvanceOutcome
{
    /// <summary>A sub-step was completed; the WorkItem is still in progress at the same stage.</summary>
    Advanced,

    /// <summary>The stage's last step was completed and the WorkItem moved to the next stage's queue.</summary>
    HandedOff,

    /// <summary>The final stage's last step was completed; the WorkItem is now Completed.</summary>
    Completed,

    /// <summary>The stage's last step was completed, but the stage is a fork — the WorkItem stays
    /// claimed at the stage until the operator picks a branch (see <see cref="IRelayService.RouteAsync"/>).</summary>
    AwaitingRoute,
}

public record AdvanceResult(AdvanceOutcome Outcome, WorkItem WorkItem, Step CompletedStep);

/// <summary>One operator answer supplied to <see cref="IRelayService.AdvanceAsync"/> — the id of the
/// step's <see cref="Domain.Entities.StepInput"/> and the recorded value.</summary>
public record StepInputValue(int StepInputId, string Value);
