using FlowLine.Domain.Entities;
using FlowLine.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Application.Assignments;

public class AssignmentService(FlowLineDbContext db) : IAssignmentService
{
    public async Task<HashSet<int>> GetAssignedStaffNumbersAsync(int workflowId, CancellationToken cancellationToken = default)
    {
        var numbers = await db.WorkflowAssignments
            .Where(a => a.WorkflowId == workflowId)
            .Select(a => a.StaffNumber)
            .ToListAsync(cancellationToken);
        return numbers.ToHashSet();
    }

    public async Task<HashSet<int>> GetAssignedWorkflowIdsAsync(int staffNumber, CancellationToken cancellationToken = default)
    {
        var ids = await db.WorkflowAssignments
            .Where(a => a.StaffNumber == staffNumber)
            .Select(a => a.WorkflowId)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    public async Task SetAssignmentAsync(int workflowId, int staffNumber, bool assigned, CancellationToken cancellationToken = default)
    {
        var existing = await db.WorkflowAssignments
            .FirstOrDefaultAsync(a => a.WorkflowId == workflowId && a.StaffNumber == staffNumber, cancellationToken);

        if (assigned && existing is null)
        {
            db.WorkflowAssignments.Add(new WorkflowAssignment { WorkflowId = workflowId, StaffNumber = staffNumber });
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (!assigned && existing is not null)
        {
            db.WorkflowAssignments.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
