using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Implementations.Auth;
using TuneBridge.Domain.Implementations.Utilities;
using TuneBridge.Domain.Interfaces;
using TuneBridge.Domain.Models;
using TuneBridge.Domain.Types.Enums;
using TuneBridge.Web.Models;

namespace TuneBridge.Web.Controllers;

/// <summary>
/// Controller for Apple Music user authentication and playlist management.
/// Provides endpoints for storing user tokens and listing playlists.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="AppleMusicController"/> class.
/// </remarks>
/// <param name="userManager">User manager for ASP.NET Identity.</param>
/// <param name="httpClientFactory">HTTP client factory for making API requests.</param>
/// <param name="logger">Logger for diagnostic information.</param>
/// <param name="mediaLinkService">The media link service used to lookup other track references.</param>
/// <param name="viewEngine">View engine for rendering partial views to strings.</param>
/// <param name="jwtHandler">JWT handler for generating Apple Music developer tokens.</param>
/// <param name="cardService">Optional OpenGraph card service.</param>
/// <param name="cacheRepository">Optional cache repository for ATProto URIs.</param>
[Authorize]
public class AppleMusicController(
    UserManager<ApplicationUser> userManager,
    IHttpClientFactory httpClientFactory,
    ILogger<AppleMusicController> logger,
    IMediaLinkService mediaLinkService,
    ICompositeViewEngine viewEngine,
    AppleJwtHandler? jwtHandler = null,
    IOpenGraphCardService? cardService = null,
    IMediaLinkCacheRepository? cacheRepository = null
) : Controller {

    /// <summary>
    /// Displays the Apple Music authentication page.
    /// </summary>
    [HttpGet]
    [Route( "applemusic" )]
    public IActionResult Index( ) => View( );

    /// <summary>
    /// Gets a developer token for MusicKit JS authentication.
    /// </summary>
    /// <returns>Developer token.</returns>
    /// <response code="200">Developer token retrieved successfully.</response>
    /// <response code="503">Apple Music service not available.</response>
    [HttpGet]
    [Route( "applemusic/developer-token" )]
    public IActionResult GetDeveloperToken( ) {
        if (jwtHandler == null) {
            return StatusCode( 503, new { message = "Apple Music service is not configured" } );
        }

        string token = jwtHandler.NewAuthenticationHeader( ).Parameter ?? string.Empty;
        return Ok( new { token } );
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

        ApplicationUser? user = await userManager.GetUserAsync( User );
        if (user == null) {
            return Unauthorized( );
        }

        user.AppleMusicUserToken = request.UserToken;
        user.AppleMusicTokenExpiration = DateTime.UtcNow.AddMilliseconds( request.ExpiresInMs );

        Microsoft.AspNetCore.Identity.IdentityResult result = await userManager.UpdateAsync( user );

        if (!result.Succeeded) {
            logger.LogError( "Failed to store Apple Music token for user {UserId}", user.Id );
            return BadRequest( new { message = "Failed to store token" } );
        }

        logger.LogInformation( "Apple Music token stored for user {UserId}", user.Id );

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
        ApplicationUser? user = await userManager.GetUserAsync( User );
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
            HttpClient client = httpClientFactory.CreateClient( "musickit-api" );

            // Add developer token (JWT) for API authentication
            if (jwtHandler != null) {
                client.DefaultRequestHeaders.Authorization = jwtHandler.NewAuthenticationHeader( );
            }

            // Add user token for accessing user's library
            client.DefaultRequestHeaders.Add( "Music-User-Token", user.AppleMusicUserToken );

            HttpResponseMessage response = await client.GetAsync( "https://api.music.apple.com/v1/me/library/playlists" );

            if (!response.IsSuccessStatusCode) {
                logger.LogError( "Failed to retrieve playlists for user {UserId}: {StatusCode}", user.Id, response.StatusCode );
                return StatusCode( (int)response.StatusCode, new { message = "Failed to retrieve playlists from Apple Music" } );
            }

            string content = await response.Content.ReadAsStringAsync( );
            using JsonDocument doc = JsonDocument.Parse( content );

            List<object> playlists = [];
            if (doc.RootElement.TryGetProperty( "data", out JsonElement dataElement )) {
                playlists.AddRange(
                    dataElement.EnumerateArray( )
                        .Where( playlist =>
                            playlist.TryGetProperty( "id", out _ ) &&
                            playlist.TryGetProperty( "attributes", out JsonElement attributesElement ) &&
                            attributesElement.TryGetProperty( "name", out _ )
                        )
                        .Select( playlist => new {
                            id = playlist.GetProperty( "id" ).GetString( ),
                            name = playlist.GetProperty( "attributes" ).GetProperty( "name" ).GetString( )
                        } )
                );
            }

            return Ok( new { playlists } );

        } catch (Exception ex) {
            logger.LogError( ex, "Error retrieving playlists for user {UserId}", user.Id );
            return StatusCode( 500, new { message = "An error occurred while retrieving playlists" } );
        }
    }

    /// <summary>
    /// Retrieves song information from Apple Music by song IDs.
    /// </summary>
    /// <param name="songIds">Array of Apple Music song IDs to retrieve.</param>
    /// <returns>List of MediaLinkResult objects for the requested songs.</returns>
    [HttpPost]
    [Route( "applemusic/getsongsbyid" )]
    public async Task<IActionResult> GetSongsByID( [FromBody] string[] songIds ) {
        ApplicationUser? user = await userManager.GetUserAsync( User );
        if (user == null) {
            return Unauthorized( );
        }
        if (jwtHandler == null) {
            return StatusCode( 503, new { message = "Apple Music service is not configured" } );
        }
        List<MediaLinkResult> results = [];

        foreach (string songId in songIds) {
            MediaLinkResult? result = await mediaLinkService.GetInfoByProviderIdAsync( songId, SupportedProviders.AppleMusic, false );
            if (result != null) {
                results.Add( result );
            }
        }

        return Ok( new { results } );
    }

    /// <summary>
    /// Attempts to retrieve the ATProto URI for a MediaLinkResult from the cache.
    /// Tries multiple lookup strategies: input link, external ID (ISRC/UPC), and metadata.
    /// </summary>
    /// <param name="result">The MediaLinkResult to find in cache.</param>
    /// <returns>The ATProto URI if found in cache, otherwise null.</returns>
    private async Task<string?> GetATProtoUriFromCache( MediaLinkResult result ) {
        try {
            return await ATProtoUriHelper.GetATProtoUriFromCacheAsync( result, cacheRepository );
        } catch (InvalidOperationException ex) {
            logger.LogWarning( ex, "Failed to retrieve ATProto URI from cache due to invalid operation, continuing without it" );
        } catch (ArgumentException ex) {
            logger.LogWarning( ex, "Failed to retrieve ATProto URI from cache due to argument error, continuing without it" );
        } catch (Exception ex) {
            logger.LogWarning( ex, "Failed to retrieve ATProto URI from cache, continuing without it" );
        }

        return null;
    }

    /// <summary>
    /// Gets the current authentication status for Apple Music.
    /// </summary>
    /// <returns>Authentication status including whether token exists and is valid.</returns>
    [HttpGet]
    [Route( "applemusic/status" )]
    public async Task<IActionResult> GetStatus( ) {
        ApplicationUser? user = await userManager.GetUserAsync( User );
        if (user == null) {
            return Unauthorized( );
        }

        bool hasToken = !string.IsNullOrEmpty( user.AppleMusicUserToken );
        bool isExpired = user.AppleMusicTokenExpiration.HasValue && user.AppleMusicTokenExpiration.Value < DateTime.UtcNow;

        return Ok( new {
            hasToken,
            isExpired,
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

        ApplicationUser? user = await userManager.GetUserAsync( User );
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
            HttpClient client = httpClientFactory.CreateClient( "musickit-api" );

            // Add developer token (JWT) for API authentication
            if (jwtHandler != null) {
                client.DefaultRequestHeaders.Authorization = jwtHandler.NewAuthenticationHeader( );
            }

            // Add user token for accessing user's library
            client.DefaultRequestHeaders.Add( "Music-User-Token", user.AppleMusicUserToken );

            // Fetch all playlist tracks with pagination
            string playlistId = request.PlaylistId.SanitizeForLogging( );

            List<string> trackIds = [];
            string? nextUrl = $"https://api.music.apple.com/v1/me/library/playlists/{playlistId}/tracks";
            int totalTracks = 0;
            bool overonekay = false;
            while (!string.IsNullOrEmpty( nextUrl )) {
                HttpResponseMessage response = await client.GetAsync( nextUrl );

                if (!response.IsSuccessStatusCode) {
                    logger.LogError( "Failed to retrieve playlist tracks for user {UserId}: {StatusCode}", user.Id, response.StatusCode );
                    return StatusCode( (int)response.StatusCode, new { message = "Failed to retrieve playlist tracks from Apple Music" } );
                }

                string content = await response.Content.ReadAsStringAsync( );
                using JsonDocument doc = JsonDocument.Parse( content );

                // Extract track IDs
                if (doc.RootElement.TryGetProperty( "data", out JsonElement dataElement )) {
                    trackIds.AddRange(
                        dataElement.EnumerateArray( )
                            .Select( trackElement => {
                                return (
                                    trackElement.TryGetProperty( "attributes", out JsonElement attributesElement ) &&
                                    attributesElement.TryGetProperty( "playParams", out JsonElement playParamsElement ) &&
                                    playParamsElement.TryGetProperty( "catalogId", out JsonElement idElement )
                                )
                                ? idElement.GetString( )
                                : null;
                            } )
                            .Where( id => !string.IsNullOrEmpty( id ) )
                            .Cast<string>( )
                    );
                }

                // Check for pagination: look for a "next" link in the response
                if (doc.RootElement.TryGetProperty( "next", out JsonElement nextElement )) {
                    string? nextValue = nextElement.GetString( );
                    nextUrl = (!string.IsNullOrEmpty( nextValue ) && !nextValue.StartsWith( "http", StringComparison.OrdinalIgnoreCase ))
                        ? "https://api.music.apple.com" + nextValue
                        : nextValue;
                } else {
                    nextUrl = null; // No more pages
                }

                // Apple Music API limit: 100 tracks per request, cap processing at 1000 tracks
                if (doc.RootElement.TryGetProperty( "data", out JsonElement trackDataElement )) {
                    totalTracks += trackDataElement.GetArrayLength( );
                }
                if (totalTracks >= 1000) {
                    overonekay = true;
                    logger.LogWarning( "Playlist {PlaylistId} exceeds 1000 tracks, processing limited to first 1000 tracks", playlistId );
                    break;
                }
            }

            return Ok( new {
                success = true,
                message = overonekay ? $"First {totalTracks} items processed successfully" : "Playlist processed successfully",
                trackCount = trackIds.Count,
                trackIds,
                tooLarge = trackIds.Count > 100
            } );

        } catch (Exception ex) {
            logger.LogError( ex, "Error processing playlist for user {UserId}", user.Id );
            return StatusCode( 500, new { message = "An error occurred while processing the playlist" } );
        }
    }

    /// <summary>
    /// Streams the results of a playlist processing as HTML, rendering each result with OpenGraph data.
    /// Designed for use with large playlists where results are streamed to the client as they are processed.
    /// </summary>
    /// <param name="songIds">Array of Apple Music song IDs for the playlist.</param>
    /// <response code="200">Results streamed successfully.</response>
    /// <response code="400">Invalid request.</response>
    /// <response code="401">User not authenticated or token expired.</response>
    /// <response code="500">Failed to process request.</response>
    [HttpPost]
    [Route( "applemusic/playlist-results-stream" )]
    public async Task PlaylistResultsStream( [FromBody] string[] songIds ) {
        ApplicationUser? user = await userManager.GetUserAsync( User );
        if (user == null) {
            Response.StatusCode = 401;
            await Response.WriteAsync( "<div class=\"alert alert-danger\">Authentication required</div>" );
            return;
        }
        if (jwtHandler == null) {
            Response.StatusCode = 503;
            await Response.WriteAsync( "<div class=\"alert alert-danger\">Apple Music service is not configured</div>" );
            return;
        }
        Response.ContentType = "text/html; charset=utf-8";
        Response.Headers.Append( "Cache-Control", "no-cache" );
        Response.Headers.Append( "X-Accel-Buffering", "no" );
        int processedCount = 0;
        int errorCount = 0;
        foreach (string songId in songIds) {
            try {
                MediaLinkResult? lookupResult = await mediaLinkService.GetInfoByProviderIdAsync( songId, SupportedProviders.AppleMusic, false );
                if (lookupResult == null || lookupResult.Results.Count == 0) {
                    errorCount++;
                    continue;
                }

                MusicLookupResultDto? primaryResult = null;
                SupportedProviders primaryProvider = SupportedProviders.AppleMusic;
                foreach ((SupportedProviders provider, MusicLookupResultDto dto) in lookupResult.Results) {
                    if (dto.IsPrimary) {
                        primaryResult = dto;
                        primaryProvider = provider;
                        break;
                    }
                    if (primaryResult == null) {
                        primaryResult = dto;
                        primaryProvider = provider;
                    }
                }

                if (primaryResult == null) {
                    errorCount++;
                    continue;
                }

                string? cardUrl = null;
                if (cardService?.IsEnabled == true) {
                    cardUrl = cardService.StoreResult( lookupResult );
                }

                string? atProtoUri = await GetATProtoUriFromCache( lookupResult );

                MusicLookupViewModel.MusicLookupResultItem item = new( )
                {
                    CardUrl = cardUrl,
                    ATProtoUri = atProtoUri,
                    Result = lookupResult,
                    PrimaryProvider = primaryProvider,
                    PrimaryResult = primaryResult
                };

                string html = await RenderViewToStringAsync( "_LookupResultCard", item );
                await Response.WriteAsync( html );
                await Response.Body.FlushAsync( );
                processedCount++;
            } catch (Exception ex) {
                logger.LogError( ex, "Error processing song {SongId}", songId.SanitizeForLogging( ) );
                errorCount++;
            }
        }
        if (processedCount == 0 && errorCount > 0) {
            await Response.WriteAsync( "<div class=\"alert alert-warning\" data-stream-complete=\"true\" data-processed=\"0\" data-errors=\"" + errorCount + "\">No results found for the tracks in this playlist</div>" );
        } else if (errorCount > 0) {
            await Response.WriteAsync( $"<div class=\"d-none\" data-stream-complete=\"true\" data-processed=\"{processedCount}\" data-errors=\"{errorCount}\"></div>" );
        } else {
            await Response.WriteAsync( $"<div class=\"d-none\" data-stream-complete=\"true\" data-processed=\"{processedCount}\" data-errors=\"0\"></div>" );
        }
    }

    /// <summary>Returns Apple Music integration partial view content without layout.</summary>
    [HttpGet]
    [Route( "applemusic/content" )]
    public IActionResult ContentPartial( ) => PartialView( "_AppleMusicContent" );

    private async Task<string> RenderViewToStringAsync( string viewName, object model ) {
        ViewData.Model = model;
        using StringWriter sw = new();
        ViewEngineResult viewResult = viewEngine.FindView( ControllerContext, viewName, false );

        if (!viewResult.Success) {
            throw new InvalidOperationException( $"View '{viewName}' not found" );
        }

        Microsoft.AspNetCore.Mvc.Rendering.ViewContext viewContext = new(
            ControllerContext,
            viewResult.View,
            ViewData,
            TempData,
            sw,
            new HtmlHelperOptions()
        );

        await viewResult.View.RenderAsync( viewContext );
        return sw.ToString( );
    }
}
