using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TuneBridge.Domain.Models;
using TuneBridge.Domain.Implementations.Auth;
using System.Text.Json;

namespace TuneBridge.Web.Controllers;

/// <summary>
/// Controller for Apple Music user authentication and playlist management.
/// Provides endpoints for storing user tokens and listing playlists.
/// </summary>
[Authorize]
public class AppleMusicController : Controller {
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AppleMusicController> _logger;
    private readonly AppleJwtHandler? _jwtHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppleMusicController"/> class.
    /// </summary>
    /// <param name="userManager">User manager for ASP.NET Identity.</param>
    /// <param name="httpClientFactory">HTTP client factory for making API requests.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="jwtHandler">JWT handler for generating Apple Music developer tokens (optional).</param>
    public AppleMusicController(
        UserManager<ApplicationUser> userManager,
        IHttpClientFactory httpClientFactory,
        ILogger<AppleMusicController> logger,
        AppleJwtHandler? jwtHandler = null ) {
        _userManager = userManager;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _jwtHandler = jwtHandler;
    }

    /// <summary>
    /// Displays the Apple Music authentication page.
    /// </summary>
    [HttpGet]
    [Route( "applemusic" )]
    public IActionResult Index( ) {
        return View( );
    }

    /// <summary>
    /// Gets a developer token for MusicKit JS authentication.
    /// </summary>
    /// <returns>Developer token.</returns>
    /// <response code="200">Developer token retrieved successfully.</response>
    /// <response code="503">Apple Music service not available.</response>
    [HttpGet]
    [Route( "applemusic/developer-token" )]
    public IActionResult GetDeveloperToken( ) {
        if (_jwtHandler == null) {
            return StatusCode( 503, new { message = "Apple Music service is not configured" } );
        }

        string token = _jwtHandler.NewAuthenticationHeader( ).Parameter ?? string.Empty;
        return Ok( new { token = token } );
    }

    /// <summary>Request for storing Apple Music user token.</summary>
    /// <param name="UserToken">Apple Music user token from MusicKit JS.</param>
    /// <param name="ExpiresInMs">Token expiration time in milliseconds.</param>
    public record StoreTokenRequest( string UserToken, long ExpiresInMs );

    /// <summary>
    /// Stores the Apple Music user token for the authenticated user.
    /// </summary>
    /// <param name="request">Token details including the user token and expiration.</param>
    /// <returns>Success confirmation.</returns>
    /// <response code="200">Token stored successfully.</response>
    /// <response code="400">Invalid request.</response>
    /// <response code="401">User not authenticated.</response>
    [HttpPost]
    [Route( "applemusic/store-token" )]
    public async Task<IActionResult> StoreToken( [FromBody] StoreTokenRequest request ) {
        if (!ModelState.IsValid) {
            return BadRequest( ModelState );
        }

        ApplicationUser? user = await _userManager.GetUserAsync( User );
        if (user == null) {
            return Unauthorized( );
        }

        user.AppleMusicUserToken = request.UserToken;
        user.AppleMusicTokenExpiration = DateTime.UtcNow.AddMilliseconds( request.ExpiresInMs );

        Microsoft.AspNetCore.Identity.IdentityResult result = await _userManager.UpdateAsync( user );

        if (!result.Succeeded) {
            _logger.LogError( "Failed to store Apple Music token for user {UserId}", user.Id );
            return BadRequest( new { message = "Failed to store token" } );
        }

        _logger.LogInformation( "Apple Music token stored for user {UserId}", user.Id );

        return Ok( new { message = "Token stored successfully" } );
    }

    /// <summary>
    /// Gets the list of playlists for the authenticated user.
    /// </summary>
    /// <returns>List of playlists with name and ID.</returns>
    /// <response code="200">Playlists retrieved successfully.</response>
    /// <response code="401">User not authenticated or token expired.</response>
    /// <response code="500">Failed to retrieve playlists.</response>
    [HttpGet]
    [Route( "applemusic/playlists" )]
    public async Task<IActionResult> GetPlaylists( ) {
        ApplicationUser? user = await _userManager.GetUserAsync( User );
        if (user == null) {
            return Unauthorized( new { message = "User not authenticated" } );
        }

        if (string.IsNullOrEmpty( user.AppleMusicUserToken )) {
            return Unauthorized( new { message = "Apple Music token not found. Please authenticate first." } );
        }

        if (user.AppleMusicTokenExpiration.HasValue && user.AppleMusicTokenExpiration.Value < DateTime.UtcNow) {
            return Unauthorized( new { message = "Apple Music token expired. Please authenticate again." } );
        }

        try {
            HttpClient client = _httpClientFactory.CreateClient( "musickit-api" );
            
            // Add developer token (JWT) for API authentication
            if (_jwtHandler != null) {
                client.DefaultRequestHeaders.Authorization = _jwtHandler.NewAuthenticationHeader( );
            }
            
            // Add user token for accessing user's library
            client.DefaultRequestHeaders.Add( "Music-User-Token", user.AppleMusicUserToken );

            HttpResponseMessage response = await client.GetAsync( "https://api.music.apple.com/v1/me/library/playlists" );

            if (!response.IsSuccessStatusCode) {
                _logger.LogError( "Failed to retrieve playlists for user {UserId}: {StatusCode}", user.Id, response.StatusCode );
                return StatusCode( (int)response.StatusCode, new { message = "Failed to retrieve playlists from Apple Music" } );
            }

            string content = await response.Content.ReadAsStringAsync( );
            using JsonDocument doc = JsonDocument.Parse( content );

            List<object> playlists = [];
            if (doc.RootElement.TryGetProperty( "data", out JsonElement dataElement )) {
                foreach (JsonElement playlist in dataElement.EnumerateArray( )) {
                    if (playlist.TryGetProperty( "id", out JsonElement idElement ) &&
                        playlist.TryGetProperty( "attributes", out JsonElement attributesElement ) &&
                        attributesElement.TryGetProperty( "name", out JsonElement nameElement )) {
                        playlists.Add( new {
                            id = idElement.GetString( ),
                            name = nameElement.GetString( )
                        } );
                    }
                }
            }

            return Ok( new { playlists = playlists } );

        } catch (Exception ex) {
            _logger.LogError( ex, "Error retrieving playlists for user {UserId}", user.Id );
            return StatusCode( 500, new { message = "An error occurred while retrieving playlists" } );
        }
    }

    /// <summary>
    /// Gets the current authentication status for Apple Music.
    /// </summary>
    /// <returns>Authentication status including whether token exists and is valid.</returns>
    [HttpGet]
    [Route( "applemusic/status" )]
    public async Task<IActionResult> GetStatus( ) {
        ApplicationUser? user = await _userManager.GetUserAsync( User );
        if (user == null) {
            return Unauthorized( );
        }

        bool hasToken = !string.IsNullOrEmpty( user.AppleMusicUserToken );
        bool isExpired = user.AppleMusicTokenExpiration.HasValue && user.AppleMusicTokenExpiration.Value < DateTime.UtcNow;

        return Ok( new {
            hasToken = hasToken,
            isExpired = isExpired,
            expiresAt = user.AppleMusicTokenExpiration
        } );
    }

    /// <summary>Request for processing a playlist.</summary>
    /// <param name="PlaylistId">Apple Music playlist ID.</param>
    public record ProcessPlaylistRequest( string PlaylistId );

    /// <summary>
    /// Processes a playlist by fetching its tracks and converting them via URLList endpoint.
    /// Only processes playlists with less than 100 tracks.
    /// </summary>
    /// <param name="request">Request containing the playlist ID to process.</param>
    /// <returns>Processing result with track URLs or error message.</returns>
    /// <response code="200">Playlist processed successfully or rejected due to size.</response>
    /// <response code="400">Invalid request.</response>
    /// <response code="401">User not authenticated or token expired.</response>
    /// <response code="500">Failed to process playlist.</response>
    [HttpPost]
    [Route( "applemusic/process-playlist" )]
    public async Task<IActionResult> ProcessPlaylist( [FromBody] ProcessPlaylistRequest request ) {
        if (!ModelState.IsValid) {
            return BadRequest( ModelState );
        }

        ApplicationUser? user = await _userManager.GetUserAsync( User );
        if (user == null) {
            return Unauthorized( new { message = "User not authenticated" } );
        }

        if (string.IsNullOrEmpty( user.AppleMusicUserToken )) {
            return Unauthorized( new { message = "Apple Music token not found. Please authenticate first." } );
        }

        if (user.AppleMusicTokenExpiration.HasValue && user.AppleMusicTokenExpiration.Value < DateTime.UtcNow) {
            return Unauthorized( new { message = "Apple Music token expired. Please authenticate again." } );
        }

        try {
            HttpClient client = _httpClientFactory.CreateClient( "musickit-api" );
            
            // Add developer token (JWT) for API authentication
            if (_jwtHandler != null) {
                client.DefaultRequestHeaders.Authorization = _jwtHandler.NewAuthenticationHeader( );
            }
            
            // Add user token for accessing user's library
            client.DefaultRequestHeaders.Add( "Music-User-Token", user.AppleMusicUserToken );

            // Fetch playlist tracks
            string url = $"https://api.music.apple.com/v1/me/library/playlists/{request.PlaylistId}/tracks";
            HttpResponseMessage response = await client.GetAsync( url );

            if (!response.IsSuccessStatusCode) {
                _logger.LogError( "Failed to retrieve playlist tracks for user {UserId}: {StatusCode}", user.Id, response.StatusCode );
                return StatusCode( (int)response.StatusCode, new { message = "Failed to retrieve playlist tracks from Apple Music" } );
            }

            string content = await response.Content.ReadAsStringAsync( );
            using JsonDocument doc = JsonDocument.Parse( content );

            // Count tracks and extract URLs
            List<string> trackUrls = [];
            if (doc.RootElement.TryGetProperty( "data", out JsonElement dataElement )) {
                int trackCount = dataElement.GetArrayLength( );

                // Check if playlist has more than 100 tracks
                if (trackCount > 100) {
                    _logger.LogWarning( "Playlist has {TrackCount} tracks, exceeds limit of 100 for user {UserId}", trackCount, user.Id );
                    return Ok( new {
                        success = false,
                        tooLarge = true,
                        trackCount = trackCount,
                        message = $"This playlist has {trackCount} tracks. We currently only support playlists with 100 tracks or fewer."
                    } );
                }

                // Extract track URLs
                foreach (JsonElement track in dataElement.EnumerateArray( )) {
                    if (track.TryGetProperty( "attributes", out JsonElement attributesElement ) &&
                        attributesElement.TryGetProperty( "url", out JsonElement urlElement )) {
                        string? trackUrl = urlElement.GetString( );
                        if (!string.IsNullOrEmpty( trackUrl )) {
                            trackUrls.Add( trackUrl );
                        }
                    }
                }

                _logger.LogInformation( "Processing playlist with {TrackCount} tracks for user {UserId}", trackCount, user.Id );
            }

            if (trackUrls.Count == 0) {
                return Ok( new {
                    success = false,
                    message = "No tracks found in playlist"
                } );
            }

            // Return the URLs for the frontend to process
            return Ok( new {
                success = true,
                trackCount = trackUrls.Count,
                trackUrls = trackUrls
            } );

        } catch (Exception ex) {
            _logger.LogError( ex, "Error processing playlist for user {UserId}", user.Id );
            return StatusCode( 500, new { message = "An error occurred while processing the playlist" } );
        }
    }
}
