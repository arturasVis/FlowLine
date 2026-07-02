using FlowLine.Domain.Entities.External;

namespace FlowLine.Application.Staff;

/// <summary>
/// Access to the company <c>StaffTable</c> for login, the assignment UI, and manager-driven
/// staff management. Reads back login/lookup data; writes are limited to the three mapped
/// columns (StaffNumber, Name, TestingPower) via <see cref="CreateStaffAsync"/> and
/// <see cref="UpdateStaffAsync"/> — a deliberate exception (per user request) to the
/// otherwise read-only external-table rule; there is intentionally no delete, since staff
/// numbers are referenced by completion history and assignments. On the SQLite dev provider
/// a mock StaffTable is seeded so login works locally too.
/// </summary>
public interface IStaffService
{
    /// <summary>The staff member whose 4-digit staff number equals <paramref name="code"/>, or null.</summary>
    Task<StaffMember?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Every staff member, ordered by staff number — for the workflow-assignment picker.</summary>
    Task<List<StaffMember>> GetStaffAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a staff member. The number must be a free 4-digit number (it doubles as the login
    /// code), the name non-blank, and the level 1–3 (stored in TestingPower). Throws
    /// <see cref="StaffServiceException"/> with a manager-readable message otherwise.
    /// </summary>
    Task<StaffMember> CreateStaffAsync(int staffNumber, string name, int level, CancellationToken cancellationToken = default);

    /// <summary>Renames a staff member and/or changes their access level (1–3).</summary>
    Task UpdateStaffAsync(int staffNumber, string name, int level, CancellationToken cancellationToken = default);
}
