using System.Net.Http.Headers;
using System.Net.Http.Json;
using BridgeBeats.Core.Infrastructure.Logging;

namespace BridgeBeats.Core.Domain.Providers.Spotify {

    /// <summary>
    /// Obtains and caches a Spotify application bearer token using the OAuth 2.0 client-credentials flow.
    /// </summary>
    /// <remarks>
    /// The token request is Basic-authenticated with the encoded <see cref="SpotifyCredentials.Credentials"/>.
    /// A fetched token is cached and reused until 30 seconds before its reported expiry, at which point a
    /// new token is requested on the next call to avoid using tokens that expire mid-request. The
    /// credential material is held only in its precomputed encoded form.
    /// </remarks>
    /// <param name="auth">The encoded Spotify credentials (client ID and secret in Base64 format) used to authenticate the token request.</param>
    /// <param name="factory">Factory used to create the <c>spotify-auth</c> HTTP client (configured with the accounts.spotify.com endpoint).</param>
    /// <param name="logger">Logger for token-refresh diagnostics.</param>
    public sealed partial class SpotifyTokenHandler(
        SpotifyCredentials auth,
        IHttpClientFactory factory,
        ILogger<SpotifyTokenHandler> logger
    ) {
        private string? _cachedToken;
        private DateTimeOffset _expiresAt;

        /// <summary>
        /// Returns a valid application bearer token, fetching and caching a new one when the cache is
        /// empty or within 30 seconds of expiry. Spotify tokens are typically valid for 1 hour.
        /// </summary>
        /// <returns>The cached or newly fetched access token.</returns>
        /// <exception cref="HttpRequestException">Thrown when the token request returns a non-success status.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the token response cannot be deserialized.</exception>
        private async Task<string> GetAppTokenAsync( ) {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt) {
                return _cachedToken;
            }

            LogTokenExpiredOrUnset( logger );
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
        /// Builds the Basic-auth header for the initial OAuth token request from the encoded credentials.
        /// </summary>
        /// <returns>A Basic <see cref="AuthenticationHeaderValue"/> carrying the encoded credentials.</returns>
        private AuthenticationHeaderValue NewBasicAuthenticationHeader( )
            => new( "Basic", auth.Credentials );

        /// <summary>
        /// Builds a Bearer-auth header carrying a current application token, refreshing it if needed. This
        /// is the primary public method used by Spotify API clients.
        /// </summary>
        /// <returns>A Bearer <see cref="AuthenticationHeaderValue"/> for authenticating Spotify API calls.</returns>
        /// <exception cref="HttpRequestException">Thrown when a required token refresh returns a non-success status.</exception>
        /// <exception cref="InvalidOperationException">Thrown when a required token refresh response cannot be deserialized.</exception>
        public async Task<AuthenticationHeaderValue> NewBearerAuthenticationHeader( )
            => new( "Bearer", await GetAppTokenAsync( ) );

        /// <summary>
        /// Deserialization target for the Spotify token endpoint response. Property names match the
        /// wire format (snake_case) and are intentionally not PascalCase.
        /// </summary>
        private sealed class TokenResponse {
#pragma warning disable IDE1006 // Naming Styles - these match the json values returned by spotify.
            /// <summary>The token type returned by Spotify (for example <c>Bearer</c>).</summary>
            public string token_type { get; set; } = string.Empty;

            /// <summary>The access token used as the Bearer credential.</summary>
            public string access_token { get; set; } = string.Empty;

            /// <summary>The token lifetime in seconds, as reported by Spotify (typically 3600).</summary>
            public int expires_in { get; set; }
#pragma warning restore IDE1006 // Naming Styles - these match the json values returned by spotify.
        }

        #region LoggerMessage Methods

        /// <summary>
        /// Logs that the Spotify token is expired or unset and a new token will be generated.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Providers.Spotify.TokenExpiredOrUnset,
            Level = LogLevel.Debug,
            Message = "Spotify token is expired or unset. Generating a new token." )]
        internal static partial void LogTokenExpiredOrUnset( ILogger logger );

        #endregion LoggerMessage Methods
    }

}
