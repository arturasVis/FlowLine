namespace FlowLine.Domain.Entities;

public enum WorkItemStatus
{
    Queued,
    InProgress,
    Completed,

    /// <summary>Voided by an admin (e.g. created by mistake). Terminal; excluded from stats.</summary>
    Cancelled,

    /// <summary>Failed on the line and physically junked. Terminal; excluded from stats.</summary>
    Scrapped
}
