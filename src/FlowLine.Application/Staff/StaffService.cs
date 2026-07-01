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
}
