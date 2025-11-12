namespace TuneBridge.Domain.Implementations.Middleware;

/// <summary>
/// Middleware to restrict access to the health endpoint to internal requests only (localhost and Docker network).
/// Allows Aspire Dashboard and Caddy to access the health endpoint while blocking public access.
/// </summary>
public class HealthEndpointAuthorizationMiddleware {
    private readonly RequestDelegate _next;
    private readonly ILogger<HealthEndpointAuthorizationMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HealthEndpointAuthorizationMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">The logger instance.</param>
    public HealthEndpointAuthorizationMiddleware(
        RequestDelegate next,
        ILogger<HealthEndpointAuthorizationMiddleware> logger
    ) {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the middleware to check health endpoint access authorization.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InvokeAsync( HttpContext context ) {
        // Only intercept requests to /health endpoint
        if (!context.Request.Path.Equals( "/health", StringComparison.OrdinalIgnoreCase )) {
            await _next( context );
            return;
        }

        // Check if the request is coming from localhost or internal Docker network
        string? remoteIp = context.Connection.RemoteIpAddress?.ToString( );
        
        // Allow localhost and Docker internal IPs (172.x.x.x, 10.x.x.x ranges)
        // Also allow requests from the same host (::1 for IPv6 localhost)
        if (IsInternalRequest( remoteIp )) {
            await _next( context );
            return;
        }

        // Block external access
        _logger.LogWarning( "Blocked external access to health endpoint from {IP}", remoteIp );
        context.Response.StatusCode = 403;
        await context.Response.WriteAsync( "Forbidden: Health endpoint is not publicly accessible." );
    }

    /// <summary>
    /// Determines if a request is from an internal source.
    /// </summary>
    /// <param name="ipAddress">The IP address to check.</param>
    /// <returns>True if the request is internal, false otherwise.</returns>
    private static bool IsInternalRequest( string? ipAddress ) {
        if (string.IsNullOrWhiteSpace( ipAddress )) {
            return false;
        }

        // Localhost IPv4 and IPv6
        if (ipAddress == "::1" || ipAddress == "127.0.0.1" || ipAddress.StartsWith( "127." )) {
            return true;
        }

        // Docker internal networks
        // 172.16.0.0 - 172.31.255.255 (Docker default bridge network)
        // 10.0.0.0 - 10.255.255.255 (Private network)
        if (ipAddress.StartsWith( "172." ) || ipAddress.StartsWith( "10." )) {
            return true;
        }

        // IPv6 loopback
        if (ipAddress.StartsWith( "::ffff:127." )) {
            return true;
        }

        return false;
    }
}
