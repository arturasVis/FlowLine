namespace FlowLine.Domain.Entities;

/// <summary>
/// Assigns a <see cref="Workflow"/> to a staff member (by their company staff number), controlling
/// which workflows a level-1 operator can see in the station picker. FlowLine-owned (unlike the
/// external StaffTable): <see cref="StaffNumber"/> is a plain int, deliberately NOT a foreign key,
/// because the staff table is company-owned/read-only and excluded from FlowLine's migrations.
/// </summary>
public class WorkflowAssignment
{
    public int Id { get; set; }

    public int WorkflowId { get; set; }
    public Workflow Workflow { get; set; } = null!;

    /// <summary>The company staff number (StaffTable."StaffNumber"). Not an FK — see class remarks.</summary>
    public int StaffNumber { get; set; }
}
