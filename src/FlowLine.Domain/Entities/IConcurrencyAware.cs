namespace FlowLine.Domain.Entities;

/// <summary>
/// Marks an entity as using an application-managed optimistic concurrency token.
/// SQLite has no native rowversion/timestamp type, so the token is a Guid that
/// FlowLineDbContext bumps on every update — see PRD NFR-4.
/// </summary>
public interface IConcurrencyAware
{
    Guid RowVersion { get; set; }
}
