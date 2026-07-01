namespace FlowLine.Domain.Entities.External;

/// <summary>
/// A row of the company's pre-existing <c>Staff_Table</c> (owned by the company database, NOT
/// FlowLine). Mapped read-only and used purely as a lookup list — e.g. resolving a History row's
/// "Assigne Number" to a person's name. FlowLine never writes to it, and its migrations exclude
/// the table (<c>ExcludeFromMigrations</c>). Column names ("Staff number", "Testing Power") are
/// mapped verbatim via HasColumnName in OnModelCreating.
/// </summary>
public class StaffMember
{
    public int StaffNumber { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>The "Testing Power" column — a skill/authorization level. Read-only reference only.</summary>
    public int? TestingPower { get; set; }
}
