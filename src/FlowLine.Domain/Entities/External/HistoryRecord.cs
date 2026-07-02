namespace FlowLine.Domain.Entities.External;

/// <summary>
/// A row of the company's pre-existing <c>History</c> table (owned by the company database, NOT
/// FlowLine). Mapped read-only: FlowLine reads pending orders from here to create WorkItems and
/// never writes back (see <c>OrderImportService</c>). EF's migrations deliberately exclude this
/// table (<c>ExcludeFromMigrations</c>) so <c>Database.Migrate()</c> never tries to create, alter,
/// or drop it. The real columns are fixed-width nchar/varchar and space-padded; the mapping in
/// OnModelCreating maps each C# property to its real column name (<c>Orderid</c>, <c>TestStatus</c>,
/// <c>AssignedNumber</c>, …) and attaches value converters that trim the padding and coerce the
/// nchar <c>QTY</c> / varchar <c>AssignedNumber</c> to their numeric CLR types on read.
/// </summary>
public class HistoryRecord
{
    public int Id { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int Qty { get; set; }
    public string? Channel { get; set; }
    public DateTime Date { get; set; }
    public bool IsTested { get; set; }
    public string? TestedBy { get; set; }

    /// <summary>The company <c>TestStatus</c> column (a testing outcome such as "Unknown"/"Pass").</summary>
    public string? Status { get; set; }
    public string? PackedBy { get; set; }
    public DateTime? PackedDate { get; set; }

    /// <summary>
    /// The company <c>AssignedNumber</c> column. It's a varchar that usually holds a non-numeric
    /// marker (e.g. "Unknown"), so it reads as null unless the value is a real staff number that
    /// resolves to a <see cref="StaffMember.StaffNumber"/>.
    /// </summary>
    public int? AssigneeNumber { get; set; }
}
