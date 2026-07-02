using FlowLine.Domain.Entities.External;
using FlowLine.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Application.Staff;

public class StaffService(FlowLineDbContext db) : IStaffService
{
    public async Task<StaffMember?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        // Staff codes are the 4-digit staff number; a non-numeric entry can never match a row.
        if (!int.TryParse(code?.Trim(), out var staffNumber))
        {
            return null;
        }

        return await db.Staff
            .FirstOrDefaultAsync(s => s.StaffNumber == staffNumber, cancellationToken);
    }

    public Task<List<StaffMember>> GetStaffAsync(CancellationToken cancellationToken = default)
    {
        return db.Staff
            .OrderBy(s => s.StaffNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task<StaffMember> CreateStaffAsync(int staffNumber, string name, int level, CancellationToken cancellationToken = default)
    {
        name = ValidateNameAndLevel(name, level);
        if (staffNumber is < 1000 or > 9999)
        {
            throw new StaffServiceException("Staff number must be a 4-digit number (it is also the login code).");
        }
        if (await db.Staff.AnyAsync(s => s.StaffNumber == staffNumber, cancellationToken))
        {
            throw new StaffServiceException($"Staff number {staffNumber} is already taken.");
        }

        var staff = new StaffMember { StaffNumber = staffNumber, Name = name, TestingPower = level };
        db.Staff.Add(staff);
        await SaveAsync(cancellationToken);
        return staff;
    }

    public async Task UpdateStaffAsync(int staffNumber, string name, int level, CancellationToken cancellationToken = default)
    {
        name = ValidateNameAndLevel(name, level);
        var staff = await db.Staff.FirstOrDefaultAsync(s => s.StaffNumber == staffNumber, cancellationToken)
            ?? throw new StaffServiceException($"Staff member {staffNumber} does not exist.");

        staff.Name = name;
        staff.TestingPower = level;
        await SaveAsync(cancellationToken);
    }

    private static string ValidateNameAndLevel(string name, int level)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new StaffServiceException("Staff name cannot be blank.");
        }
        if (level is < AccessLevel.Staff or > AccessLevel.Manager)
        {
            throw new StaffServiceException("Access level must be 1 (operator), 2 (advanced), or 3 (manager).");
        }
        return name;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Only StaffNumber/Name/TestingPower are mapped; the real company StaffTable may
            // carry extra constrained columns FlowLine doesn't know about — surface that as a
            // readable message instead of a raw provider error.
            throw new StaffServiceException(
                $"The staff table rejected the change ({ex.GetBaseException().Message}). " +
                "If this is the company database, StaffTable may have required columns FlowLine doesn't manage.");
        }
    }
}
