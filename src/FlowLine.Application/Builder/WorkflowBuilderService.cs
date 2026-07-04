using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Application.Builder;

public class WorkflowBuilderService(FlowLineDbContext db, MediaStorageOptions mediaOptions) : IWorkflowBuilderService
{
    public Task<List<Workflow>> GetWorkflowsAsync(CancellationToken cancellationToken = default)
    {
        // Steps + Stations are loaded so the list can show a runnable/not-runnable badge
        // (see WorkflowReadiness) without a second round of queries.
        return db.Workflows
            .Include(w => w.Stages).ThenInclude(s => s.Steps)
            .Include(w => w.Stages).ThenInclude(s => s.Stations)
            .AsSplitQuery()
            .OrderByDescending(w => w.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<Workflow?> GetWorkflowAsync(int workflowId, CancellationToken cancellationToken = default)
    {
        return db.Workflows
            .Include(w => w.Stages).ThenInclude(s => s.Steps).ThenInclude(st => st.MediaAssets)
            .Include(w => w.Stages).ThenInclude(s => s.Steps).ThenInclude(st => st.Inputs)
            .Include(w => w.Stages).ThenInclude(s => s.Stations)
            .Include(w => w.Stages).ThenInclude(s => s.Branches)
            .AsSplitQuery()
            .SingleOrDefaultAsync(w => w.Id == workflowId, cancellationToken);
    }

    public async Task<Workflow> CreateWorkflowAsync(string name, string? description, CancellationToken cancellationToken = default)
    {
        var workflow = new Workflow { Name = name, Description = description, IsActive = true };
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync(cancellationToken);
        return workflow;
    }

    public async Task UpdateWorkflowAsync(int workflowId, string name, string? description, CancellationToken cancellationToken = default)
    {
        var workflow = await db.Workflows.FindAsync([workflowId], cancellationToken)
            ?? throw new WorkflowBuilderException($"Workflow {workflowId} does not exist.");
        workflow.Name = name;
        workflow.Description = description;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetWorkflowActiveAsync(int workflowId, bool isActive, CancellationToken cancellationToken = default)
    {
        var workflow = await db.Workflows.FindAsync([workflowId], cancellationToken)
            ?? throw new WorkflowBuilderException($"Workflow {workflowId} does not exist.");
        workflow.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetWorkflowRequiresPrebuildAsync(int workflowId, bool requiresPrebuild, CancellationToken cancellationToken = default)
    {
        var workflow = await db.Workflows.FindAsync([workflowId], cancellationToken)
            ?? throw new WorkflowBuilderException($"Workflow {workflowId} does not exist.");
        workflow.RequiresPrebuild = requiresPrebuild;
        // Both flags change how the entry stage gets work — they can't be on at once.
        if (requiresPrebuild)
        {
            workflow.AllowAdHocStart = false;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetWorkflowAllowAdHocStartAsync(int workflowId, bool allowAdHocStart, CancellationToken cancellationToken = default)
    {
        var workflow = await db.Workflows.FindAsync([workflowId], cancellationToken)
            ?? throw new WorkflowBuilderException($"Workflow {workflowId} does not exist.");
        workflow.AllowAdHocStart = allowAdHocStart;
        // Both flags change how the entry stage gets work — they can't be on at once.
        if (allowAdHocStart)
        {
            workflow.RequiresPrebuild = false;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Workflow> DuplicateWorkflowAsync(int workflowId, string newName, CancellationToken cancellationToken = default)
    {
        var source = await db.Workflows
            .Include(w => w.Stages).ThenInclude(s => s.Steps).ThenInclude(st => st.MediaAssets)
            .Include(w => w.Stages).ThenInclude(s => s.Steps).ThenInclude(st => st.Inputs)
            .Include(w => w.Stages).ThenInclude(s => s.Branches)
            .AsSplitQuery()
            .SingleOrDefaultAsync(w => w.Id == workflowId, cancellationToken)
            ?? throw new WorkflowBuilderException($"Workflow {workflowId} does not exist.");

        var clone = new Workflow
        {
            Name = newName,
            Description = source.Description,
            IsActive = true,
            RequiresPrebuild = source.RequiresPrebuild,
            AllowAdHocStart = source.AllowAdHocStart,
        };

        // Map each source stage to its clone so branch targets can be remapped after all stages exist.
        var stageCloneBySourceId = new Dictionary<int, Stage>();

        foreach (var stage in source.Stages.OrderBy(s => s.OrderIndex))
        {
            var stageClone = new Stage
            {
                Workflow = clone,
                Name = stage.Name,
                OrderIndex = stage.OrderIndex,
                RequiresScan = stage.RequiresScan,
            };
            clone.Stages.Add(stageClone);
            stageCloneBySourceId[stage.Id] = stageClone;

            foreach (var step in stage.Steps.OrderBy(s => s.OrderIndex))
            {
                var stepClone = new Step
                {
                    Stage = stageClone,
                    Name = step.Name,
                    Instructions = step.Instructions,
                    OrderIndex = step.OrderIndex,
                };
                stageClone.Steps.Add(stepClone);

                foreach (var input in step.Inputs.OrderBy(i => i.OrderIndex))
                {
                    stepClone.Inputs.Add(new StepInput
                    {
                        Step = stepClone,
                        Label = input.Label,
                        Type = input.Type,
                        Required = input.Required,
                        OrderIndex = input.OrderIndex,
                        Options = input.Options,
                    });
                }

                foreach (var media in step.MediaAssets.OrderBy(m => m.DisplayOrder))
                {
                    var clonedPath = await CopyMediaFileAsync(media.FilePath, cancellationToken);
                    stepClone.MediaAssets.Add(new MediaAsset
                    {
                        Step = stepClone,
                        FilePath = clonedPath,
                        DisplayOrder = media.DisplayOrder,
                    });
                }
            }
        }

        // Branches, now that every clone stage exists: remap each target to the clone stage (by
        // navigation, so EF resolves the FK on save). Null target (Finish) copies across as null.
        foreach (var stage in source.Stages)
        {
            var stageClone = stageCloneBySourceId[stage.Id];
            foreach (var branch in stage.Branches.OrderBy(b => b.OrderIndex))
            {
                stageClone.Branches.Add(new StageBranch
                {
                    Stage = stageClone,
                    Label = branch.Label,
                    OrderIndex = branch.OrderIndex,
                    TargetStage = branch.TargetStageId is null ? null : stageCloneBySourceId[branch.TargetStageId.Value],
                });
            }
        }

        db.Workflows.Add(clone);
        await db.SaveChangesAsync(cancellationToken);
        return clone;
    }

    public async Task<Stage> AddStageAsync(int workflowId, string name, CancellationToken cancellationToken = default)
    {
        _ = await db.Workflows.FindAsync([workflowId], cancellationToken)
            ?? throw new WorkflowBuilderException($"Workflow {workflowId} does not exist.");

        var maxOrder = await db.Stages
            .Where(s => s.WorkflowId == workflowId)
            .Select(s => (int?)s.OrderIndex)
            .MaxAsync(cancellationToken) ?? -1;

        var stage = new Stage { WorkflowId = workflowId, Name = name, OrderIndex = maxOrder + 1 };
        db.Stages.Add(stage);
        await db.SaveChangesAsync(cancellationToken);
        return stage;
    }

    public async Task UpdateStageAsync(int stageId, string name, CancellationToken cancellationToken = default)
    {
        var stage = await db.Stages.FindAsync([stageId], cancellationToken)
            ?? throw new WorkflowBuilderException($"Stage {stageId} does not exist.");
        stage.Name = name;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetStageRequiresScanAsync(int stageId, bool requiresScan, CancellationToken cancellationToken = default)
    {
        var stage = await db.Stages.FindAsync([stageId], cancellationToken)
            ?? throw new WorkflowBuilderException($"Stage {stageId} does not exist.");
        stage.RequiresScan = requiresScan;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<StageBranch> AddStageBranchAsync(int stageId, string label, int? targetStageId, CancellationToken cancellationToken = default)
    {
        var stage = await db.Stages.FindAsync([stageId], cancellationToken)
            ?? throw new WorkflowBuilderException($"Stage {stageId} does not exist.");

        label = (label ?? string.Empty).Trim();
        if (label.Length == 0)
        {
            throw new WorkflowBuilderException("A branch needs a label.");
        }
        await ValidateBranchTargetAsync(stage.WorkflowId, targetStageId, cancellationToken);

        var maxOrder = await db.StageBranches
            .Where(b => b.StageId == stageId)
            .Select(b => (int?)b.OrderIndex)
            .MaxAsync(cancellationToken) ?? -1;

        var branch = new StageBranch { StageId = stageId, Label = label, TargetStageId = targetStageId, OrderIndex = maxOrder + 1 };
        db.StageBranches.Add(branch);
        await db.SaveChangesAsync(cancellationToken);
        return branch;
    }

    public async Task UpdateStageBranchAsync(int branchId, string label, int? targetStageId, CancellationToken cancellationToken = default)
    {
        var branch = await db.StageBranches.Include(b => b.Stage).SingleOrDefaultAsync(b => b.Id == branchId, cancellationToken)
            ?? throw new WorkflowBuilderException($"Branch {branchId} does not exist.");

        label = (label ?? string.Empty).Trim();
        if (label.Length == 0)
        {
            throw new WorkflowBuilderException("A branch needs a label.");
        }
        await ValidateBranchTargetAsync(branch.Stage.WorkflowId, targetStageId, cancellationToken);

        branch.Label = label;
        branch.TargetStageId = targetStageId;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteStageBranchAsync(int branchId, CancellationToken cancellationToken = default)
    {
        var branch = await db.StageBranches.FindAsync([branchId], cancellationToken)
            ?? throw new WorkflowBuilderException($"Branch {branchId} does not exist.");
        db.StageBranches.Remove(branch);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ReorderStageBranchesAsync(int stageId, IReadOnlyList<int> orderedBranchIds, CancellationToken cancellationToken = default)
    {
        var branches = await db.StageBranches.Where(b => b.StageId == stageId).ToListAsync(cancellationToken);

        if (branches.Count != orderedBranchIds.Count || !branches.Select(b => b.Id).ToHashSet().SetEquals(orderedBranchIds))
        {
            throw new WorkflowBuilderException("Reorder must include every branch on the stage exactly once.");
        }

        for (var i = 0; i < orderedBranchIds.Count; i++)
        {
            branches.Single(b => b.Id == orderedBranchIds[i]).OrderIndex = i;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // A branch target is either Finish (null) or a stage in the same workflow.
    private async Task ValidateBranchTargetAsync(int workflowId, int? targetStageId, CancellationToken cancellationToken)
    {
        if (targetStageId is null)
        {
            return;
        }
        var ok = await db.Stages.AnyAsync(s => s.Id == targetStageId && s.WorkflowId == workflowId, cancellationToken);
        if (!ok)
        {
            throw new WorkflowBuilderException("A branch can only target a stage in the same workflow.");
        }
    }

    public async Task DeleteStageAsync(int stageId, CancellationToken cancellationToken = default)
    {
        var stage = await db.Stages
            .Include(s => s.Steps).ThenInclude(st => st.MediaAssets)
            .SingleOrDefaultAsync(s => s.Id == stageId, cancellationToken)
            ?? throw new WorkflowBuilderException($"Stage {stageId} does not exist.");

        var mediaPaths = stage.Steps.SelectMany(s => s.MediaAssets).Select(m => m.FilePath).ToList();

        db.Stages.Remove(stage);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new WorkflowBuilderException(
                "Can't delete this stage — it still has work items or recorded step completions tied to it.");
        }

        foreach (var path in mediaPaths)
        {
            TryDeleteFile(path);
        }
    }

    public async Task ReorderStagesAsync(int workflowId, IReadOnlyList<int> orderedStageIds, CancellationToken cancellationToken = default)
    {
        var stages = await db.Stages.Where(s => s.WorkflowId == workflowId).ToListAsync(cancellationToken);

        if (stages.Count != orderedStageIds.Count || !stages.Select(s => s.Id).ToHashSet().SetEquals(orderedStageIds))
        {
            throw new WorkflowBuilderException("Reorder must include every stage in the workflow exactly once.");
        }

        for (var i = 0; i < orderedStageIds.Count; i++)
        {
            stages.Single(s => s.Id == orderedStageIds[i]).OrderIndex = i;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Step> AddStepAsync(int stageId, string name, string instructions, CancellationToken cancellationToken = default)
    {
        _ = await db.Stages.FindAsync([stageId], cancellationToken)
            ?? throw new WorkflowBuilderException($"Stage {stageId} does not exist.");

        var maxOrder = await db.Steps
            .Where(s => s.StageId == stageId)
            .Select(s => (int?)s.OrderIndex)
            .MaxAsync(cancellationToken) ?? -1;

        var step = new Step { StageId = stageId, Name = name, Instructions = instructions, OrderIndex = maxOrder + 1 };
        db.Steps.Add(step);
        await db.SaveChangesAsync(cancellationToken);
        return step;
    }

    public async Task UpdateStepAsync(int stepId, string name, string instructions, CancellationToken cancellationToken = default)
    {
        var step = await db.Steps.FindAsync([stepId], cancellationToken)
            ?? throw new WorkflowBuilderException($"Step {stepId} does not exist.");
        step.Name = name;
        step.Instructions = instructions;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteStepAsync(int stepId, CancellationToken cancellationToken = default)
    {
        var step = await db.Steps
            .Include(s => s.MediaAssets)
            .SingleOrDefaultAsync(s => s.Id == stepId, cancellationToken)
            ?? throw new WorkflowBuilderException($"Step {stepId} does not exist.");

        var mediaPaths = step.MediaAssets.Select(m => m.FilePath).ToList();

        db.Steps.Remove(step);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new WorkflowBuilderException(
                "Can't delete this step — it has recorded step completions tied to it.");
        }

        foreach (var path in mediaPaths)
        {
            TryDeleteFile(path);
        }
    }

    public async Task ReorderStepsAsync(int stageId, IReadOnlyList<int> orderedStepIds, CancellationToken cancellationToken = default)
    {
        var steps = await db.Steps.Where(s => s.StageId == stageId).ToListAsync(cancellationToken);

        if (steps.Count != orderedStepIds.Count || !steps.Select(s => s.Id).ToHashSet().SetEquals(orderedStepIds))
        {
            throw new WorkflowBuilderException("Reorder must include every step in the stage exactly once.");
        }

        for (var i = 0; i < orderedStepIds.Count; i++)
        {
            steps.Single(s => s.Id == orderedStepIds[i]).OrderIndex = i;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<StepInput> AddStepInputAsync(int stepId, string label, StepInputType type, bool required, string? options, CancellationToken cancellationToken = default)
    {
        _ = await db.Steps.FindAsync([stepId], cancellationToken)
            ?? throw new WorkflowBuilderException($"Step {stepId} does not exist.");

        label = (label ?? string.Empty).Trim();
        if (label.Length == 0)
        {
            throw new WorkflowBuilderException("An input needs a label.");
        }

        var maxOrder = await db.StepInputs
            .Where(i => i.StepId == stepId)
            .Select(i => (int?)i.OrderIndex)
            .MaxAsync(cancellationToken) ?? -1;

        var input = new StepInput
        {
            StepId = stepId,
            Label = label,
            Type = type,
            Required = required,
            Options = NormaliseOptions(type, options),
            OrderIndex = maxOrder + 1,
        };
        db.StepInputs.Add(input);
        await db.SaveChangesAsync(cancellationToken);
        return input;
    }

    public async Task UpdateStepInputAsync(int inputId, string label, StepInputType type, bool required, string? options, CancellationToken cancellationToken = default)
    {
        var input = await db.StepInputs.FindAsync([inputId], cancellationToken)
            ?? throw new WorkflowBuilderException($"Input {inputId} does not exist.");

        label = (label ?? string.Empty).Trim();
        if (label.Length == 0)
        {
            throw new WorkflowBuilderException("An input needs a label.");
        }

        input.Label = label;
        input.Type = type;
        input.Required = required;
        input.Options = NormaliseOptions(type, options);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteStepInputAsync(int inputId, CancellationToken cancellationToken = default)
    {
        var input = await db.StepInputs.FindAsync([inputId], cancellationToken)
            ?? throw new WorkflowBuilderException($"Input {inputId} does not exist.");

        db.StepInputs.Remove(input);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Restrict FK from StepCompletionValue — an input operators have already answered.
            throw new WorkflowBuilderException(
                "Can't delete this input — operators have already recorded answers for it.");
        }
    }

    public async Task ReorderStepInputsAsync(int stepId, IReadOnlyList<int> orderedInputIds, CancellationToken cancellationToken = default)
    {
        var inputs = await db.StepInputs.Where(i => i.StepId == stepId).ToListAsync(cancellationToken);

        if (inputs.Count != orderedInputIds.Count || !inputs.Select(i => i.Id).ToHashSet().SetEquals(orderedInputIds))
        {
            throw new WorkflowBuilderException("Reorder must include every input on the step exactly once.");
        }

        for (var i = 0; i < orderedInputIds.Count; i++)
        {
            inputs.Single(x => x.Id == orderedInputIds[i]).OrderIndex = i;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // Checklist keeps its item list; every other type has no options. Trims blank lines so an
    // empty box doesn't leave a stray checklist item.
    private static string? NormaliseOptions(StepInputType type, string? options)
    {
        if (type != StepInputType.Checklist || string.IsNullOrWhiteSpace(options))
        {
            return null;
        }

        var items = options
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);
        var cleaned = string.Join('\n', items);
        return cleaned.Length == 0 ? null : cleaned;
    }

    public async Task<MediaAsset> AddMediaAssetAsync(int stepId, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        _ = await db.Steps.FindAsync([stepId], cancellationToken)
            ?? throw new WorkflowBuilderException($"Step {stepId} does not exist.");

        var maxOrder = await db.MediaAssets
            .Where(m => m.StepId == stepId)
            .Select(m => (int?)m.DisplayOrder)
            .MaxAsync(cancellationToken) ?? -1;

        var relativePath = await SaveFileAsync(stepId, fileName, content, cancellationToken);

        var media = new MediaAsset { StepId = stepId, FilePath = relativePath, DisplayOrder = maxOrder + 1 };
        db.MediaAssets.Add(media);
        await db.SaveChangesAsync(cancellationToken);
        return media;
    }

    public async Task DeleteMediaAssetAsync(int mediaAssetId, CancellationToken cancellationToken = default)
    {
        var media = await db.MediaAssets.FindAsync([mediaAssetId], cancellationToken)
            ?? throw new WorkflowBuilderException($"Media asset {mediaAssetId} does not exist.");

        var path = media.FilePath;
        db.MediaAssets.Remove(media);
        await db.SaveChangesAsync(cancellationToken);

        TryDeleteFile(path);
    }

    private async Task<string> SaveFileAsync(int stepId, string fileName, Stream content, CancellationToken cancellationToken)
    {
        var stepDir = Path.Combine(mediaOptions.RootPath, stepId.ToString());
        Directory.CreateDirectory(stepDir);

        var storedFileName = $"{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
        var fullPath = Path.Combine(stepDir, storedFileName);

        await using (var fileStream = File.Create(fullPath))
        {
            await content.CopyToAsync(fileStream, cancellationToken);
        }

        return $"{stepId}/{storedFileName}";
    }

    private async Task<string> CopyMediaFileAsync(string sourceRelativePath, CancellationToken cancellationToken)
    {
        // Stage/Step IDs don't exist yet at clone time (not saved), so cloned files go under a
        // fresh GUID folder rather than reusing the source's StepId-named folder.
        var extension = Path.GetExtension(sourceRelativePath);
        var destRelativePath = $"{Guid.NewGuid():N}/{Guid.NewGuid():N}{extension}";

        var sourceFullPath = Path.Combine(mediaOptions.RootPath, sourceRelativePath);
        var destFullPath = Path.Combine(mediaOptions.RootPath, destRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destFullPath)!);

        if (File.Exists(sourceFullPath))
        {
            await using var sourceStream = File.OpenRead(sourceFullPath);
            await using var destStream = File.Create(destFullPath);
            await sourceStream.CopyToAsync(destStream, cancellationToken);
        }

        return destRelativePath;
    }

    private void TryDeleteFile(string relativePath)
    {
        try
        {
            var fullPath = Path.Combine(mediaOptions.RootPath, relativePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup — an orphaned file on disk isn't worth failing the delete for.
        }
    }
}
