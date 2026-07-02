using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Application.Builder;

public class WorkflowBuilderService(FlowLineDbContext db, MediaStorageOptions mediaOptions) : IWorkflowBuilderService
{
    public Task<List<Workflow>> GetWorkflowsAsync(CancellationToken cancellationToken = default)
    {
        return db.Workflows
            .Include(w => w.Stages)
            .OrderByDescending(w => w.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<Workflow?> GetWorkflowAsync(int workflowId, CancellationToken cancellationToken = default)
    {
        return db.Workflows
            .Include(w => w.Stages).ThenInclude(s => s.Steps).ThenInclude(st => st.MediaAssets)
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
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Workflow> DuplicateWorkflowAsync(int workflowId, string newName, CancellationToken cancellationToken = default)
    {
        var source = await db.Workflows
            .Include(w => w.Stages).ThenInclude(s => s.Steps).ThenInclude(st => st.MediaAssets)
            .AsSplitQuery()
            .SingleOrDefaultAsync(w => w.Id == workflowId, cancellationToken)
            ?? throw new WorkflowBuilderException($"Workflow {workflowId} does not exist.");

        var clone = new Workflow { Name = newName, Description = source.Description, IsActive = true };

        foreach (var stage in source.Stages.OrderBy(s => s.OrderIndex))
        {
            var stageClone = new Stage { Workflow = clone, Name = stage.Name, OrderIndex = stage.OrderIndex };
            clone.Stages.Add(stageClone);

            foreach (var step in stage.Steps.OrderBy(s => s.OrderIndex))
            {
                var stepClone = new Step
                {
                    Stage = stageClone,
                    Name = step.Name,
                    Instructions = step.Instructions,
                    OrderIndex = step.OrderIndex,
                    RequiresScan = step.RequiresScan,
                };
                stageClone.Steps.Add(stepClone);

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

    public async Task UpdateStepAsync(int stepId, string name, string instructions, bool requiresScan, CancellationToken cancellationToken = default)
    {
        var step = await db.Steps.FindAsync([stepId], cancellationToken)
            ?? throw new WorkflowBuilderException($"Step {stepId} does not exist.");
        step.Name = name;
        step.Instructions = instructions;
        step.RequiresScan = requiresScan;
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
