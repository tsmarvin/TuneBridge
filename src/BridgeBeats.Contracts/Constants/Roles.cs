namespace BridgeBeats.Contracts.Constants;

/// <summary>
/// Authorization role names used in access-control policies across the solution.
/// </summary>
public static class Roles {

    /// <summary>
    /// Role name <c>"AspireDashboardAccess"</c> that gates access to the Aspire dashboard. The role
    /// is created at startup but not assigned to any user by default; it must be assigned manually.
    /// </summary>
    public const string AspireDashboardAccess = "AspireDashboardAccess";
}
