using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using TuneBridge.Domain.Models;

namespace TuneBridge.Domain.Implementations.Auth;

/// <summary>
/// Handles OAuth 2.0 authorization code flow for Tidal user authentication.
/// Manages user access tokens and refresh tokens for accessing user's library and playlists.
/// </summary>
public sealed class TidalUserAuthHandler {
    private readonly TidalCredentials _credentials;
    private readonly IHttpClientFactory _factory;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<TidalUserAuthHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TidalUserAuthHandler"/> class.
    /// </summary>
    public TidalUserAuthHandler(
        TidalCredentials credentials,
        IHttpClientFactory factory,
        UserManager<ApplicationUser> userManager,
        ILogger<TidalUserAuthHandler> logger ) {
        _credentials = credentials;
        _factory = factory;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Generates the Tidal OAuth authorization URL for user consent.
    /// </summary>
    /// <param name="redirectUri">The redirect URI registered in the Tidal app.</param>
    /// <param name="state">Optional state parameter for CSRF protection.</param>
    /// <returns>The authorization URL to redirect the user to.</returns>
    public string GetAuthorizationUrl( string redirectUri, string? state = null ) {
        string scopeList = "playlists.read";
        string url = $"https://login.tidal.com/authorize?response_type=code&client_id={Uri.EscapeDataString( _credentials.ClientId )}&redirect_uri={Uri.EscapeDataString( redirectUri )}&scope={Uri.EscapeDataString( scopeList )}";

        if (!string.IsNullOrEmpty( state )) {
            url += $"&state={Uri.EscapeDataString( state )}";
        }

        return url;
    }

    /// <summary>
    /// Exchanges an authorization code for access and refresh tokens.
    /// </summary>
    /// <param name="code">The authorization code received from the callback.</param>
    /// <param name="redirectUri">The redirect URI used in the authorization request.</param>
    /// <returns>Token response containing access token, refresh token, and expiration.</returns>
    public async Task<TidalTokenResponse?> ExchangeCodeForTokensAsync( string code, string redirectUri ) {
        try {
            HttpClient client = _factory.CreateClient("tidal-auth");
            using HttpRequestMessage req = new(HttpMethod.Post, "v1/oauth2/token") {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = redirectUri,
                    ["client_id"] = _credentials.ClientId,
                    ["client_secret"] = _credentials.ClientSecret
                })
            };

            using HttpResponseMessage response = await client.SendAsync(req);
            if (!response.IsSuccessStatusCode) {
                string errorBody = await response.Content.ReadAsStringAsync( );
                _logger.LogError( "Failed to exchange code for tokens. Status: {StatusCode}, Body: {Body}",
                    response.StatusCode, errorBody );
                return null;
            }

            TidalTokenResponse? tokenResponse = await response.Content.ReadFromJsonAsync<TidalTokenResponse>();
            return tokenResponse;
        } catch (Exception ex) {
            _logger.LogError( ex, "Error exchanging authorization code for tokens" );
            return null;
        }
    }

    /// <summary>
    /// Refreshes an expired access token using a refresh token.
    /// </summary>
    /// <param name="refreshToken">The refresh token.</param>
    /// <returns>New token response with fresh access token.</returns>
    public async Task<TidalTokenResponse?> RefreshAccessTokenAsync( string refreshToken ) {
        try {
            HttpClient client = _factory.CreateClient("tidal-auth");
            using HttpRequestMessage req = new(HttpMethod.Post, "v1/oauth2/token") {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken,
                    ["client_id"] = _credentials.ClientId,
                    ["client_secret"] = _credentials.ClientSecret
                })
            };

            using HttpResponseMessage response = await client.SendAsync(req);
            if (!response.IsSuccessStatusCode) {
                string errorBody = await response.Content.ReadAsStringAsync( );
                _logger.LogError( "Failed to refresh access token. Status: {StatusCode}, Body: {Body}",
                    response.StatusCode, errorBody );
                return null;
            }

            TidalTokenResponse? tokenResponse = await response.Content.ReadFromJsonAsync<TidalTokenResponse>();
            return tokenResponse;
        } catch (Exception ex) {
            _logger.LogError( ex, "Error refreshing access token" );
            return null;
        }
    }

    /// <summary>
    /// Gets a valid access token for the user, refreshing if necessary.
    /// </summary>
    /// <param name="user">The application user.</param>
    /// <returns>Valid access token or null if unable to obtain one.</returns>
    public async Task<string?> GetValidAccessTokenAsync( ApplicationUser user ) {
        if (string.IsNullOrEmpty( user.TidalAccessToken )) {
            return null;
        }

        // Check if token is expired (with 5 minute buffer)
        if (user.TidalTokenExpiry.HasValue && user.TidalTokenExpiry.Value > DateTime.UtcNow.AddMinutes( 5 )) {
            return user.TidalAccessToken;
        }

        // Token is expired or about to expire, refresh it
        if (string.IsNullOrEmpty( user.TidalRefreshToken )) {
            _logger.LogWarning( "User {UserId} has expired token but no refresh token", user.Id );
            return null;
        }

        TidalTokenResponse? newTokens = await RefreshAccessTokenAsync( user.TidalRefreshToken );
        if (newTokens == null) {
            return null;
        }

        // Update user with new tokens
        user.TidalAccessToken = newTokens.access_token;
        user.TidalTokenExpiry = DateTime.UtcNow.AddSeconds( newTokens.expires_in );

        // Only update refresh token if a new one was provided
        if (!string.IsNullOrEmpty( newTokens.refresh_token )) {
            user.TidalRefreshToken = newTokens.refresh_token;
        }

        IdentityResult result = await _userManager.UpdateAsync( user );
        if (!result.Succeeded) {
            _logger.LogError( "Failed to update user {UserId} with new Tidal tokens", user.Id );
            return null;
        }

        return user.TidalAccessToken;
    }

    /// <summary>
    /// Creates an authenticated HTTP client for Tidal API requests on behalf of a user.
    /// </summary>
    /// <param name="user">The application user.</param>
    /// <returns>Authenticated HTTP client or null if unable to authenticate.</returns>
    public async Task<HttpClient?> CreateUserAuthenticatedClientAsync( ApplicationUser user ) {
        string? accessToken = await GetValidAccessTokenAsync( user );
        if (string.IsNullOrEmpty( accessToken )) {
            return null;
        }

        HttpClient client = _factory.CreateClient("tidal-api");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "Bearer", accessToken );
        return client;
    }

    /// <summary>
    /// Token response from Tidal OAuth endpoint.
    /// </summary>
    public sealed class TidalTokenResponse {
#pragma warning disable IDE1006 // Naming Styles - these match the json values returned by tidal.
        /// <summary>OAuth token type, always "Bearer" for authorization code flow.</summary>
        public string token_type { get; set; } = string.Empty;

        /// <summary>The actual access token string to use in API Authorization headers.</summary>
        public string access_token { get; set; } = string.Empty;

        /// <summary>Token lifetime in seconds from issuance.</summary>
        public int expires_in { get; set; }

        /// <summary>Refresh token for obtaining new access tokens.</summary>
        public string? refresh_token { get; set; }

        /// <summary>Scope of permissions granted.</summary>
        public string? scope { get; set; }
#pragma warning restore IDE1006 // Naming Styles
    }
}
