using System.Net.Http.Headers;

namespace TuneBridge.Domain.Implementations.Auth {

    /// <summary>
    /// Manages OAuth token generation and caching for Spotify API authentication.
    /// </summary>
    /// <param name="auth">The Spotify credentials for authentication.</param>
    /// <param name="factory">The HTTP client factory for making token requests.</param>
    /// <param name="logger">The logger for recording authentication events.</param>
    public sealed class SpotifyTokenHandler(
        SpotifyCredentials auth,
        IHttpClientFactory factory,
        ILogger<SpotifyTokenHandler> logger
    ) {
        private string? _cachedToken;
        private DateTimeOffset _expiresAt;

        /// <summary>
        /// Gets a valid access token, either from cache or by requesting a new one from Spotify.
        /// </summary>
        /// <returns>A valid Spotify access token.</returns>
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
        /// Creates a Basic authentication header using the Spotify credentials.
        /// </summary>
        /// <returns>An authentication header with Basic scheme.</returns>
        private AuthenticationHeaderValue NewBasicAuthenticationHeader( )
            => new( "Basic", auth.Credentials );

        /// <summary>
        /// Creates a Bearer authentication header with a valid access token.
        /// </summary>
        /// <returns>An authentication header with Bearer scheme and access token.</returns>
        public async Task<AuthenticationHeaderValue> NewBearerAuthenticationHeader( )
            => new( "Bearer", await GetAppTokenAsync( ) );

        /// <summary>
        /// Represents the JSON response from the Spotify token endpoint.
        /// </summary>
        private sealed class TokenResponse {
#pragma warning disable IDE1006 // Naming Styles - these match the json values returned by spotify.
            /// <summary>The token type (e.g., "Bearer").</summary>
            public string token_type { get; set; } = string.Empty;

            /// <summary>The access token string.</summary>
            public string access_token { get; set; } = string.Empty;

            /// <summary>The number of seconds until the token expires.</summary>
            public int expires_in { get; set; }
#pragma warning restore IDE1006 // Naming Styles - these match the json values returned by spotify.
        }
    }

}
