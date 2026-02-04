using BridgeBeats.Contracts.Constants;
using BridgeBeats.Web.Logging;

namespace BridgeBeats.Web.Middleware;

/// <summary>
/// Middleware to restrict access to the liveness endpoint to internal requests only (localhost and Docker network).
/// Allows Aspire Dashboard and Caddy to access the liveness endpoint while blocking public access.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="HealthEndpointAuthorizationMiddleware"/> class.
/// </remarks>
/// <param name="next">The next middleware in the pipeline.</param>
/// <param name="logger">The logger instance.</param>
public partial class HealthEndpointAuthorizationMiddleware(
    RequestDelegate next,
    ILogger<HealthEndpointAuthorizationMiddleware> logger
) {

    /// <summary>
    /// Invokes the middleware to check health endpoint access authorization.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InvokeAsync( HttpContext context ) {
        // Only intercept requests to liveness endpoint
        if (!context.Request.Path.Equals( EndpointPaths.Alive, StringComparison.OrdinalIgnoreCase )) {
            await next( context );
            return;
        }

        // Check if the request is coming from localhost or internal Docker network
        string? remoteIp = context.Connection.RemoteIpAddress?.ToString( );

        // Allow localhost and Docker internal IPs (172.x.x.x, 10.x.x.x ranges)
        // Also allow requests from the same host (::1 for IPv6 localhost)
        if (IsInternalRequest( remoteIp )) {
            await next( context );
            return;
        }

        // Block external access
        LogBlockedExternalAccess( logger, remoteIp );
        context.Response.StatusCode = 403;
        await context.Response.WriteAsync( "Forbidden: Liveness endpoint is not publicly accessible." );
    }

    /// <summary>
    /// Logs that external access to the liveness endpoint was blocked.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Middleware.HealthEndpointAuthorizationMiddlewareBlockedExternalAccess,
        Level = LogLevel.Warning,
        Message = "Blocked external access to liveness endpoint from {IP}" )]
    private static partial void LogBlockedExternalAccess( ILogger logger, string? ip );

    /// <summary>
    /// Determines if a request is from an internal source.
    /// </summary>
    /// <param name="ipAddress">The IP address to check.</param>
    /// <returns>True if the request is internal, false otherwise.</returns>
    private static bool IsInternalRequest( string? ipAddress ) {
        if (string.IsNullOrWhiteSpace( ipAddress )) {
            return false;
        }

        // Try to parse the IP address
        if (!System.Net.IPAddress.TryParse( ipAddress, out System.Net.IPAddress? parsedIp )) {
            return false;
        }

        // Check for localhost (IPv4 and IPv6)
        if (System.Net.IPAddress.IsLoopback( parsedIp )) {
            return true;
        }

        // Convert to bytes for range checking (IPv4 only for simplicity)
        if (parsedIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) {
            // For IPv6, only allow loopback which was already checked above
            return false;
        }

        byte[] bytes = parsedIp.GetAddressBytes( );

        // RFC 1918 private network range: 172.16.0.0/12 (172.16.0.0 - 172.31.255.255)
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) {
            return true;
        }

        // Private network: 10.0.0.0/8 (10.0.0.0 - 10.255.255.255)
        if (bytes[0] == 10) {
            return true;
        }

        // Private network: 192.168.0.0/16 (192.168.0.0 - 192.168.255.255)
        return bytes[0] == 192 && bytes[1] == 168;
    }
}
