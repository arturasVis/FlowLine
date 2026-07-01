namespace FlowLine.Application.Orders;

/// <summary>
/// A company <c>History</c> row that has not yet been pulled into FlowLine as a WorkItem, with the
/// assignee's name already resolved from <c>Staff_Table</c>. Read-only projection — importing one
/// creates a FlowLine WorkItem but never writes back to History.
/// </summary>
public record ImportableOrder(
    string OrderId,
    string Sku,
    int Qty,
    string? Channel,
    DateTime Date,
    string? CompanyStatus,
    int? AssigneeNumber,
    string? AssigneeName);
