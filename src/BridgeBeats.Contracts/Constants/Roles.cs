namespace BridgeBeats.Contracts.Constants;

/// <summary>
/// Defines application role names for authorization.
/// </summary>
public static class Roles {
    /// <summary>
    /// Role required to access the Aspire Dashboard.
    /// This role is not assigned to any user by default and must be manually assigned via database edits.
    /// </summary>
    public const string AspireDashboardAccess = "AspireDashboardAccess";
}
