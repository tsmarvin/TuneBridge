using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Infrastructure.Utilities;
using BridgeBeats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BridgeBeats.Web.Controllers;

/// <summary>
/// Manages the authenticated user's own playlists, rooted at <c>playlists</c>. Lists the user's playlists,
/// deletes a playlist the user owns, and serves the Apple Music playlists page. Requires an authenticated
/// session.
/// </summary>
/// <param name="playlistService">Optional playlist service used to list, delete, and supply the public domain for the user's playlists; when disabled, actions report unavailability.</param>
/// <param name="logger">Optional logger for load and delete errors.</param>
[Authorize]
[Route( "playlists" )]
public partial class PlaylistsController( IPlaylistService? playlistService, ILogger<PlaylistsController>? logger = null ) : Controller {

    /// <summary>Optional playlist service used to list, delete, and supply the public domain for the user's playlists.</summary>
    private readonly IPlaylistService? _playlistService = playlistService;
    /// <summary>Optional logger for load and delete errors.</summary>
    private readonly ILogger<PlaylistsController>? _logger = logger;

    /// <summary>
    /// Lists the authenticated user's playlists, projecting each into a summary with its item count and URL.
    /// </summary>
    /// <returns>
    /// HTTP GET <c>playlists</c>. The <c>Index</c> view with the user's playlists on success, or carrying an
    /// error message when the service is unavailable or loading fails; <c>401 Unauthorized</c> when the user
    /// id cannot be resolved.
    /// </returns>
    [HttpGet]
    public async Task<IActionResult> Index( ) {
        if (_playlistService?.IsEnabled != true) {
            return View( "Index", new PlaylistsViewModel {
                ErrorMessage = "Playlist service is not available"
            } );
        }

        string? userId = User.FindFirst( System.Security.Claims.ClaimTypes.NameIdentifier )?.Value;
        if (string.IsNullOrEmpty( userId )) {
            return Unauthorized( );
        }

        try {
            List<PlaylistEntryDto> playlists = await _playlistService.GetUserPlaylistsAsync( userId );

            PlaylistsViewModel viewModel = new( ) {
                Playlists = [.. playlists.Select( p => new PlaylistsViewModel.PlaylistSummary {
                    PlaylistId = p.PlaylistId,
                    Title = p.Title ?? "Untitled Playlist",
                    Description = p.Description,
                    ItemCount = p.CardIds.Split( ',', StringSplitOptions.RemoveEmptyEntries ).Length,
                    CreatedAt = p.CreatedAt,
                    PlaylistUrl = $"https://{_playlistService.Domain}/playlist/{p.PlaylistId}"
                } )]
            };

            return View( "Index", viewModel );
        } catch (Exception ex) {
            LogLoadError( ex, userId );
            return View( "Index", new PlaylistsViewModel {
                ErrorMessage = "Failed to load playlists. Please try again later."
            } );
        }
    }

    /// <summary>
    /// Deletes a playlist owned by the authenticated user.
    /// </summary>
    /// <param name="id">The playlist id from the route.</param>
    /// <returns>
    /// HTTP POST <c>playlists/{id}/delete</c>. <c>200 OK</c> on success; <c>404 Not Found</c> when the
    /// playlist does not exist or is not owned by the user; <c>400 Bad Request</c> when the service is
    /// unavailable; <c>401 Unauthorized</c> when the user id cannot be resolved; <c>500</c> on an unexpected
    /// error. Requires a valid anti-forgery token.
    /// </returns>
    [HttpPost( "{id}/delete" )]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete( string id ) {
        if (_playlistService?.IsEnabled != true) {
            return BadRequest( new { error = "Playlist service not available" } );
        }

        string? userId = User.FindFirst( System.Security.Claims.ClaimTypes.NameIdentifier )?.Value;
        if (string.IsNullOrEmpty( userId )) {
            return Unauthorized( );
        }

        try {
            bool deleted = await _playlistService.DeletePlaylistAsync( id, userId );
            return deleted
                ? Ok( new { message = "Playlist deleted successfully" } )
                : NotFound( new { error = "Playlist not found or you don't have permission to delete it" } );
        } catch (Exception ex) {
            LogDeleteError( ex, id.SanitizeForLogging( ), userId );
            return StatusCode( 500, new { error = "Failed to delete playlist" } );
        }
    }

    /// <summary>
    /// Renders the Apple Music playlists page.
    /// </summary>
    /// <returns>The <c>AppleMusic</c> view (HTTP GET <c>playlists/applemusic</c>).</returns>
    [HttpGet( "applemusic" )]
    public IActionResult AppleMusic( ) {
        return View( "AppleMusic" );
    }
}
