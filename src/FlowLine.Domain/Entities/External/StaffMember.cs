namespace FlowLine.Domain.Entities.External;

/// <summary>
/// A row of the company's pre-existing <c>StaffTable</c> (owned by the company database, NOT
/// FlowLine). Mapped read-only and used purely as a lookup list — e.g. resolving a History row's
/// "AssignedNumber" to a person's name. FlowLine never writes to it, and its migrations exclude
/// the table (<c>ExcludeFromMigrations</c>). Column names ("StaffNumber", "TestingPower") are
/// mapped verbatim via HasColumnName in OnModelCreating.
/// </summary>
public class StaffMember
{
    public int StaffNumber { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>The "TestingPower" column — a skill/authorization level. Read-only reference only.</summary>
    public int? TestingPower { get; set; }
}
