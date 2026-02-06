using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BridgeBeats.Web.Authentication;

/// <summary>
/// Authentication handler for internal service-to-service communication.
/// Validates requests from trusted internal services (e.g., Discord worker) using a shared secret key.
/// </summary>
public class InternalServiceAuthHandler : AuthenticationHandler<InternalServiceAuthOptions> {

    /// <summary>
    /// Initializes a new instance of the <see cref="InternalServiceAuthHandler"/> class.
    /// </summary>
    public InternalServiceAuthHandler(
        IOptionsMonitor<InternalServiceAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : base( options, logger, encoder ) { }

    /// <summary>
    /// Validates the X-Service-Key header against the configured internal service key.
    /// </summary>
    /// <returns>An <see cref="AuthenticateResult"/> indicating success or failure.</returns>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync( ) {
        if (!Request.Headers.TryGetValue( InternalServiceDefaults.HeaderName, out Microsoft.Extensions.Primitives.StringValues headerValue )) {
            return Task.FromResult( AuthenticateResult.NoResult( ) );
        }

        string? providedKey = headerValue.ToString( );
        if (string.IsNullOrWhiteSpace( providedKey )) {
            return Task.FromResult( AuthenticateResult.NoResult( ) );
        }

        if (string.IsNullOrWhiteSpace( Options.ServiceKey )) {
            return Task.FromResult( AuthenticateResult.Fail( "Internal service authentication is not configured." ) );
        }

        if (!string.Equals( providedKey, Options.ServiceKey, StringComparison.Ordinal )) {
            return Task.FromResult( AuthenticateResult.Fail( "Invalid service key." ) );
        }

        Claim[] claims = [
            new Claim( ClaimTypes.Name, "InternalService" ),
            new Claim( ClaimTypes.Role, "InternalService" )
        ];
        ClaimsIdentity identity = new( claims, Scheme.Name );
        ClaimsPrincipal principal = new( identity );
        AuthenticationTicket ticket = new( principal, Scheme.Name );

        return Task.FromResult( AuthenticateResult.Success( ticket ) );
    }
}

/// <summary>
/// Options for the internal service authentication handler.
/// </summary>
public class InternalServiceAuthOptions : AuthenticationSchemeOptions {

    /// <summary>
    /// The shared secret key used to authenticate internal service-to-service requests.
    /// </summary>
    public string ServiceKey { get; set; } = string.Empty;
}

/// <summary>
/// Constants for internal service authentication.
/// </summary>
public static class InternalServiceDefaults {

    /// <summary>
    /// The authentication scheme name for internal service authentication.
    /// </summary>
    public const string AuthenticationScheme = "InternalService";

    /// <summary>
    /// The HTTP header name used to pass the internal service key.
    /// </summary>
    public const string HeaderName = "X-Service-Key";
}
