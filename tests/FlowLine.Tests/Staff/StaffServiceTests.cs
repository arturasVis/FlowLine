using FlowLine.Application.Staff;
using FlowLine.Domain.Entities.External;
using FlowLine.Infrastructure.Data;
using FlowLine.Tests.Data;

namespace FlowLine.Tests.Staff;

public class StaffServiceTests
{
    private static async Task<FlowLineDbContext> NewDbWithStaffAsync(Microsoft.EntityFrameworkCore.DbContextOptions<FlowLineDbContext> options)
    {
        var db = new FlowLineDbContext(options);
        await SqliteTestDatabase.CreateExternalTablesAsync(db);
        db.Staff.AddRange(
            new StaffMember { StaffNumber = 1001, Name = "Alex Assembler", TestingPower = 1 },
            new StaffMember { StaffNumber = 1003, Name = "Morgan Manager", TestingPower = 3 });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task GetByCode_KnownCode_ReturnsStaffWithLevel()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = await NewDbWithStaffAsync(options);
            var service = new StaffService(db);

            var member = await service.GetByCodeAsync("1003");

            Assert.NotNull(member);
            Assert.Equal("Morgan Manager", member!.Name);
            Assert.Equal(AccessLevel.Manager, AccessLevel.Normalize(member.TestingPower));
        }
    }

    [Theory]
    [InlineData("9999")]  // no such staff number
    [InlineData("abc")]   // non-numeric can never match
    [InlineData("")]
    public async Task GetByCode_UnknownOrInvalid_ReturnsNull(string code)
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = await NewDbWithStaffAsync(options);
            var service = new StaffService(db);

            Assert.Null(await service.GetByCodeAsync(code));
        }
    }

    [Fact]
    public async Task GetStaff_ReturnsAllOrderedByNumber()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = await NewDbWithStaffAsync(options);
            var service = new StaffService(db);

            var staff = await service.GetStaffAsync();

            Assert.Equal([1001, 1003], staff.Select(s => s.StaffNumber));
        }
    }

    [Theory]
    [InlineData(null, AccessLevel.Staff)]
    [InlineData(1, AccessLevel.Staff)]
    [InlineData(2, AccessLevel.Advanced)]
    [InlineData(3, AccessLevel.Manager)]
    [InlineData(9, AccessLevel.Manager)]  // anything above 3 caps at manager
    public void Normalize_MapsTestingPowerToLevel(int? testingPower, int expected)
    {
        Assert.Equal(expected, AccessLevel.Normalize(testingPower));
    }
}
