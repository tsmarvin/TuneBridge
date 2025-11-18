using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using TuneBridge.Configuration;
using TuneBridge.Domain.Models;

namespace TuneBridge.Web.Controllers;

/// <summary>
/// Controller for Spotify OAuth authentication and playlist management.
/// Provides endpoints for user authentication with Spotify and playlist access.
/// </summary>
public class SpotifyController : Controller {
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<SpotifyController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AppSettings _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpotifyController"/> class.
    /// </summary>
    /// <param name="userManager">User manager for ASP.NET Identity.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="httpClientFactory">HTTP client factory for Spotify API calls.</param>
    /// <param name="configuration">Application configuration.</param>
    public SpotifyController(
        UserManager<ApplicationUser> userManager,
        ILogger<SpotifyController> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration ) {
        _userManager = userManager;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _settings = new AppSettings( );
        configuration.GetRequiredSection( "TuneBridge" ).Bind( _settings );
    }

    /// <summary>
    /// Displays the Spotify playlists page (requires authentication).
    /// </summary>
    [Authorize]
    [HttpGet]
    [Route( "spotify/playlists" )]
    public IActionResult PlaylistsPage( ) {
        return View( "Playlists" );
    }

    /// <summary>
    /// Initiates the Spotify OAuth flow by redirecting to Spotify's authorization page.
    /// </summary>
    [Authorize]
    [HttpGet]
    [Route( "spotify/authorize" )]
    public IActionResult Authorize( ) {
        string scopes = "playlist-read-private playlist-read-collaborative user-library-read";
        string authUrl = $"https://accounts.spotify.com/authorize?" +
            $"client_id={Uri.EscapeDataString( _settings.SpotifyClientId )}" +
            $"&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString( _settings.SpotifyRedirectUri )}" +
            $"&scope={Uri.EscapeDataString( scopes )}";

        return Redirect( authUrl );
    }

    /// <summary>
    /// Handles the OAuth callback from Spotify and exchanges the authorization code for tokens.
    /// </summary>
    [Authorize]
    [HttpGet]
    [Route( "spotify/callback" )]
    public async Task<IActionResult> Callback( string? code, string? error ) {
        if (!string.IsNullOrEmpty( error )) {
            _logger.LogWarning( "Spotify OAuth error: {Error}", error );
            return RedirectToAction( "PlaylistsPage" );
        }

        if (string.IsNullOrEmpty( code )) {
            _logger.LogWarning( "Spotify OAuth callback received without code" );
            return RedirectToAction( "PlaylistsPage" );
        }

        ApplicationUser? user = await _userManager.GetUserAsync( User );
        if (user == null) {
            return Unauthorized( );
        }

        try {
            HttpClient client = _httpClientFactory.CreateClient( );
            string credentials = Convert.ToBase64String( Encoding.UTF8.GetBytes( $"{_settings.SpotifyClientId}:{_settings.SpotifyClientSecret}" ) );

            using HttpRequestMessage request = new( HttpMethod.Post, "https://accounts.spotify.com/api/token" ) {
                Content = new FormUrlEncodedContent( new Dictionary<string, string> {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = _settings.SpotifyRedirectUri
                } )
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue( "Basic", credentials );

            using HttpResponseMessage response = await client.SendAsync( request );
            _ = response.EnsureSuccessStatusCode( );

            string responseBody = await response.Content.ReadAsStringAsync( );
            using JsonDocument jsonDoc = JsonDocument.Parse( responseBody );
            JsonElement root = jsonDoc.RootElement;

            user.SpotifyAccessToken = root.GetProperty( "access_token" ).GetString( );
            user.SpotifyRefreshToken = root.GetProperty( "refresh_token" ).GetString( );
            int expiresIn = root.GetProperty( "expires_in" ).GetInt32( );
            user.SpotifyTokenExpiry = DateTime.UtcNow.AddSeconds( expiresIn );

            _ = await _userManager.UpdateAsync( user );

            _logger.LogInformation( "Spotify OAuth successful for user {UserId}", user.Id );
        } catch (Exception ex) {
            _logger.LogError( ex, "Error exchanging Spotify authorization code for tokens" );
        }

        return RedirectToAction( "PlaylistsPage" );
    }

    /// <summary>
    /// Gets the current Spotify authentication status for the logged-in user.
    /// </summary>
    [Authorize]
    [HttpGet]
    [Route( "spotify/status" )]
    public async Task<IActionResult> GetAuthStatus( ) {
        ApplicationUser? user = await _userManager.GetUserAsync( User );
        if (user == null) {
            return Unauthorized( );
        }

        bool isAuthenticated = !string.IsNullOrEmpty( user.SpotifyAccessToken );
        bool isExpired = user.SpotifyTokenExpiry.HasValue && user.SpotifyTokenExpiry.Value <= DateTime.UtcNow;

        return Ok( new {
            isAuthenticated = isAuthenticated && !isExpired,
            tokenExpiry = user.SpotifyTokenExpiry
        } );
    }

    /// <summary>
    /// Gets the user's Spotify playlists.
    /// </summary>
    [Authorize]
    [HttpGet]
    [Route( "spotify/playlists/list" )]
    public async Task<IActionResult> GetPlaylists( ) {
        ApplicationUser? user = await _userManager.GetUserAsync( User );
        if (user == null) {
            return Unauthorized( );
        }

        if (string.IsNullOrEmpty( user.SpotifyAccessToken )) {
            return BadRequest( new { error = "Not authenticated with Spotify" } );
        }

        // Refresh token if expired
        if (user.SpotifyTokenExpiry.HasValue && user.SpotifyTokenExpiry.Value <= DateTime.UtcNow) {
            bool refreshed = await RefreshAccessTokenAsync( user );
            if (!refreshed) {
                return BadRequest( new { error = "Failed to refresh Spotify token" } );
            }
        }

        try {
            HttpClient client = _httpClientFactory.CreateClient( );
            using HttpRequestMessage request = new( HttpMethod.Get, "https://api.spotify.com/v1/me/playlists?limit=50" );
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue( "Bearer", user.SpotifyAccessToken );

            using HttpResponseMessage response = await client.SendAsync( request );
            _ = response.EnsureSuccessStatusCode( );

            string responseBody = await response.Content.ReadAsStringAsync( );
            using JsonDocument jsonDoc = JsonDocument.Parse( responseBody );
            JsonElement root = jsonDoc.RootElement;

            if (root.TryGetProperty( "items", out JsonElement items )) {
                List<object> playlists = [];
                foreach (JsonElement item in items.EnumerateArray( )) {
                    playlists.Add( new {
                        id = item.GetProperty( "id" ).GetString( ),
                        name = item.GetProperty( "name" ).GetString( ),
                        owner = item.GetProperty( "owner" ).GetProperty( "display_name" ).GetString( ),
                        trackCount = item.GetProperty( "tracks" ).GetProperty( "total" ).GetInt32( ),
                        url = item.GetProperty( "external_urls" ).GetProperty( "spotify" ).GetString( )
                    } );
                }

                return Ok( new { playlists = playlists } );
            }

            return Ok( new { playlists = new List<object>( ) } );
        } catch (Exception ex) {
            _logger.LogError( ex, "Error fetching Spotify playlists for user {UserId}", user.Id );
            return StatusCode( 500, new { error = "Failed to fetch playlists" } );
        }
    }

    /// <summary>
    /// Refreshes the Spotify access token using the refresh token.
    /// </summary>
    private async Task<bool> RefreshAccessTokenAsync( ApplicationUser user ) {
        if (string.IsNullOrEmpty( user.SpotifyRefreshToken )) {
            return false;
        }

        try {
            HttpClient client = _httpClientFactory.CreateClient( );
            string credentials = Convert.ToBase64String( Encoding.UTF8.GetBytes( $"{_settings.SpotifyClientId}:{_settings.SpotifyClientSecret}" ) );

            using HttpRequestMessage request = new( HttpMethod.Post, "https://accounts.spotify.com/api/token" ) {
                Content = new FormUrlEncodedContent( new Dictionary<string, string> {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = user.SpotifyRefreshToken
                } )
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue( "Basic", credentials );

            using HttpResponseMessage response = await client.SendAsync( request );
            _ = response.EnsureSuccessStatusCode( );

            string responseBody = await response.Content.ReadAsStringAsync( );
            using JsonDocument jsonDoc = JsonDocument.Parse( responseBody );
            JsonElement root = jsonDoc.RootElement;

            user.SpotifyAccessToken = root.GetProperty( "access_token" ).GetString( );
            int expiresIn = root.GetProperty( "expires_in" ).GetInt32( );
            user.SpotifyTokenExpiry = DateTime.UtcNow.AddSeconds( expiresIn );

            _ = await _userManager.UpdateAsync( user );

            _logger.LogInformation( "Spotify token refreshed for user {UserId}", user.Id );
            return true;
        } catch (Exception ex) {
            _logger.LogError( ex, "Error refreshing Spotify token for user {UserId}", user.Id );
            return false;
        }
    }
}
