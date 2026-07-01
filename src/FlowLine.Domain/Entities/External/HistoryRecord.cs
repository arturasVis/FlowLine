namespace FlowLine.Domain.Entities.External;

/// <summary>
/// A row of the company's pre-existing <c>History</c> table (owned by the company database, NOT
/// FlowLine). Mapped read-only: FlowLine reads pending orders from here to create WorkItems and
/// never writes back (see <c>OrderImportService</c>). EF's migrations deliberately exclude this
/// table (<c>ExcludeFromMigrations</c>) so <c>Database.Migrate()</c> never tries to create, alter,
/// or drop it. Column names mirror the existing table exactly, including the space in
/// "Assigne Number", via HasColumnName in OnModelCreating.
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
    public string? Status { get; set; }
    public string? PackedBy { get; set; }
    public DateTime? PackedDate { get; set; }

    /// <summary>The "Assigne Number" column — references <see cref="StaffMember.StaffNumber"/>.</summary>
    public int? AssigneeNumber { get; set; }
}
