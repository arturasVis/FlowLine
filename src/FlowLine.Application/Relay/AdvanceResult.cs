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
}

public record AdvanceResult(AdvanceOutcome Outcome, WorkItem WorkItem, Step CompletedStep);
