using System.Net.Http.Headers;

namespace TuneBridge.Domain.Implementations.Auth {

    public sealed class SpotifyTokenHandler(
        SpotifyCredentials auth,
        IHttpClientFactory factory,
        ILogger<SpotifyTokenHandler> logger
    ) {
        private string? _cachedToken;
        private DateTimeOffset _expiresAt;

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

        private AuthenticationHeaderValue NewBasicAuthenticationHeader( )
            => new( "Basic", auth.Credentials );

        public async Task<AuthenticationHeaderValue> NewBearerAuthenticationHeader( )
            => new( "Bearer", await GetAppTokenAsync( ) );

        private sealed class TokenResponse {
#pragma warning disable IDE1006 // Naming Styles - these match the json values returned by spotify.
            public string token_type { get; set; } = string.Empty;
            public string access_token { get; set; } = string.Empty;
            public int expires_in { get; set; }
#pragma warning restore IDE1006 // Naming Styles - these match the json values returned by spotify.
        }
    }

}
