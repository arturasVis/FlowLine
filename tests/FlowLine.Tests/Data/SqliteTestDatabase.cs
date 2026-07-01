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
}
