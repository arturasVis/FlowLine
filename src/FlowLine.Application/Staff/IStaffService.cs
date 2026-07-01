using FlowLine.Domain.Entities.External;

namespace FlowLine.Application.Staff;

/// <summary>
/// Read-only access to the company <c>Staff_Table</c> for login and the assignment UI. Staff are
/// company-owned data (see <see cref="StaffMember"/>); this service never writes to the table.
/// On the SQLite dev provider a mock Staff_Table is seeded so login works locally too.
/// </summary>
public interface IStaffService
{
    /// <summary>The staff member whose 4-digit staff number equals <paramref name="code"/>, or null.</summary>
    Task<StaffMember?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Every staff member, ordered by staff number — for the workflow-assignment picker.</summary>
    Task<List<StaffMember>> GetStaffAsync(CancellationToken cancellationToken = default);
}
