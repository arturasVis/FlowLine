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
    /// Creates SQLite stand-ins for the company-owned external tables (Staff_Table, History).
    /// They're ExcludeFromMigrations, so EnsureCreated skips them — tests exercising staff login,
    /// imports, or prebuild lookups create them by hand, matching the HasColumnName mapping
    /// (spaces included) in FlowLineDbContext.ConfigureExternalTables.
    /// </summary>
    public static async Task CreateExternalTablesAsync(FlowLineDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE "Staff_Table" (
                "Staff number" INTEGER NOT NULL PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "Testing Power" INTEGER NULL
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE "History" (
                "ID" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                "OrderId" TEXT NOT NULL,
                "SKU" TEXT NOT NULL,
                "QTY" INTEGER NOT NULL,
                "Channel" TEXT NULL,
                "Date" TEXT NOT NULL,
                "IsTested" INTEGER NOT NULL,
                "TestedBy" TEXT NULL,
                "Status" TEXT NULL,
                "PackedBy" TEXT NULL,
                "PackedDate" TEXT NULL,
                "Assigne Number" INTEGER NULL
            );
            """);
    }
}
