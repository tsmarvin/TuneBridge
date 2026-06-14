using System.Net.Http.Headers;
using System.Net.Http.Json;
using BridgeBeats.Core.Infrastructure.Logging;

namespace BridgeBeats.Core.Domain.Providers.Tidal {

    /// <summary>
    /// Acquires and caches a Tidal application access token using the OAuth2
    /// client-credentials flow, and produces ready-to-use authorization headers.
    /// </summary>
    /// <remarks>
    /// The handler posts a <c>client_credentials</c> grant to Tidal's token endpoint,
    /// authenticating the request with the base64 client credentials from
    /// <see cref="TidalCredentials"/> in an HTTP Basic header. The returned bearer
    /// token is cached in memory and reused until shortly before it expires; the
    /// cached expiry is set 30 seconds ahead of the token's reported lifetime so a
    /// refresh happens before the token actually lapses.
    /// </remarks>
    /// <param name="auth">The precomputed base64 Tidal client credentials.</param>
    /// <param name="factory">
    /// Factory used to create the named <c>tidal-auth</c> HTTP client for the token
    /// request.
    /// </param>
    /// <param name="logger">Logger for token-lifecycle diagnostics.</param>
    public sealed partial class TidalTokenHandler(
        TidalCredentials auth,
        IHttpClientFactory factory,
        ILogger<TidalTokenHandler> logger
    ) {
        /// <summary>The currently cached bearer token, or <see langword="null"/> if none has been acquired yet.</summary>
        private string? _cachedToken;

        /// <summary>The instant at which the cached token is treated as expired (30 seconds before its reported expiry).</summary>
        private DateTimeOffset _expiresAt;

        /// <summary>
        /// Returns a valid Tidal application access token, reusing the cached token
        /// while it remains fresh and otherwise requesting a new one.
        /// </summary>
        /// <returns>A valid bearer access token string.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the token endpoint returns a response that cannot be
        /// deserialized into a token payload.
        /// </exception>
        /// <exception cref="HttpRequestException">
        /// Thrown when the token request returns an unsuccessful HTTP status code.
        /// </exception>
        private async Task<string> GetAppTokenAsync( ) {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt) {
                return _cachedToken;
            }

            LogTokenExpiredOrUnset( logger );
            HttpClient client = factory.CreateClient("tidal-auth");
            using HttpRequestMessage req = new(HttpMethod.Post, "v1/oauth2/token") {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                    ["grant_type"] = "client_credentials"
                })
            };
            req.Headers.Authorization = NewBasicAuthenticationHeader( );

            using HttpResponseMessage response = await client.SendAsync(req);
            _ = response.EnsureSuccessStatusCode( );

            TokenResponse payload = await response.Content.ReadFromJsonAsync<TokenResponse>()
                         ?? throw new InvalidOperationException("Invalid tidal auth token response");

            _cachedToken = payload.access_token;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds( payload.expires_in - 30 ); // safety margin

            return _cachedToken;
        }

        /// <summary>
        /// Builds the HTTP Basic authorization header used to authenticate the token
        /// request, from the base64 client credentials.
        /// </summary>
        /// <returns>A <c>Basic</c> authorization header carrying the base64 client credentials.</returns>
        private AuthenticationHeaderValue NewBasicAuthenticationHeader( )
            => new( "Basic", auth.Credentials );

        /// <summary>
        /// Builds the HTTP Bearer authorization header for Tidal API calls, acquiring
        /// or reusing a cached access token as needed.
        /// </summary>
        /// <returns>A <c>Bearer</c> authorization header carrying a valid Tidal access token.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a new token must be acquired but the token endpoint returns a
        /// response that cannot be deserialized.
        /// </exception>
        /// <exception cref="HttpRequestException">
        /// Thrown when a new token must be acquired but the token request returns an
        /// unsuccessful HTTP status code.
        /// </exception>
        public async Task<AuthenticationHeaderValue> NewBearerAuthenticationHeader( )
            => new( "Bearer", await GetAppTokenAsync( ) );

        /// <summary>
        /// DTO mirroring the Tidal OAuth2 token endpoint response. Property names match
        /// the wire format.
        /// </summary>
        private sealed class TokenResponse {
#pragma warning disable IDE1006 // Naming Styles - these match the json values returned by tidal.
            /// <summary>Gets or sets the token type, mapped from the <c>token_type</c> member (typically <c>"Bearer"</c>).</summary>
            public string token_type { get; set; } = string.Empty;

            /// <summary>Gets or sets the access token, mapped from the <c>access_token</c> member.</summary>
            public string access_token { get; set; } = string.Empty;

            /// <summary>Gets or sets the token lifetime in seconds, mapped from the <c>expires_in</c> member.</summary>
            public int expires_in { get; set; }
#pragma warning restore IDE1006 // Naming Styles - these match the json values returned by tidal.
        }

        #region LoggerMessage Methods

        /// <summary>
        /// Logs (at Debug level) that the cached Tidal token is expired or unset and a
        /// new token is being generated.
        /// </summary>
        /// <param name="logger">The logger to write to.</param>
        [LoggerMessage(
            EventId = LogEventIds.Providers.Tidal.TokenExpiredOrUnset,
            Level = LogLevel.Debug,
            Message = "Tidal token is expired or unset. Generating a new token." )]
        internal static partial void LogTokenExpiredOrUnset( ILogger logger );

        #endregion LoggerMessage Methods
    }

}
