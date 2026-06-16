using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace BridgeBeats.Web.Authentication;

/// <summary>
/// Authentication handler for internal service-to-service calls (for example, a worker calling the
/// web API). It validates a shared service key supplied in a request header.
/// </summary>
/// <remarks>
/// The caller presents the key in the <see cref="InternalServiceDefaults.HeaderName"/> header. When the
/// header is absent the handler returns no result so other schemes can run. When the configured key is
/// missing, or the presented key does not match, authentication fails. On a match the handler issues a
/// principal carrying the <c>InternalService</c> name and role. The presented and configured keys are
/// trimmed of a leading byte-order mark before an ordinal comparison.
/// </remarks>
/// <param name="options">Monitor supplying the configured <see cref="InternalServiceAuthOptions"/>.</param>
/// <param name="logger">Logger factory supplied to the base authentication handler.</param>
/// <param name="encoder">URL encoder supplied to the base authentication handler.</param>
public class InternalServiceAuthHandler(
    IOptionsMonitor<InternalServiceAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<InternalServiceAuthOptions>( options, logger, encoder ) {
    /// <summary>
    /// Validates the service key header and produces an authentication result.
    /// </summary>
    /// <returns>
    /// <see cref="AuthenticateResult.NoResult"/> when the header is absent or empty;
    /// <see cref="AuthenticateResult.Fail(string)"/> when the key is unconfigured or does not match;
    /// otherwise a success result carrying the internal-service principal.
    /// </returns>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync( ) {
        if (!Request.Headers.TryGetValue( InternalServiceDefaults.HeaderName, out StringValues headerValue )) {
            return Task.FromResult( AuthenticateResult.NoResult( ) );
        }

        string providedKey = headerValue.ToString().Trim('\uFEFF');
        if (string.IsNullOrWhiteSpace( providedKey )) {
            return Task.FromResult( AuthenticateResult.NoResult( ) );
        }

        string configuredKey = Options.ServiceKey.Trim('\uFEFF');
        if (string.IsNullOrWhiteSpace( configuredKey )) {
            return Task.FromResult( AuthenticateResult.Fail( "Internal service authentication is not configured." ) );
        }

        byte[] providedBytes = Encoding.UTF8.GetBytes( providedKey );
        byte[] configuredBytes = Encoding.UTF8.GetBytes( configuredKey );
        if (!CryptographicOperations.FixedTimeEquals( providedBytes, configuredBytes )) {
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
/// Options for the internal-service authentication scheme.
/// </summary>
public class InternalServiceAuthOptions : AuthenticationSchemeOptions {
    /// <summary>
    /// The shared service key that an internal caller must present to authenticate. When empty,
    /// the scheme rejects all callers.
    /// </summary>
    public string ServiceKey { get; set; } = string.Empty;
}

/// <summary>
/// Constant identifiers for the internal-service authentication scheme.
/// </summary>
public static class InternalServiceDefaults {
    /// <summary>
    /// The name of the internal-service authentication scheme.
    /// </summary>
    public const string AuthenticationScheme = "InternalService";

    /// <summary>
    /// The request header that carries the internal-service key (<c>X-Service-Key</c>).
    /// </summary>
    public const string HeaderName = "X-Service-Key";
}
