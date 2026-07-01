using FlowLine.Application.Assignments;
using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using FlowLine.Tests.Data;

namespace FlowLine.Tests.Assignments;

public class AssignmentServiceTests
{
    private static async Task<(int WorkflowA, int WorkflowB)> SeedWorkflowsAsync(FlowLineDbContext db)
    {
        var a = new Workflow { Name = "Gaming PC Build" };
        var b = new Workflow { Name = "RMA Teardown" };
        db.Workflows.AddRange(a, b);
        await db.SaveChangesAsync();
        return (a.Id, b.Id);
    }

    [Fact]
    public async Task Assign_ThenQueries_ReflectAssignment()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var (wfA, wfB) = await SeedWorkflowsAsync(db);
            var service = new AssignmentService(db);

            await service.SetAssignmentAsync(wfA, 1001, assigned: true);
            await service.SetAssignmentAsync(wfB, 1001, assigned: true);
            await service.SetAssignmentAsync(wfA, 1004, assigned: true);

            Assert.Equal([1001, 1004], (await service.GetAssignedStaffNumbersAsync(wfA)).OrderBy(n => n));
            Assert.Equal([wfA, wfB], (await service.GetAssignedWorkflowIdsAsync(1001)).OrderBy(i => i));
            Assert.Equal([wfA], await service.GetAssignedWorkflowIdsAsync(1004));
        }
    }

    [Fact]
    public async Task Assign_IsIdempotent_NoDuplicateRow()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var (wfA, _) = await SeedWorkflowsAsync(db);
            var service = new AssignmentService(db);

            await service.SetAssignmentAsync(wfA, 1001, assigned: true);
            await service.SetAssignmentAsync(wfA, 1001, assigned: true);  // second time: no-op, no unique-index violation

            Assert.Single(await service.GetAssignedStaffNumbersAsync(wfA));
        }
    }

    [Fact]
    public async Task Unassign_RemovesAssignment_AndIsIdempotent()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = new FlowLineDbContext(options);
            var (wfA, _) = await SeedWorkflowsAsync(db);
            var service = new AssignmentService(db);

            await service.SetAssignmentAsync(wfA, 1001, assigned: true);
            await service.SetAssignmentAsync(wfA, 1001, assigned: false);
            await service.SetAssignmentAsync(wfA, 1001, assigned: false);  // already gone: no-op

            Assert.Empty(await service.GetAssignedStaffNumbersAsync(wfA));
        }
    }
}
