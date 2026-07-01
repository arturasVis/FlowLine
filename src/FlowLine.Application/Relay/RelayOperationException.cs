namespace FlowLine.Application.Relay;

/// <summary>
/// Thrown for relay operations that are invalid given the current state of a
/// WorkItem/Station (e.g. advancing a unit your station hasn't claimed). Distinct
/// from a concurrency conflict (<see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>),
/// which is an expected race outcome, not a misuse error.
/// </summary>
public class RelayOperationException(string message) : Exception(message);
