using BridgeBeats.Core.Infrastructure.Identity;
using BridgeBeats.Web.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BridgeBeats.Web.Controllers {
    /// <summary>
    /// Serves the application's <c>.well-known</c> ATProto OAuth documents: the client metadata that
    /// identifies BridgeBeats as an OAuth client to ATProto authorization servers, and the JWKS containing
    /// the public signing key when private-key client authentication is configured. All endpoints allow
    /// anonymous access and are cacheable.
    /// </summary>
    /// <param name="configuration">Application configuration, used to read and normalize the configured public domain.</param>
    /// <param name="signingKeyProvider">Optional provider of the ATProto signing key; when present, the client uses private-key JWT authentication and exposes a JWKS, otherwise it declares no token-endpoint authentication.</param>
    [AllowAnonymous]
    public class WellKnownController(
        IConfiguration configuration,
        ATProtoSigningKeyProvider? signingKeyProvider = null
    ) : Controller {

        /// <summary>The normalized public domain (for example <c>https://example.com</c>) used to build absolute URIs in the well-known documents; <see langword="null"/> when not configured.</summary>
        private readonly string? _domain = AppSettings.NormalizeDomain(
            configuration.GetSection( "BridgeBeats" ).GetValue<string>( "Domain" )
        );
        /// <summary>The optional ATProto signing-key provider; when present the client advertises private-key JWT authentication and a JWKS endpoint.</summary>
        private readonly ATProtoSigningKeyProvider? _signingKeyProvider = signingKeyProvider;

        /// <summary>
        /// Serves the ATProto OAuth client-metadata document that describes BridgeBeats to authorization
        /// servers: client id and name, redirect URIs, grant and response types, requested scope, and the
        /// token-endpoint authentication method (private-key JWT with a JWKS reference when a signing key is
        /// configured, otherwise none). All URLs are derived dynamically from the configured Domain, replacing
        /// a static client-metadata.json file.
        /// </summary>
        /// <returns>
        /// HTTP GET <c>.well-known/client-metadata.json</c>. <c>200 OK</c> with the metadata as JSON when the
        /// domain is configured; <c>404 Not Found</c> when no domain is configured. Cacheable for one hour.
        /// </returns>
        [HttpGet( ".well-known/client-metadata.json" )]
        [ResponseCache( Duration = 3600, Location = ResponseCacheLocation.Any )]
        public IActionResult ClientMetadata( ) {
            if (string.IsNullOrWhiteSpace( _domain )) {
                return NotFound( "Domain is not configured." );
            }

            string clientId = $"{_domain}/.well-known/client-metadata.json";

            Dictionary<string, object> metadata = new( ) {
                ["client_id"]                = clientId,
                ["client_name"]              = "BridgeBeats",
                ["client_uri"]               = _domain,
                ["logo_uri"]                 = $"{_domain}/images/icon-192.png",
                ["tos_uri"]                  = $"{_domain}/privacy",
                ["policy_uri"]               = $"{_domain}/privacy",
                ["application_type"]         = "web",
                ["grant_types"]              = new[] { "authorization_code", "refresh_token" },
                ["response_types"]           = new[] { "code" },
                ["scope"]                    = "atproto repo:link.bridgebeats.playlist",
                ["redirect_uris"]            = new[] { $"{_domain}/account/atproto-callback" },
                ["dpop_bound_access_tokens"] = true
            };

            if (_signingKeyProvider is null) {
                metadata["token_endpoint_auth_method"] = "none";
            } else {
                metadata["token_endpoint_auth_method"] = "private_key_jwt";
                metadata["token_endpoint_auth_signing_alg"] = "ES256";
                metadata["jwks_uri"] = $"{_domain}/.well-known/jwks.json";
            }

            return Json( metadata );
        }

        /// <summary>
        /// Serves the JSON Web Key Set containing the public half of the ATProto signing key, allowing
        /// authorization servers to verify the client's private-key JWT authentication. Only the public key
        /// is exposed (no private key 'd' parameter).
        /// </summary>
        /// <returns>
        /// HTTP GET <c>.well-known/jwks.json</c>. <c>200 OK</c> with the JWKS as JSON when a signing key is
        /// configured; <c>404 Not Found</c> otherwise. Cacheable for one hour.
        /// </returns>
        [HttpGet( ".well-known/jwks.json" )]
        [ResponseCache( Duration = 3600, Location = ResponseCacheLocation.Any )]
        public IActionResult Jwks( ) {
            if (_signingKeyProvider is null) {
                return NotFound( "No signing key configured." );
            }

            string jwksJson = _signingKeyProvider.GetPublicJwks( );
            return Content( jwksJson, "application/json" );
        }
    }
}
