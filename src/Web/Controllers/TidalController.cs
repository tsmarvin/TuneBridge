using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TuneBridge.Domain.Implementations.Auth;
using TuneBridge.Domain.Models;

namespace TuneBridge.Web.Controllers;

/// <summary>
/// Controller for Tidal user authentication and playlist management.
/// Provides OAuth flow and playlist listing for authenticated users.
/// </summary>
[Authorize]
public class TidalController : Controller {
    private readonly TidalUserAuthHandler _tidalAuth;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<TidalController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="TidalController"/> class.
    /// </summary>
    public TidalController(
        TidalUserAuthHandler tidalAuth,
        UserManager<ApplicationUser> userManager,
        ILogger<TidalController> logger,
        IHttpClientFactory httpClientFactory ) {
        _tidalAuth = tidalAuth;
        _userManager = userManager;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Displays the Tidal authentication and playlist page.
    /// </summary>
    [HttpGet]
    [Route( "tidal" )]
    public async Task<IActionResult> Index( ) {
        ApplicationUser? user = await _userManager.GetUserAsync( User );
        if (user == null) {
            return Unauthorized( );
        }

        bool isConnected = !string.IsNullOrEmpty( user.TidalAccessToken );

        return View( "Index", new TidalViewModel {
            IsConnected = isConnected,
            Email = user.Email
        } );
    }

    /// <summary>
    /// Initiates the Tidal OAuth authorization flow.
    /// </summary>
    [HttpGet]
    [Route( "tidal/connect" )]
    public IActionResult Connect( ) {
        string redirectUri = Url.Action( "Callback", "Tidal", null, Request.Scheme )!;
        string authUrl = _tidalAuth.GetAuthorizationUrl( redirectUri, Guid.NewGuid( ).ToString( ) );

        return Redirect( authUrl );
    }

    /// <summary>
    /// Handles the OAuth callback from Tidal.
    /// </summary>
    [HttpGet]
    [Route( "tidal/callback" )]
    public async Task<IActionResult> Callback( [FromQuery] string? code, [FromQuery] string? error ) {
        if (!string.IsNullOrEmpty( error )) {
            _logger.LogError( "Tidal OAuth error: {Error}", error );
            return RedirectToAction( "Index" );
        }

        if (string.IsNullOrEmpty( code )) {
            _logger.LogError( "No authorization code received from Tidal" );
            return RedirectToAction( "Index" );
        }

        ApplicationUser? user = await _userManager.GetUserAsync( User );
        if (user == null) {
            return Unauthorized( );
        }

        string redirectUri = Url.Action( "Callback", "Tidal", null, Request.Scheme )!;
        TidalUserAuthHandler.TidalTokenResponse? tokens = await _tidalAuth.ExchangeCodeForTokensAsync( code, redirectUri );

        if (tokens == null) {
            _logger.LogError( "Failed to exchange code for tokens for user {UserId}", user.Id );
            return RedirectToAction( "Index" );
        }

        // Store tokens in user profile
        user.TidalAccessToken = tokens.access_token;
        user.TidalRefreshToken = tokens.refresh_token;
        user.TidalTokenExpiry = DateTime.UtcNow.AddSeconds( tokens.expires_in );

        IdentityResult result = await _userManager.UpdateAsync( user );
        if (!result.Succeeded) {
            _logger.LogError( "Failed to save Tidal tokens for user {UserId}", user.Id );
        }

        return RedirectToAction( "Index" );
    }

    /// <summary>
    /// Disconnects the user's Tidal account.
    /// </summary>
    [HttpPost]
    [Route( "tidal/disconnect" )]
    public async Task<IActionResult> Disconnect( ) {
        ApplicationUser? user = await _userManager.GetUserAsync( User );
        if (user == null) {
            return Unauthorized( );
        }

        user.TidalAccessToken = null;
        user.TidalRefreshToken = null;
        user.TidalTokenExpiry = null;

        _ = await _userManager.UpdateAsync( user );

        return RedirectToAction( "Index" );
    }

    /// <summary>
    /// Gets the user's Tidal playlists.
    /// </summary>
    [HttpGet]
    [Route( "tidal/playlists" )]
    public async Task<IActionResult> GetPlaylists( ) {
        ApplicationUser? user = await _userManager.GetUserAsync( User );
        if (user == null) {
            return Unauthorized( );
        }

        HttpClient? client = await _tidalAuth.CreateUserAuthenticatedClientAsync( user );
        if (client == null) {
            return BadRequest( new { error = "Not connected to Tidal or token expired" } );
        }

        try {
            // Get user ID first
            HttpResponseMessage userResponse = await client.GetAsync( "userprofiles/me" );
            if (!userResponse.IsSuccessStatusCode) {
                _logger.LogError( "Failed to get Tidal user profile. Status: {StatusCode}", userResponse.StatusCode );
                return BadRequest( new { error = "Failed to get user profile from Tidal" } );
            }

            using System.Text.Json.JsonDocument userDoc = await System.Text.Json.JsonDocument.ParseAsync(
                await userResponse.Content.ReadAsStreamAsync( )
            );

            if (!userDoc.RootElement.TryGetProperty( "data", out var dataElement ) ||
                dataElement.ValueKind != System.Text.Json.JsonValueKind.Array) {
                return BadRequest( new { error = "Invalid user profile response" } );
            }

            var userArray = dataElement.EnumerateArray( );
            if (!userArray.MoveNext( )) {
                return BadRequest( new { error = "No user data found" } );
            }

            var userData = userArray.Current;
            if (!userData.TryGetProperty( "id", out var userIdElement )) {
                return BadRequest( new { error = "User ID not found" } );
            }

            string userId = userIdElement.GetString( ) ?? string.Empty;

            // Get playlists
            HttpResponseMessage playlistResponse = await client.GetAsync( $"userprofiles/{userId}/playlists" );
            if (!playlistResponse.IsSuccessStatusCode) {
                _logger.LogError( "Failed to get Tidal playlists. Status: {StatusCode}", playlistResponse.StatusCode );
                return BadRequest( new { error = "Failed to get playlists from Tidal" } );
            }

            using System.Text.Json.JsonDocument playlistDoc = await System.Text.Json.JsonDocument.ParseAsync(
                await playlistResponse.Content.ReadAsStreamAsync( )
            );

            List<PlaylistDto> playlists = [];

            if (playlistDoc.RootElement.TryGetProperty( "data", out var playlistData ) &&
                playlistData.ValueKind == System.Text.Json.JsonValueKind.Array) {
                foreach (var playlist in playlistData.EnumerateArray( )) {
                    if (playlist.TryGetProperty( "attributes", out var attributes ) &&
                        attributes.TryGetProperty( "name", out var nameElement ) &&
                        playlist.TryGetProperty( "id", out var idElement )) {
                        playlists.Add( new PlaylistDto {
                            Id = idElement.GetString( ) ?? string.Empty,
                            Name = nameElement.GetString( ) ?? "Unnamed Playlist"
                        } );
                    }
                }
            }

            return Ok( playlists );
        } catch (Exception ex) {
            _logger.LogError( ex, "Error fetching Tidal playlists for user {UserId}", user.Id );
            return StatusCode( 500, new { error = "An error occurred while fetching playlists" } );
        }
    }

    /// <summary>
    /// View model for the Tidal page.
    /// </summary>
    public class TidalViewModel {
        /// <summary>Whether the user is connected to Tidal.</summary>
        public bool IsConnected { get; set; }

        /// <summary>User's email address.</summary>
        public string? Email { get; set; }
    }

    /// <summary>
    /// DTO for playlist information.
    /// </summary>
    public class PlaylistDto {
        /// <summary>Playlist ID.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Playlist name.</summary>
        public string Name { get; set; } = string.Empty;
    }
}
