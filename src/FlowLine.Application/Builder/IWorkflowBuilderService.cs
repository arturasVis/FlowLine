using FlowLine.Domain.Entities;

namespace FlowLine.Application.Builder;

/// <summary>
/// The workflow builder: admin CRUD over the template side of the domain model
/// (Workflow/Stage/Step/MediaAsset) — PRD §7.1 (FR-1–FR-5). This is what makes process
/// definitions data instead of code; it never touches the instance side (WorkItem,
/// StepCompletion, Station claims) — that's IRelayService's job.
/// </summary>
public interface IWorkflowBuilderService
{
    /// <summary>All workflows, newest first — for the workflow list screen. No stages/steps loaded.</summary>
    Task<List<Workflow>> GetWorkflowsAsync(CancellationToken cancellationToken = default);

    /// <summary>One workflow with its Stages, Steps, and MediaAssets all loaded and ordered, for the editor screen.</summary>
    Task<Workflow?> GetWorkflowAsync(int workflowId, CancellationToken cancellationToken = default);

    Task<Workflow> CreateWorkflowAsync(string name, string? description, CancellationToken cancellationToken = default);

    Task UpdateWorkflowAsync(int workflowId, string name, string? description, CancellationToken cancellationToken = default);

    /// <summary>"Archive" (FR-1) is IsActive = false, not a delete — history (WorkItems, timings) stays intact.</summary>
    Task SetWorkflowActiveAsync(int workflowId, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>Toggles whether units in this workflow must have a prebuild ID scanned at step 1.</summary>
    Task SetWorkflowRequiresPrebuildAsync(int workflowId, bool requiresPrebuild, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deep-clones a workflow's whole template (stages, steps, media — including copying the
    /// underlying media files, so deleting media on one copy never affects the other) under a
    /// new name, so a new process can start from an existing one (FR-5).
    /// </summary>
    Task<Workflow> DuplicateWorkflowAsync(int workflowId, string newName, CancellationToken cancellationToken = default);

    /// <summary>Appends a new Stage at the end of the workflow.</summary>
    Task<Stage> AddStageAsync(int workflowId, string name, CancellationToken cancellationToken = default);

    Task UpdateStageAsync(int stageId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Throws <see cref="WorkflowBuilderException"/> if the stage still has WorkItems on it
    /// or its steps have recorded StepCompletions (the FK Restrict guard tripped).
    /// </summary>
    Task DeleteStageAsync(int stageId, CancellationToken cancellationToken = default);

    /// <summary>Reassigns OrderIndex 0..N-1 to match the given order. Must include every stage in the workflow exactly once.</summary>
    Task ReorderStagesAsync(int workflowId, IReadOnlyList<int> orderedStageIds, CancellationToken cancellationToken = default);

    /// <summary>Appends a new Step at the end of the stage.</summary>
    Task<Step> AddStepAsync(int stageId, string name, string instructions, CancellationToken cancellationToken = default);

    Task UpdateStepAsync(int stepId, string name, string instructions, CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="WorkflowBuilderException"/> if the step has recorded StepCompletions.</summary>
    Task DeleteStepAsync(int stepId, CancellationToken cancellationToken = default);

    /// <summary>Reassigns OrderIndex 0..N-1 to match the given order. Must include every step in the stage exactly once.</summary>
    Task ReorderStepsAsync(int stageId, IReadOnlyList<int> orderedStepIds, CancellationToken cancellationToken = default);

    /// <summary>Saves the uploaded file under the configured media root and appends a MediaAsset row for it.</summary>
    Task<MediaAsset> AddMediaAssetAsync(int stepId, string fileName, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Deletes the MediaAsset row and best-effort deletes its backing file.</summary>
    Task DeleteMediaAssetAsync(int mediaAssetId, CancellationToken cancellationToken = default);
}
