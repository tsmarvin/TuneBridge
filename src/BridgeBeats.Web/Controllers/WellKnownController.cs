using BridgeBeats.Core.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BridgeBeats.Web.Controllers {
    /// <summary>
    /// Controller for well-known endpoints required by the ATProto OAuth specification.
    /// Serves dynamic client metadata and public JWKS for confidential client authentication.
    /// </summary>
    /// <remarks>
    /// These endpoints must be publicly accessible and match the client_id URL.
    /// The client-metadata.json endpoint is used by authorization servers to discover client capabilities.
    /// The jwks.json endpoint publishes the public signing key for verifying client assertion JWTs.
    /// </remarks>
    /// <remarks>
    /// Initializes a new instance of the <see cref="WellKnownController"/> class.
    /// </remarks>
    /// <param name="configuration">Application configuration for reading BaseUrl.</param>
    /// <param name="signingKeyProvider">Optional signing key provider for JWKS endpoint.</param>
    [AllowAnonymous]
    public class WellKnownController(
        IConfiguration configuration,
        ATProtoSigningKeyProvider? signingKeyProvider = null
    ) : Controller {
        private readonly string? _baseUrl = configuration.GetSection( "BridgeBeats" ).GetValue<string>( "BaseUrl" );

        /// <summary>
        /// Returns the ATProto OAuth client metadata document.
        /// This replaces the static .well-known/client-metadata.json file so that
        /// all URLs are derived dynamically from the configured BaseUrl.
        /// </summary>
        /// <returns>JSON client metadata document.</returns>
        [HttpGet( ".well-known/client-metadata.json" )]
        [ResponseCache( Duration = 3600, Location = ResponseCacheLocation.Any )]
        public IActionResult ClientMetadata( ) {
            if (string.IsNullOrWhiteSpace( _baseUrl )) {
                return NotFound( "BaseUrl is not configured." );
            }

            string baseUrl = _baseUrl.TrimEnd( '/' );
            string clientId = $"{baseUrl}/.well-known/client-metadata.json";

            Dictionary<string, object> metadata = new( ) {
                ["client_id"]                       = clientId,
                ["client_name"]                     = "BridgeBeats",
                ["client_uri"]                      = baseUrl,
                ["logo_uri"]                        = $"{baseUrl}/images/icon-192.png",
                ["tos_uri"]                         = $"{baseUrl}/privacy",
                ["policy_uri"]                      = $"{baseUrl}/privacy",
                ["application_type"]                = "web",
                ["grant_types"]                     = new[] { "authorization_code", "refresh_token" },
                ["response_types"]                  = new[] { "code" },
                ["scope"]                           = "atproto repo:link.bridgebeats.playlist",
                ["redirect_uris"]                   = new[] { $"{baseUrl}/account/atproto-callback" },
                ["token_endpoint_auth_method"]      = "private_key_jwt",
                ["dpop_bound_access_tokens"]        = true,
                ["token_endpoint_auth_signing_alg"] = "ES256",
                ["jwks_uri"]                        = $"{baseUrl}/.well-known/jwks.json"
            };

            return Json( metadata );
        }

        /// <summary>
        /// Returns the JSON Web Key Set (JWKS) containing the public signing key.
        /// Authorization servers use this to verify client assertion JWTs.
        /// Only the public key is exposed (no private key 'd' parameter).
        /// </summary>
        /// <returns>JWKS JSON document.</returns>
        [HttpGet( ".well-known/jwks.json" )]
        [ResponseCache( Duration = 3600, Location = ResponseCacheLocation.Any )]
        public IActionResult Jwks( ) {
            if (signingKeyProvider is null) {
                return NotFound( "No signing key configured." );
            }

            string jwksJson = signingKeyProvider.GetPublicJwks( );
            return Content( jwksJson, "application/json" );
        }
    }
}
