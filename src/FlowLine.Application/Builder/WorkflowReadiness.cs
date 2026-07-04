using FlowLine.Domain.Entities;

namespace FlowLine.Application.Builder;

/// <summary>
/// Whether a workflow can actually run a unit end to end, and if not, why. A workflow is
/// runnable only if it's active, has at least one stage, and every stage has at least one
/// step (something to do) and at least one station (someone to do it) — otherwise a unit
/// reaching an unmanned or empty stage would silently stall there with no error.
///
/// Requires the workflow loaded with Stages → Steps and Stages → Stations (see
/// WorkflowBuilderService.GetWorkflows/GetWorkflow).
/// </summary>
public sealed record WorkflowReadiness(bool IsRunnable, IReadOnlyList<string> Problems)
{
    public static WorkflowReadiness For(Workflow workflow)
    {
        var problems = new List<string>();

        if (!workflow.IsActive)
        {
            problems.Add("Workflow is archived.");
        }
        if (workflow.Stages.Count == 0)
        {
            problems.Add("No stages yet — add at least one.");
        }

        foreach (var stage in workflow.Stages.OrderBy(s => s.OrderIndex))
        {
            if (stage.Steps.Count == 0)
            {
                problems.Add($"Stage \"{stage.Name}\" has no steps.");
            }
            if (stage.Stations.Count == 0)
            {
                problems.Add($"Stage \"{stage.Name}\" has no station — units will stall here.");
            }
        }

        return new WorkflowReadiness(problems.Count == 0, problems);
    }

    /// <summary>True if this specific stage has no station manning it (units would stall).</summary>
    public static bool StageHasNoStation(Stage stage) => stage.Stations.Count == 0;
}
