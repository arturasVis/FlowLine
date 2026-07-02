using FlowLine.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Tests.Data;

internal static class SqliteTestDatabase
{
    /// <summary>
    /// SQLite ":memory:" only persists for the lifetime of one open connection,
    /// so the connection must be kept open and shared across DbContexts in a test.
    /// </summary>
    public static (SqliteConnection Connection, DbContextOptions<FlowLineDbContext> Options) Create()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<FlowLineDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var context = new FlowLineDbContext(options))
        {
            context.Database.EnsureCreated();
        }

        return (connection, options);
    }

    /// <summary>
    /// Creates SQLite stand-ins for the company-owned external tables (StaffTable, History).
    /// They're ExcludeFromMigrations, so EnsureCreated skips them — tests exercising staff login,
    /// imports, or prebuild lookups create them by hand. Column names/types mirror the real company
    /// schema mapped in FlowLineDbContext.ConfigureExternalTables (Orderid/TestStatus/AssignedNumber,
    /// with QTY and AssignedNumber stored as text as they are on the company server).
    /// </summary>
    public static async Task CreateExternalTablesAsync(FlowLineDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE "StaffTable" (
                "StaffNumber" INTEGER NOT NULL PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "TestingPower" INTEGER NULL
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE "History" (
                "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                "Orderid" TEXT NOT NULL,
                "SKU" TEXT NOT NULL,
                "QTY" TEXT NOT NULL,
                "Channel" TEXT NULL,
                "Date" TEXT NOT NULL,
                "IsTested" INTEGER NOT NULL,
                "TestedBy" TEXT NULL,
                "TestStatus" TEXT NULL,
                "PackedBy" TEXT NULL,
                "PackedDate" TEXT NULL,
                "AssignedNumber" TEXT NULL
            );
            """);
    }
}
