namespace BridgeBeats.Contracts.Constants;

/// <summary>
/// Well-known health-probe route paths exposed by the services, kept here so callers and middleware
/// reference the same values.
/// </summary>
public static class EndpointPaths {

    /// <summary>
    /// Health-check path, <c>"/health"</c>. Reports a basic healthy status for the service.
    /// </summary>
    public const string Health = "/health";

    /// <summary>
    /// Liveness path, <c>"/alive"</c>. Reports whether the service process is running; access is
    /// restricted to internal callers.
    /// </summary>
    public const string Alive = "/alive";
}
