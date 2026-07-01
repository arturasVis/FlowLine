namespace FlowLine.Application.Assignments;

/// <summary>
/// Manages which workflows a staff member is assigned to. Used by the manager assignment screen
/// and by the station picker to limit what a level-1 operator sees.
/// </summary>
public interface IAssignmentService
{
    /// <summary>Staff numbers currently assigned to the given workflow.</summary>
    Task<HashSet<int>> GetAssignedStaffNumbersAsync(int workflowId, CancellationToken cancellationToken = default);

    /// <summary>Workflow IDs the given staff member is assigned to.</summary>
    Task<HashSet<int>> GetAssignedWorkflowIdsAsync(int staffNumber, CancellationToken cancellationToken = default);

    /// <summary>Adds or removes an assignment; idempotent (no-op if already in the desired state).</summary>
    Task SetAssignmentAsync(int workflowId, int staffNumber, bool assigned, CancellationToken cancellationToken = default);
}
