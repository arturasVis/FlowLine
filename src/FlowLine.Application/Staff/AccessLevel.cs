namespace FlowLine.Application.Staff;

/// <summary>
/// The company <c>Staff_Table."Testing Power"</c> column is really an access level:
/// 1 = normal staff, 2 = advanced (read-only reports), 3 = manager (full access).
/// A null/unknown level is treated as the least-privileged <see cref="Staff"/>.
/// </summary>
public static class AccessLevel
{
    public const int Staff = 1;
    public const int Advanced = 2;
    public const int Manager = 3;

    public static int Normalize(int? testingPower) =>
        testingPower is >= Manager ? Manager
        : testingPower is Advanced ? Advanced
        : Staff;
}

/// <summary>Custom claim types FlowLine puts on the auth cookie.</summary>
public static class FlowLineClaims
{
    /// <summary>The staff member's normalized access level (1/2/3) as a string.</summary>
    public const string Level = "flowline:level";
}

/// <summary>Authorization policy names, keyed off the level claim.</summary>
public static class FlowLinePolicies
{
    /// <summary>Level ≥ 2 — view Timing and the read-only workflow list.</summary>
    public const string CanViewReports = "CanViewReports";

    /// <summary>Level = 3 — edit workflows, orders, import, stations, assignments (manager).</summary>
    public const string CanManage = "CanManage";
}
