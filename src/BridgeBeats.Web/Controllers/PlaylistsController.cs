using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Infrastructure.Utilities;
using BridgeBeats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BridgeBeats.Web.Controllers;

/// <summary>
/// Controller for managing user playlists.
/// Provides views for authenticated users to view and manage their playlists.
/// </summary>
[Authorize]
[Route( "playlists" )]
public class PlaylistsController( IPlaylistService? playlistService, ILogger<PlaylistsController>? logger = null ) : Controller {

    private readonly IPlaylistService? _playlistService = playlistService;
    private readonly ILogger<PlaylistsController>? _logger = logger;

    /// <summary>
    /// Displays the user's playlists.
    /// </summary>
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
                    PlaylistUrl = $"https://{_playlistService.BaseUrl}/playlist/{p.PlaylistId}"
                } )]
            };


            return View( "Index", viewModel );
        } catch (Exception ex) {
            _logger?.LogError( ex, "Error loading user playlists for user {UserId}", userId );
            return View( "Index", new PlaylistsViewModel {
                ErrorMessage = "Failed to load playlists. Please try again later."
            } );
        }
    }

    /// <summary>
    /// Deletes a playlist owned by the current user.
    /// </summary>
    /// <param name="id">The playlist ID to delete.</param>
    [HttpPost( "{id}/delete" )]
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
            _logger?.LogError( ex, "Error deleting playlist {PlaylistId} for user {UserId}", id.SanitizeForLogging( ), userId );
            return StatusCode( 500, new { error = "Failed to delete playlist" } );
        }
    }

    /// <summary>
    /// Displays the Apple Music integration page as a sub-page of playlists.
    /// </summary>
    [HttpGet( "applemusic" )]
    public IActionResult AppleMusic( ) {
        return View( "AppleMusic" );
    }
}
