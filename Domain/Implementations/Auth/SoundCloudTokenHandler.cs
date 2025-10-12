using System.Net.Http.Headers;

namespace TuneBridge.Domain.Implementations.Auth {

    /// <summary>
    /// Manages OAuth 2.1 client credentials flow for SoundCloud API authentication.
    /// Handles automatic token refresh with caching to minimize API calls and includes 
    /// a 30-second safety margin to prevent using tokens that expire mid-request.
    /// </summary>
    /// <param name="auth">Pre-configured SoundCloud API credentials (client ID and secret).</param>
    /// <param name="factory">HTTP client factory configured with the SoundCloud OAuth endpoint.</param>
    /// <param name="logger">Logger for diagnostics, particularly useful for debugging token refresh issues.</param>
    /// <remarks>
    /// SoundCloud uses OAuth 2.1 with client credentials flow.
    /// See: https://developers.soundcloud.com/docs/api/guide#authentication
    /// Token endpoint: https://secure.soundcloud.com/oauth/token
    /// Tokens are cached until 30 seconds before expiration to avoid expired token errors.
    /// </remarks>
    public sealed class SoundCloudTokenHandler(
        SoundCloudCredentials auth,
        IHttpClientFactory factory,
        ILogger<SoundCloudTokenHandler> logger
    ) {
        private string? _cachedToken;
        private DateTimeOffset _expiresAt;

        /// <summary>
        /// Retrieves a valid SoundCloud access token, either from the in-memory cache or by performing
        /// a new OAuth 2.1 client credentials grant request. The token is cached and reused until it
        /// expires (minus a 30-second safety buffer).
        /// </summary>
        /// <returns>
        /// A valid access token string that can be used in Authorization headers for SoundCloud API calls.
        /// </returns>
        /// <exception cref="HttpRequestException">
        /// Thrown if the SoundCloud OAuth server is unreachable or returns a non-success status code.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the SoundCloud API returns an unexpected or malformed token response.
        /// </exception>
        private async Task<string> GetAppTokenAsync( ) {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt) {
                return _cachedToken;
            }

            logger.LogDebug( "SoundCloud token is expired or unset. Generating a new token." );
            HttpClient client = factory.CreateClient("soundcloud-oauth");
            
            using HttpRequestMessage req = new(HttpMethod.Post, "oauth/token") {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = auth.ClientId,
                    ["client_secret"] = auth.ClientSecret
                })
            };

            using HttpResponseMessage response = await client.SendAsync(req);
            _ = response.EnsureSuccessStatusCode( );

            TokenResponse payload = await response.Content.ReadFromJsonAsync<TokenResponse>()
                         ?? throw new InvalidOperationException("Invalid SoundCloud OAuth token response");

            _cachedToken = payload.access_token;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds( payload.expires_in - 30 ); // safety margin

            return _cachedToken;
        }

        /// <summary>
        /// Creates an OAuth authentication header for use in SoundCloud API requests.
        /// The header format is "OAuth {access_token}" as per SoundCloud API requirements.
        /// This method ensures the token is valid before returning, automatically refreshing expired tokens as needed.
        /// </summary>
        /// <returns>
        /// An <see cref="AuthenticationHeaderValue"/> with scheme "OAuth" and a valid access token.
        /// Ready to be assigned to HttpClient.DefaultRequestHeaders.Authorization for API calls.
        /// </returns>
        /// <exception cref="HttpRequestException">
        /// Thrown if token refresh fails due to network issues or SoundCloud OAuth service being unavailable.
        /// </exception>
        public async Task<AuthenticationHeaderValue> NewOAuthAuthenticationHeader( )
            => new( "OAuth", await GetAppTokenAsync( ) );

        /// <summary>
        /// Internal DTO for deserializing the JSON response from SoundCloud's OAuth token endpoint.
        /// Fields use snake_case naming to match SoundCloud's API response format exactly.
        /// </summary>
        private sealed class TokenResponse {
#pragma warning disable IDE1006 // Naming Styles - these match the json values returned by SoundCloud.
            /// <summary>The actual access token string to use in API Authorization headers.</summary>
            public string access_token { get; set; } = string.Empty;

            /// <summary>Token lifetime in seconds from issuance.</summary>
            public int expires_in { get; set; }
#pragma warning restore IDE1006 // Naming Styles - these match the json values returned by SoundCloud.
        }
    }

}
