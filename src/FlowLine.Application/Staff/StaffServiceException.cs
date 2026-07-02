namespace FlowLine.Application.Staff;

/// <summary>A staff create/edit failed for a reason the manager can fix — shown as-is in the UI.</summary>
public class StaffServiceException(string message) : Exception(message);
