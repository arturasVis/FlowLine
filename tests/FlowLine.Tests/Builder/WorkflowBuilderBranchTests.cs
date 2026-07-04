using FlowLine.Application.Builder;
using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using FlowLine.Tests.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowLine.Tests.Builder;

public class WorkflowBuilderBranchTests
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
    public async Task AddStageBranchAsync_ValidTarget_CreatesBranch()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            var (service, db, _, cleanup) = CreateService(options);
            using (db) using (cleanup)
            {
                var workflow = new Workflow { Name = "Test Workflow" };
                var stageA = new Stage { Workflow = workflow, Name = "Stage A", OrderIndex = 0 };
                var stageB = new Stage { Workflow = workflow, Name = "Stage B", OrderIndex = 1 };
                db.Workflows.Add(workflow);
                db.Stages.AddRange(stageA, stageB);
                await db.SaveChangesAsync();

                var branch = await service.AddStageBranchAsync(stageA.Id, "Go to B", stageB.Id);

                Assert.Equal("Go to B", branch.Label);
                Assert.Equal(stageB.Id, branch.TargetStageId);
                Assert.Equal(0, branch.OrderIndex);

                var reloaded = await db.StageBranches.SingleAsync(b => b.Id == branch.Id);
                Assert.Equal(stageA.Id, reloaded.StageId);
                Assert.Equal(stageB.Id, reloaded.TargetStageId);
            }
        }
    }

    [Fact]
    public async Task AddStageBranchAsync_InvalidTarget_Throws()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            var (service, db, _, cleanup) = CreateService(options);
            using (db) using (cleanup)
            {
                var workflow1 = new Workflow { Name = "Workflow 1" };
                var stageA = new Stage { Workflow = workflow1, Name = "Stage A", OrderIndex = 0 };
                var workflow2 = new Workflow { Name = "Workflow 2" };
                var stageB = new Stage { Workflow = workflow2, Name = "Stage B", OrderIndex = 0 };
                db.Workflows.AddRange(workflow1, workflow2);
                db.Stages.AddRange(stageA, stageB);
                await db.SaveChangesAsync();

                var ex = await Assert.ThrowsAsync<WorkflowBuilderException>(() =>
                    service.AddStageBranchAsync(stageA.Id, "Go to B", stageB.Id));
                Assert.Equal("A branch can only target a stage in the same workflow.", ex.Message);
            }
        }
    }

    [Fact]
    public async Task UpdateStageBranchAsync_Valid_UpdatesLabelAndTarget()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            var (service, db, _, cleanup) = CreateService(options);
            using (db) using (cleanup)
            {
                var workflow = new Workflow { Name = "Test Workflow" };
                var stageA = new Stage { Workflow = workflow, Name = "Stage A", OrderIndex = 0 };
                var stageB = new Stage { Workflow = workflow, Name = "Stage B", OrderIndex = 1 };
                var branch = new StageBranch { Stage = stageA, Label = "Old Label", TargetStage = stageB, OrderIndex = 0 };
                db.Workflows.Add(workflow);
                db.Stages.AddRange(stageA, stageB);
                db.StageBranches.Add(branch);
                await db.SaveChangesAsync();

                await service.UpdateStageBranchAsync(branch.Id, "New Label", null);

                var reloaded = await db.StageBranches.SingleAsync(b => b.Id == branch.Id);
                Assert.Equal("New Label", reloaded.Label);
                Assert.Null(reloaded.TargetStageId);
            }
        }
    }

    [Fact]
    public async Task DeleteStageBranchAsync_Deletes()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            var (service, db, _, cleanup) = CreateService(options);
            using (db) using (cleanup)
            {
                var workflow = new Workflow { Name = "Test Workflow" };
                var stageA = new Stage { Workflow = workflow, Name = "Stage A", OrderIndex = 0 };
                var branch = new StageBranch { Stage = stageA, Label = "Label", OrderIndex = 0 };
                db.Workflows.Add(workflow);
                db.Stages.Add(stageA);
                db.StageBranches.Add(branch);
                await db.SaveChangesAsync();

                await service.DeleteStageBranchAsync(branch.Id);

                var exists = await db.StageBranches.AnyAsync(b => b.Id == branch.Id);
                Assert.False(exists);
            }
        }
    }

    [Fact]
    public async Task ReorderStageBranchesAsync_Valid_Reorders()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            var (service, db, _, cleanup) = CreateService(options);
            using (db) using (cleanup)
            {
                var workflow = new Workflow { Name = "Test Workflow" };
                var stageA = new Stage { Workflow = workflow, Name = "Stage A", OrderIndex = 0 };
                var branch1 = new StageBranch { Stage = stageA, Label = "Branch 1", OrderIndex = 0 };
                var branch2 = new StageBranch { Stage = stageA, Label = "Branch 2", OrderIndex = 1 };
                db.Workflows.Add(workflow);
                db.Stages.Add(stageA);
                db.StageBranches.AddRange(branch1, branch2);
                await db.SaveChangesAsync();

                await service.ReorderStageBranchesAsync(stageA.Id, [branch2.Id, branch1.Id]);

                var reloaded1 = await db.StageBranches.SingleAsync(b => b.Id == branch1.Id);
                var reloaded2 = await db.StageBranches.SingleAsync(b => b.Id == branch2.Id);
                Assert.Equal(1, reloaded1.OrderIndex);
                Assert.Equal(0, reloaded2.OrderIndex);
            }
        }
    }

    [Fact]
    public async Task DuplicateWorkflowAsync_WithBranches_DuplicatesBranchesCorrectly()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            var (service, db, _, cleanup) = CreateService(options);
            using (db) using (cleanup)
            {
                var workflow = new Workflow { Name = "Test Workflow" };
                var stageA = new Stage { Workflow = workflow, Name = "Stage A", OrderIndex = 0 };
                var stageB = new Stage { Workflow = workflow, Name = "Stage B", OrderIndex = 1 };
                var branch = new StageBranch { Stage = stageA, Label = "Route to B", TargetStage = stageB, OrderIndex = 0 };
                db.Workflows.Add(workflow);
                db.Stages.AddRange(stageA, stageB);
                db.StageBranches.Add(branch);
                await db.SaveChangesAsync();

                var clone = await service.DuplicateWorkflowAsync(workflow.Id, "Clone Workflow");

                Assert.Equal("Clone Workflow", clone.Name);
                Assert.Equal(2, clone.Stages.Count);

                var cloneStageA = clone.Stages.Single(s => s.Name == "Stage A");
                var cloneStageB = clone.Stages.Single(s => s.Name == "Stage B");

                Assert.Single(cloneStageA.Branches);
                var cloneBranch = cloneStageA.Branches.Single();
                Assert.Equal("Route to B", cloneBranch.Label);
                Assert.Equal(cloneStageB.Id, cloneBranch.TargetStageId);
            }
        }
    }
}
