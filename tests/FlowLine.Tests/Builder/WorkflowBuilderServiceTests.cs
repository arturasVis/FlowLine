using FlowLine.Application.Builder;
using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using FlowLine.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Tests.Builder;

public class WorkflowBuilderServiceTests
{
    private static (WorkflowBuilderService Service, FlowLineDbContext Db, string MediaRoot, IDisposable Cleanup) CreateService(
        DbContextOptions<FlowLineDbContext> options)
    {
        var db = new FlowLineDbContext(options);
        var tempDir = Directory.CreateTempSubdirectory("flowline-media-test-");
        var service = new WorkflowBuilderService(db, new MediaStorageOptions { RootPath = tempDir.FullName });

        var cleanup = new CompositeDisposable(db, () =>
        {
            try { tempDir.Delete(recursive: true); } catch (IOException) { }
        });

        return (service, db, tempDir.FullName, cleanup);
    }

    private sealed class CompositeDisposable(IDisposable inner, Action onDispose) : IDisposable
    {
        public void Dispose()
        {
            inner.Dispose();
            onDispose();
        }
    }

    [Fact]
    public async Task CreateWorkflowAsync_CreatesAnActiveWorkflow()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            var (service, db, _, cleanup) = CreateService(options);
            using (db) using (cleanup)
            {
                var workflow = await service.CreateWorkflowAsync("Office PC Build", "A simpler build.");

                Assert.True(workflow.IsActive);
                Assert.Equal("Office PC Build", workflow.Name);

                var reloaded = await db.Workflows.SingleAsync(w => w.Id == workflow.Id);
                Assert.Equal("A simpler build.", reloaded.Description);
            }
        }
    }

    [Fact]
    public async Task UpdateWorkflowAsync_ChangesNameAndDescription()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            var (service, db, _, cleanup) = CreateService(options);
            using (db) using (cleanup)
            {
                var workflow = await service.CreateWorkflowAsync("Draft Name", null);

                await service.UpdateWorkflowAsync(workflow.Id, "Final Name", "Now with a description.");

                var reloaded = await db.Workflows.SingleAsync(w => w.Id == workflow.Id);
                Assert.Equal("Final Name", reloaded.Name);
                Assert.Equal("Now with a description.", reloaded.Description);
            }
        }
    }

    [Fact]
    public async Task SetWorkflowActiveAsync_TogglesIsActive()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            var (service, db, _, cleanup) = CreateService(options);
            using (db) using (cleanup)
            {
                var workflow = await service.CreateWorkflowAsync("Laptop Disassembly", null);

                await service.SetWorkflowActiveAsync(workflow.Id, false);
                Assert.False((await db.Workflows.SingleAsync(w => w.Id == workflow.Id)).IsActive);

                await service.SetWorkflowActiveAsync(workflow.Id, true);
                Assert.True((await db.Workflows.SingleAsync(w => w.Id == workflow.Id)).IsActive);
            }
        }
    }

    [Fact]
    public async Task AddStageAsync_AppendsAtTheEnd_WithIncrementingOrderIndex()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            var (service, db, _, cleanup) = CreateService(options);
            using (db) using (cleanup)
            {
                var workflow = await service.CreateWorkflowAsync("RMA Teardown", null);

                var first = await service.AddStageAsync(workflow.Id, "Inspect");
                var second = await service.AddStageAsync(workflow.Id, "Disassemble");

                Assert.Equal(0, first.OrderIndex);
                Assert.Equal(1, second.OrderIndex);
            }
        }
    }

    [Fact]
    public async Task ReorderStagesAsync_ReassignsOrderIndexToMatchGivenOrder()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            var (service, db, _, cleanup) = CreateService(options);
            using (db) using (cleanup)
            {
                var workflow = await service.CreateWorkflowAsync("RMA Teardown", null);
                var a = await service.AddStageAsync(workflow.Id, "A");
                var b = await service.AddStageAsync(workflow.Id, "B");
                var c = await service.AddStageAsync(workflow.Id, "C");

                await service.ReorderStagesAsync(workflow.Id, [c.Id, a.Id, b.Id]);

                var reloaded = await db.Stages.Where(s => s.WorkflowId == workflow.Id).OrderBy(s => s.OrderIndex).ToListAsync();
                Assert.Equal([c.Id, a.Id, b.Id], reloaded.Select(s => s.Id));
            }
        }
    }

    [Fact]
    public async Task ReorderStagesAsync_MissingAStage_Throws()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            var (service, db, _, cleanup) = CreateService(options);
            using (db) using (cleanup)
            {
                var workflow = await service.CreateWorkflowAsync("RMA Teardown", null);
                var a = await service.AddStageAsync(workflow.Id, "A");
                await service.AddStageAsync(workflow.Id, "B");

                await Assert.ThrowsAsync<WorkflowBuilderException>(
                    () => service.ReorderStagesAsync(workflow.Id, [a.Id]));
            }
        }
    }

    [Fact]
    public async Task DeleteStageAsync_EmptyStage_Succeeds()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            var (service, db, _, cleanup) = CreateService(options);
            using (db) using (cleanup)
            {
                var workflow = await service.CreateWorkflowAsync("RMA Teardown", null);
                var stage = await service.AddStageAsync(workflow.Id, "Inspect");

                await service.DeleteStageAsync(stage.Id);

                Assert.False(await db.Stages.AnyAsync(s => s.Id == stage.Id));
            }
        }
    }

    [Fact]
    public async Task DeleteStageAsync_StageWithAnActiveWorkItem_ThrowsAndLeavesItIntact()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            var (service, db, _, cleanup) = CreateService(options);
            using (db) using (cleanup)
            {
                var workflow = await service.CreateWorkflowAsync("RMA Teardown", null);
                var stage = await service.AddStageAsync(workflow.Id, "Inspect");

                // A separate DbContext, like the unrelated request that actually created this
                // WorkItem would have used — sharing `db`'s tracker here would make EF's
                // in-memory "severed required relationship" check fire before ever reaching the
                // database, masking the real FK Restrict guard this test means to exercise.
                using (var seedDb = new FlowLineDbContext(options))
                {
                    seedDb.WorkItems.Add(new WorkItem
                    {
                        WorkflowId = workflow.Id,
                        CurrentStageId = stage.Id,
                        OrderNumber = "ORD-1",
                        Sku = "SKU-1",
                        Quantity = 1,
                    });
                    await seedDb.SaveChangesAsync();
                }

                await Assert.ThrowsAsync<WorkflowBuilderException>(() => service.DeleteStageAsync(stage.Id));

                Assert.True(await db.Stages.AnyAsync(s => s.Id == stage.Id));
            }
        }
    }

    [Fact]
    public async Task AddStepAsync_AppendsAtTheEnd_WithIncrementingOrderIndex()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            var (service, db, _, cleanup) = CreateService(options);
            using (db) using (cleanup)
            {
                var workflow = await service.CreateWorkflowAsync("RMA Teardown", null);
                var stage = await service.AddStageAsync(workflow.Id, "Inspect");

                var first = await service.AddStepAsync(stage.Id, "Open box", "Open the RMA box.");
                var second = await service.AddStepAsync(stage.Id, "Photograph unit", "Take 4 photos.");

                Assert.Equal(0, first.OrderIndex);
                Assert.Equal(1, second.OrderIndex);
            }
        }
    }

    [Fact]
    public async Task DeleteStepAsync_StepWithARecordedCompletion_ThrowsAndLeavesItIntact()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            var (service, db, _, cleanup) = CreateService(options);
            using (db) using (cleanup)
            {
                var workflow = await service.CreateWorkflowAsync("RMA Teardown", null);
                var stage = await service.AddStageAsync(workflow.Id, "Inspect");
                var step = await service.AddStepAsync(stage.Id, "Open box", "Open the RMA box.");

                // Separate DbContext — see the comment in the equivalent Stage test above.
                using (var seedDb = new FlowLineDbContext(options))
                {
                    var workItem = new WorkItem
                    {
                        WorkflowId = workflow.Id,
                        CurrentStageId = stage.Id,
                        OrderNumber = "ORD-1",
                        Sku = "SKU-1",
                        Quantity = 1,
                    };
                    seedDb.WorkItems.Add(workItem);
                    await seedDb.SaveChangesAsync();

                    seedDb.StepCompletions.Add(new StepCompletion
                    {
                        WorkItemId = workItem.Id,
                        StepId = step.Id,
                        CompletedAtUtc = DateTime.UtcNow,
                    });
                    await seedDb.SaveChangesAsync();
                }

                await Assert.ThrowsAsync<WorkflowBuilderException>(() => service.DeleteStepAsync(step.Id));

                Assert.True(await db.Steps.AnyAsync(s => s.Id == step.Id));
            }
        }
    }

    [Fact]
    public async Task AddMediaAssetAsync_SavesFileUnderMediaRoot_AndCreatesRow()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            var (service, db, mediaRoot, cleanup) = CreateService(options);
            using (db) using (cleanup)
            {
                var workflow = await service.CreateWorkflowAsync("RMA Teardown", null);
                var stage = await service.AddStageAsync(workflow.Id, "Inspect");
                var step = await service.AddStepAsync(stage.Id, "Open box", "Open the RMA box.");

                using var content = new MemoryStream([1, 2, 3, 4]);
                var media = await service.AddMediaAssetAsync(step.Id, "photo.png", content);

                Assert.Equal(0, media.DisplayOrder);
                Assert.EndsWith(".png", media.FilePath);
                Assert.True(File.Exists(Path.Combine(mediaRoot, media.FilePath)));
            }
        }
    }

    [Fact]
    public async Task DeleteMediaAssetAsync_RemovesRowAndFile()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            var (service, db, mediaRoot, cleanup) = CreateService(options);
            using (db) using (cleanup)
            {
                var workflow = await service.CreateWorkflowAsync("RMA Teardown", null);
                var stage = await service.AddStageAsync(workflow.Id, "Inspect");
                var step = await service.AddStepAsync(stage.Id, "Open box", "Open the RMA box.");

                using var content = new MemoryStream([1, 2, 3, 4]);
                var media = await service.AddMediaAssetAsync(step.Id, "photo.png", content);
                var savedPath = Path.Combine(mediaRoot, media.FilePath);
                Assert.True(File.Exists(savedPath));

                await service.DeleteMediaAssetAsync(media.Id);

                Assert.False(await db.MediaAssets.AnyAsync(m => m.Id == media.Id));
                Assert.False(File.Exists(savedPath));
            }
        }
    }

    [Fact]
    public async Task DuplicateWorkflowAsync_ClonesStructureAndMedia_Independently()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            var (service, db, mediaRoot, cleanup) = CreateService(options);
            using (db) using (cleanup)
            {
                var workflow = await service.CreateWorkflowAsync("Gaming PC Build", "Original.");
                var stage = await service.AddStageAsync(workflow.Id, "Case Prep");
                var step = await service.AddStepAsync(stage.Id, "Unbox", "Unbox the case.");
                using var content = new MemoryStream([9, 9, 9]);
                var originalMedia = await service.AddMediaAssetAsync(step.Id, "case.png", content);

                var clone = await service.DuplicateWorkflowAsync(workflow.Id, "Gaming PC Build (Copy)");

                var fullClone = await service.GetWorkflowAsync(clone.Id);
                Assert.NotNull(fullClone);
                Assert.Equal("Gaming PC Build (Copy)", fullClone.Name);
                var clonedStage = Assert.Single(fullClone.Stages);
                Assert.Equal("Case Prep", clonedStage.Name);
                var clonedStep = Assert.Single(clonedStage.Steps);
                Assert.Equal("Unbox", clonedStep.Name);
                var clonedMedia = Assert.Single(clonedStep.MediaAssets);
                Assert.NotEqual(originalMedia.FilePath, clonedMedia.FilePath);

                var clonedFullPath = Path.Combine(mediaRoot, clonedMedia.FilePath);
                Assert.True(File.Exists(clonedFullPath));

                // Deleting the clone's media must not affect the original's file.
                await service.DeleteMediaAssetAsync(clonedMedia.Id);
                var originalFullPath = Path.Combine(mediaRoot, originalMedia.FilePath);
                Assert.True(File.Exists(originalFullPath));
            }
        }
    }

    [Fact]
    public async Task UpdateStepAsync_SetsRequiresScan_AndDuplicateCopiesIt()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            var (service, db, _, cleanup) = CreateService(options);
            using (cleanup)
            using (db)
            {
                var workflow = await service.CreateWorkflowAsync("WF", null);
                var stage = await service.AddStageAsync(workflow.Id, "S1");
                var step = await service.AddStepAsync(stage.Id, "Verify unit", "scan it");
                Assert.False(step.RequiresScan); // default off

                await service.UpdateStepAsync(step.Id, "Verify unit", "scan it", requiresScan: true);
                var reloaded = await db.Steps.SingleAsync(s => s.Id == step.Id);
                Assert.True(reloaded.RequiresScan);

                var copy = await service.DuplicateWorkflowAsync(workflow.Id, "WF Copy");
                var copiedStep = await db.Steps.SingleAsync(s => s.Stage.WorkflowId == copy.Id);
                Assert.True(copiedStep.RequiresScan); // deep clone keeps the flag
            }
        }
    }
}
