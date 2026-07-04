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
    /// <summary>All workflows, newest first, with stages/steps/stations loaded for readiness badges.</summary>
    Task<List<Workflow>> GetWorkflowsAsync(CancellationToken cancellationToken = default);

    /// <summary>One workflow with its Stages, Steps, MediaAssets, and Stations loaded for the editor screen.</summary>
    Task<Workflow?> GetWorkflowAsync(int workflowId, CancellationToken cancellationToken = default);

    Task<Workflow> CreateWorkflowAsync(string name, string? description, CancellationToken cancellationToken = default);

    Task UpdateWorkflowAsync(int workflowId, string name, string? description, CancellationToken cancellationToken = default);

    /// <summary>"Archive" (FR-1) is IsActive = false, not a delete — history (WorkItems, timings) stays intact.</summary>
    Task SetWorkflowActiveAsync(int workflowId, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>Toggles whether units in this workflow must have a prebuild ID scanned at step 1. Enabling turns AllowAdHocStart off.</summary>
    Task SetWorkflowRequiresPrebuildAsync(int workflowId, bool requiresPrebuild, CancellationToken cancellationToken = default);

    /// <summary>
    /// Toggles free-run mode: the entry station gets a "Start run" button that creates and claims a
    /// WorkItem on the spot (no premade order) — for routines/training where the point is doing and
    /// timing the steps. Enabling turns RequiresPrebuild off (both change how the entry stage gets work).
    /// </summary>
    Task SetWorkflowAllowAdHocStartAsync(int workflowId, bool allowAdHocStart, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deep-clones a workflow's whole template (stages, steps, media — including copying the
    /// underlying media files, so deleting media on one copy never affects the other) under a
    /// new name, so a new process can start from an existing one (FR-5).
    /// </summary>
    Task<Workflow> DuplicateWorkflowAsync(int workflowId, string newName, CancellationToken cancellationToken = default);

    /// <summary>Appends a new Stage at the end of the workflow.</summary>
    Task<Stage> AddStageAsync(int workflowId, string name, CancellationToken cancellationToken = default);

    Task UpdateStageAsync(int stageId, string name, CancellationToken cancellationToken = default);

    /// <summary>Toggles scan-to-claim for a stage. Scan-required stages do not auto-claim from their queue.</summary>
    Task SetStageRequiresScanAsync(int stageId, bool requiresScan, CancellationToken cancellationToken = default);

    /// <summary>Adds a routing branch (operator button) to a stage. targetStageId null = Finish; otherwise a stage in the same workflow.</summary>
    Task<StageBranch> AddStageBranchAsync(int stageId, string label, int? targetStageId, CancellationToken cancellationToken = default);

    Task UpdateStageBranchAsync(int branchId, string label, int? targetStageId, CancellationToken cancellationToken = default);

    Task DeleteStageBranchAsync(int branchId, CancellationToken cancellationToken = default);

    /// <summary>Reassigns OrderIndex 0..N-1 to match the given order. Must include every branch on the stage exactly once.</summary>
    Task ReorderStageBranchesAsync(int stageId, IReadOnlyList<int> orderedBranchIds, CancellationToken cancellationToken = default);

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

    /// <summary>Appends a new data-capture input to a step (manager-configured). Options is the checklist item list (ignored for other types).</summary>
    Task<StepInput> AddStepInputAsync(int stepId, string label, StepInputType type, bool required, string? options, CancellationToken cancellationToken = default);

    Task UpdateStepInputAsync(int inputId, string label, StepInputType type, bool required, string? options, CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="WorkflowBuilderException"/> if operators have already recorded answers for the input.</summary>
    Task DeleteStepInputAsync(int inputId, CancellationToken cancellationToken = default);

    /// <summary>Reassigns OrderIndex 0..N-1 to match the given order. Must include every input on the step exactly once.</summary>
    Task ReorderStepInputsAsync(int stepId, IReadOnlyList<int> orderedInputIds, CancellationToken cancellationToken = default);

    /// <summary>Saves the uploaded file under the configured media root and appends a MediaAsset row for it.</summary>
    Task<MediaAsset> AddMediaAssetAsync(int stepId, string fileName, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Deletes the MediaAsset row and best-effort deletes its backing file.</summary>
    Task DeleteMediaAssetAsync(int mediaAssetId, CancellationToken cancellationToken = default);
}
