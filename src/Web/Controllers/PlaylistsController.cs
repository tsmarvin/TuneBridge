using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TuneBridge.Domain.Contracts.Entities;
using TuneBridge.Domain.Interfaces;
using TuneBridge.Web.Models;

namespace TuneBridge.Web.Controllers;

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
    /// Sanitizes a string for safe logging by removing or replacing characters that could cause log forging.
    /// </summary>
    /// <param name="input">The input string to sanitize.</param>
    /// <param name="maxLength">Maximum length to truncate to (default 100).</param>
    /// <returns>Sanitized string safe for logging.</returns>
    private static string SanitizeForLog( string? input, int maxLength = 100 ) {
        if (string.IsNullOrEmpty( input )) {
            return string.Empty;
        }

        // Remove newlines, carriage returns, and other control characters
        string sanitized = input.Replace( "\r", "" ).Replace( "\n", "" ).Replace( "\t", " " );
        
        // Truncate if too long
        if (sanitized.Length > maxLength) {
            sanitized = sanitized[..maxLength];
        }

        return sanitized;
    }

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
            List<PlaylistEntry> playlists = await _playlistService.GetUserPlaylistsAsync( userId );

            PlaylistsViewModel viewModel = new( ) {
                Playlists = playlists.Select( p => new PlaylistsViewModel.PlaylistSummary {
                    PlaylistId = p.PlaylistId,
                    Title = p.Title ?? "Untitled Playlist",
                    Description = p.Description,
                    ItemCount = p.CardIds.Split( ',', StringSplitOptions.RemoveEmptyEntries ).Length,
                    CreatedAt = p.CreatedAt,
                    PlaylistUrl = $"https://{_playlistService.BaseUrl}/playlist/{p.PlaylistId}"
                } ).ToList( )
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
            _logger?.LogError( ex, "Error deleting playlist {PlaylistId} for user {UserId}", 
                SanitizeForLog( id, 50 ), userId );
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
