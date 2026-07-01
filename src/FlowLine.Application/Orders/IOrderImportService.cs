namespace FlowLine.Application.Orders;

/// <summary>
/// Reads pending orders from the company-owned <c>History</c> table (read-only source) and turns
/// selected ones into FlowLine WorkItems. Deliberately separate from <see cref="IOrderService"/>:
/// this is the "import from the company DB" path, only meaningful on the SQL Server deployment
/// where those external tables exist. It never writes back to History or Staff_Table.
/// </summary>
public interface IOrderImportService
{
    /// <summary>
    /// True only when the app is running on the SQL Server deployment, where the company's
    /// History/Staff_Table tables exist. False under the SQLite dev provider, where the import
    /// feature has nothing to read — the UI shows an explanatory message instead of querying.
    /// </summary>
    bool ExternalTablesSupported { get; }

    /// <summary>
    /// History rows whose OrderId isn't already a FlowLine WorkItem, newest first, each with its
    /// assignee name resolved from Staff_Table.
    /// </summary>
    Task<List<ImportableOrder>> GetImportableOrdersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a queued WorkItem in the given workflow for each supplied History OrderId (skipping
    /// any that were imported in the meantime). Returns the number actually imported.
    /// </summary>
    Task<int> ImportAsync(int workflowId, IReadOnlyCollection<string> orderIds, CancellationToken cancellationToken = default);
}
