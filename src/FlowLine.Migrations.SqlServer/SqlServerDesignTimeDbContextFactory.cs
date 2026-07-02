using FlowLine.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FlowLine.Migrations.SqlServer;

/// <summary>
/// Lets the design-time `dotnet ef` tool build a <see cref="FlowLineDbContext"/> configured for
/// SQL Server with migrations emitted into *this* assembly. Only used by the tooling when adding
/// or scripting migrations — the running app configures the context itself in Program.cs. The
/// connection string here just has to be parseable; no server is contacted to scaffold a migration.
/// Override it with the FLOWLINE_SQLSERVER connection-string env var when scripting against a real DB.
/// </summary>
public sealed class SqlServerDesignTimeDbContextFactory : IDesignTimeDbContextFactory<FlowLineDbContext>
{
    public FlowLineDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("FLOWLINE_SQLSERVER")
            ?? "Server=WIN-K1TRUVHT0PC\\XUMGPC,1433;Database=xumlocal;User Id=XumAdmin;Password=Lolipopchainsaw3;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<FlowLineDbContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(typeof(SqlServerDesignTimeDbContextFactory).Assembly.GetName().Name))
            .Options;

        return new FlowLineDbContext(options);
    }
}
