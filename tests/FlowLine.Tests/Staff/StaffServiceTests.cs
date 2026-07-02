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

    [Fact]
    public async Task CreateStaff_ValidInput_PersistsAndIsLoginable()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = await NewDbWithStaffAsync(options);
            var service = new StaffService(db);

            var created = await service.CreateStaffAsync(1005, "  Riley Rookie  ", AccessLevel.Advanced);

            Assert.Equal("Riley Rookie", created.Name); // trimmed
            Assert.Equal(AccessLevel.Advanced, created.TestingPower);

            // The new number works as a login code straight away.
            var byCode = await service.GetByCodeAsync("1005");
            Assert.NotNull(byCode);
            Assert.Equal("Riley Rookie", byCode.Name);
        }
    }

    [Theory]
    [InlineData(999)]   // too short
    [InlineData(10000)] // too long
    public async Task CreateStaff_NonFourDigitNumber_Throws(int staffNumber)
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = await NewDbWithStaffAsync(options);
            var service = new StaffService(db);

            await Assert.ThrowsAsync<StaffServiceException>(
                () => service.CreateStaffAsync(staffNumber, "Someone", AccessLevel.Staff));
        }
    }

    [Fact]
    public async Task CreateStaff_TakenNumber_Throws()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = await NewDbWithStaffAsync(options);
            var service = new StaffService(db);

            await Assert.ThrowsAsync<StaffServiceException>(
                () => service.CreateStaffAsync(1001, "Impostor", AccessLevel.Staff));
        }
    }

    [Theory]
    [InlineData("  ", AccessLevel.Staff)] // blank name
    [InlineData("Valid Name", 0)]         // level below range
    [InlineData("Valid Name", 4)]         // level above range
    public async Task CreateStaff_BlankNameOrBadLevel_Throws(string name, int level)
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = await NewDbWithStaffAsync(options);
            var service = new StaffService(db);

            await Assert.ThrowsAsync<StaffServiceException>(
                () => service.CreateStaffAsync(1005, name, level));
        }
    }

    [Fact]
    public async Task UpdateStaff_RenamesAndChangesLevel()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = await NewDbWithStaffAsync(options);
            var service = new StaffService(db);

            await service.UpdateStaffAsync(1001, "Alex A. Assembler", AccessLevel.Manager);

            var reloaded = await service.GetByCodeAsync("1001");
            Assert.NotNull(reloaded);
            Assert.Equal("Alex A. Assembler", reloaded.Name);
            Assert.Equal(AccessLevel.Manager, reloaded.TestingPower);
        }
    }

    [Fact]
    public async Task UpdateStaff_UnknownNumber_Throws()
    {
        var (connection, options) = SqliteTestDatabase.Create();
        using (connection)
        {
            using var db = await NewDbWithStaffAsync(options);
            var service = new StaffService(db);

            await Assert.ThrowsAsync<StaffServiceException>(
                () => service.UpdateStaffAsync(9999, "Nobody", AccessLevel.Staff));
        }
    }
}
