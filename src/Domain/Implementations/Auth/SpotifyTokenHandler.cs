using System.Net.Http.Headers;

namespace TuneBridge.Domain.Implementations.Auth {

    /// <summary>
    /// Manages OAuth 2.0 client credentials flow for Spotify Web API authentication. Handles automatic
    /// token refresh with caching to minimize API calls and includes a 30-second safety margin to prevent
    /// using tokens that expire mid-request.
    /// </summary>
    /// <param name="auth">Pre-configured Spotify API credentials (client ID and secret in Base64 format).</param>
    /// <param name="factory">HTTP client factory configured with the Spotify auth endpoint (accounts.spotify.com).</param>
    /// <param name="logger">Logger for diagnostics, particularly useful for debugging token refresh issues.</param>
    /// <remarks>
    /// Tokens are cached until 30 seconds before expiration, at which point a new token is automatically
    /// requested on the next call to avoid using expired tokens during API requests.
    /// </remarks>
    public sealed class SpotifyTokenHandler(
        SpotifyCredentials auth,
        IHttpClientFactory factory,
        ILogger<SpotifyTokenHandler> logger
    ) {
        private string? _cachedToken;
        private DateTimeOffset _expiresAt;

        /// <summary>
        /// Retrieves a valid Spotify access token, either from the in-memory cache or by performing
        /// a new OAuth 2.0 client credentials grant request. The token is cached and reused until it
        /// expires (minus a 30-second safety buffer).
        /// </summary>
        /// <returns>
        /// A valid access token string that can be used in Authorization headers for Spotify API calls.
        /// The token is typically valid for 1 hour.
        /// </returns>
        /// <exception cref="HttpRequestException">
        /// Thrown if the Spotify auth server is unreachable or returns a non-success status code.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the Spotify API returns an unexpected or malformed token response.
        /// </exception>
        private async Task<string> GetAppTokenAsync( ) {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt) {
                return _cachedToken;
            }

            logger.LogDebug( "Spotify token is expired or unset. Generating a new token." );
            HttpClient client = factory.CreateClient("spotify-auth");
            using HttpRequestMessage req = new(HttpMethod.Post, "api/token") {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                    ["grant_type"] = "client_credentials"
                })
            };
            req.Headers.Authorization = NewBasicAuthenticationHeader( );

            using HttpResponseMessage response = await client.SendAsync(req);
            _ = response.EnsureSuccessStatusCode( );

            TokenResponse payload = await response.Content.ReadFromJsonAsync<TokenResponse>()
                         ?? throw new InvalidOperationException("Invalid spotify auth token response");

            _cachedToken = payload.access_token;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds( payload.expires_in - 30 ); // safety margin

            return _cachedToken;
        }

        /// <summary>
        /// Constructs a Basic authentication header using the Base64-encoded client credentials.
        /// This is used for the initial OAuth token request per the Spotify API specification.
        /// </summary>
        /// <returns>
        /// An <see cref="AuthenticationHeaderValue"/> with scheme "Basic" and the encoded credentials.
        /// </returns>
        private AuthenticationHeaderValue NewBasicAuthenticationHeader( )
            => new( "Basic", auth.Credentials );

        /// <summary>
        /// Creates a Bearer token authentication header for use in Spotify Web API requests. This method
        /// ensures the token is valid before returning, automatically refreshing expired tokens as needed.
        /// This is the primary public method used by Spotify API clients.
        /// </summary>
        /// <returns>
        /// An <see cref="AuthenticationHeaderValue"/> with scheme "Bearer" and a valid access token.
        /// Ready to be assigned to HttpClient.DefaultRequestHeaders.Authorization for API calls.
        /// </returns>
        /// <exception cref="HttpRequestException">
        /// Thrown if token refresh fails due to network issues or Spotify auth service being unavailable.
        /// </exception>
        public async Task<AuthenticationHeaderValue> NewBearerAuthenticationHeader( )
            => new( "Bearer", await GetAppTokenAsync( ) );

        /// <summary>
        /// Internal DTO for deserializing the JSON response from Spotify's OAuth token endpoint.
        /// Fields use snake_case naming to match Spotify's API response format exactly.
        /// </summary>
        private sealed class TokenResponse {
#pragma warning disable IDE1006 // Naming Styles - these match the json values returned by spotify.
            /// <summary>OAuth token type, always "Bearer" for client credentials flow.</summary>
            public string token_type { get; set; } = string.Empty;

            /// <summary>The actual access token string to use in API Authorization headers.</summary>
            public string access_token { get; set; } = string.Empty;

            /// <summary>Token lifetime in seconds from issuance. Typically 3600 (1 hour) for Spotify.</summary>
            public int expires_in { get; set; }
#pragma warning restore IDE1006 // Naming Styles - these match the json values returned by spotify.
        }
    }

}
