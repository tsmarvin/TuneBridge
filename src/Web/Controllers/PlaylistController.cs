using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using BridgeBeats.Domain.Contracts.DTOs;
using BridgeBeats.Domain.Contracts.Entities;
using BridgeBeats.Domain.Implementations.Utilities;
using BridgeBeats.Domain.Interfaces;
using BridgeBeats.Web.Models;

namespace BridgeBeats.Web.Controllers;

/// <summary>
/// Controller for managing and displaying playlists of music cards.
/// </summary>
[Route( "playlist" )]
public class PlaylistController( IPlaylistService? playlistService, IOpenGraphCardService? cardService, IMediaLinkService? mediaLinkService, ILogger<PlaylistController>? logger = null ) : Controller {

    private readonly IPlaylistService? _playlistService = playlistService;
    private readonly IOpenGraphCardService? _cardService = cardService;
    private readonly IMediaLinkService? _mediaLinkService = mediaLinkService;
    private readonly ILogger<PlaylistController>? _logger = logger;

    /// <summary>
    /// Creates a new playlist from a list of card IDs.
    /// </summary>
    /// <param name="request">Request containing card IDs and optional metadata.</param>
    /// <returns>JSON response with the playlist URL.</returns>
    [HttpPost( "create" )]
    public async Task<IActionResult> CreatePlaylist( [FromBody] CreatePlaylistRequest request ) {
        if (_playlistService?.IsEnabled != true) {
            return BadRequest( new { error = "Playlist service not available" } );
        }

        if (request.CardIds == null || request.CardIds.Count == 0) {
            return BadRequest( new { error = "At least one card ID is required" } );
        }

        if (request.CardIds.Count > 20) {
            return BadRequest( new { error = "Playlist cannot contain more than 20 cards" } );
        }

        if (request.CardRkeys == null || request.CardRkeys.Count != request.CardIds.Count) {
            return BadRequest( new { error = "Card rkeys must match card IDs" } );
        }

        try {
            // Get user ID if authenticated
            string? userId = User?.Identity?.IsAuthenticated == true
                                ? User.FindFirst( ClaimTypes.NameIdentifier )?.Value
                                : null;

            // Only allow title and description for authenticated users
            string? title = userId != null ? request.Title : null;
            string? description = userId != null ? request.Description : null;

            string playlistUrl = await _playlistService.CreatePlaylistAsync(
                                            request.CardIds,
                                            request.CardRkeys,
                                            title,
                                            description,
                                            userId
                                        );

            return Ok( new { playlistUrl } );
        } catch (ArgumentException ex) {
            return BadRequest( new { error = ex.Message } );
        } catch (Exception ex) {
            _logger?.LogError( ex, "Error creating playlist" );
            return StatusCode( 500, new { error = "Failed to create playlist" } );
        }
    }

    /// <summary>
    /// Displays a playlist card with all included music items.
    /// </summary>
    /// <param name="id">The unique identifier of the playlist.</param>
    /// <returns>An HTML page displaying the playlist.</returns>
    [HttpGet( "{id}" )]
    public async Task<IActionResult> Playlist( string id ) {
        if (_playlistService?.IsEnabled != true) {
            return NotFound( "Playlist service not available" );
        }

        PlaylistEntry? playlist = await _playlistService.GetPlaylistAsync( id );

        if (playlist == null) {
            return NotFound( "Playlist not found or expired" );
        }

        // Parse card IDs and rkeys
        string[] cardIds = playlist.CardIds.Split( ',', StringSplitOptions.RemoveEmptyEntries );
        string[] cardRkeys = playlist.CardRkeys.Split( ',', StringSplitOptions.RemoveEmptyEntries );
        List<PlaylistItemViewModel> items = [];

        // Ensure we have matching counts
        if (cardIds.Length != cardRkeys.Length) {
            _logger?.LogWarning(
                "Playlist {PlaylistId} has mismatched card IDs ({CardIdCount}) and rkeys ({RkeyCount})",
                id.SanitizeForLogging( ), cardIds.Length, cardRkeys.Length
            );
        }

        for (int i = 0; i < Math.Min( cardIds.Length, cardRkeys.Length ); i++) {
            string cardId = cardIds[i];
            string rkey = cardRkeys[i];

            // Try to get existing result from card service
            MediaLinkResult? result = _cardService?.GetResult( cardId );

            // If not found in cache, try to reconstruct from rkey
            if (result == null && _cardService != null && (rkey.StartsWith( "track:" ) || rkey.StartsWith( "album:" ))) {
                // Extract the external ID and type
                string[] parts = rkey.Split( ':', 2 );
                if (parts.Length == 2) {
                    bool isAlbum = parts[0] == "album";
                    string externalId = parts[1];

                    // Lookup by ISRC/UPC using media link service
                    result = isAlbum
                        ? await _mediaLinkService?.GetInfoByUPCAsync( externalId )!
                        : await _mediaLinkService?.GetInfoByISRCAsync( externalId )!;

                    // If we successfully recreated the result, store it back in the card service
                    if (result != null) {
                        _ = _cardService.StoreResult( result );
                        _logger?.LogInformation( "Regenerated card {CardId} from rkey {Rkey} for playlist {PlaylistId}",
                            cardId.SanitizeForLogging( ), rkey.SanitizeForLogging( ), id.SanitizeForLogging( ) );
                    }
                }
            }

            if (result == null) {
                _logger?.LogWarning(
                    "Unable to load or regenerate card {CardId} (rkey: {Rkey}) for playlist {PlaylistId}",
                    cardId.SanitizeForLogging( ), rkey.SanitizeForLogging( ), id.SanitizeForLogging( )
                );
                continue;
            }

            // Find primary result for display
            MusicLookupResultDto? primaryResult = result.Results.Values.FirstOrDefault( r => r.IsPrimary )
                                                   ?? result.Results.Values.FirstOrDefault( );

            if (primaryResult != null) {
                items.Add( new PlaylistItemViewModel {
                    CardId = cardId,
                    CardUrl = $"https://{_cardService?.BaseUrl}/card/{cardId}",
                    Result = result,
                    Title = primaryResult.Title ?? "Unknown Title",
                    Artist = primaryResult.Artist ?? "Unknown Artist",
                    ArtUrl = primaryResult.ArtUrl,
                    IsAlbum = primaryResult.IsAlbum == true
                } );
            }
        }

        PlaylistViewModel viewModel = new( ) {
            PlaylistId = playlist.PlaylistId,
            Title = playlist.Title ?? "BridgeBeats",
            Description = playlist.Description,
            Items = items,
            BaseUrl = _playlistService.BaseUrl
        };

        return View( viewModel );
    }

    /// <summary>
    /// Displays an embeddable compact playlist view.
    /// This endpoint is designed for iframe embedding.
    /// </summary>
    /// <param name="id">The unique identifier of the playlist.</param>
    /// <returns>A minimal HTML page with the playlist suitable for iframe embedding.</returns>
    [HttpGet( "{id}/embed" )]
    public async Task<IActionResult> Embed( string id ) {
        if (_playlistService?.IsEnabled != true) {
            return NotFound( "Playlist service not available" );
        }

        PlaylistEntry? playlist = await _playlistService.GetPlaylistAsync( id );

        if (playlist == null) {
            return NotFound( "Playlist not found or expired" );
        }

        // Parse card IDs and rkeys
        string[] cardIds = playlist.CardIds.Split( ',', StringSplitOptions.RemoveEmptyEntries );
        string[] cardRkeys = playlist.CardRkeys.Split( ',', StringSplitOptions.RemoveEmptyEntries );
        List<PlaylistItemViewModel> items = [];

        for (int i = 0; i < Math.Min( cardIds.Length, cardRkeys.Length ); i++) {
            string cardId = cardIds[i];
            string rkey = cardRkeys[i];

            MediaLinkResult? result = _cardService?.GetResult( cardId );

            // Try to regenerate if not in cache
            if (result == null && _cardService != null && (rkey.StartsWith( "track:" ) || rkey.StartsWith( "album:" ))) {
                string[] parts = rkey.Split( ':', 2 );
                if (parts.Length == 2) {
                    bool isAlbum = parts[0] == "album";
                    string externalId = parts[1];

                    result = isAlbum
                        ? await _mediaLinkService?.GetInfoByUPCAsync( externalId )!
                        : await _mediaLinkService?.GetInfoByISRCAsync( externalId )!;

                    if (result != null) {
                        _ = _cardService.StoreResult( result );
                        _logger?.LogInformation(
                            "Regenerated card {CardId} for playlist embed {PlaylistId}",
                            cardId.SanitizeForLogging( ), id.SanitizeForLogging( )
                        );
                    }
                }
            }

            if (result == null) {
                _logger?.LogWarning(
                    "Unable to load or regenerate card {CardId} for playlist embed {PlaylistId}",
                    cardId.SanitizeForLogging( ), id.SanitizeForLogging( )
                );
                continue;
            }

            MusicLookupResultDto? primaryResult = result.Results.Values.FirstOrDefault( r => r.IsPrimary )
                                                   ?? result.Results.Values.FirstOrDefault( );

            if (primaryResult != null) {
                items.Add( new PlaylistItemViewModel {
                    CardId = cardId,
                    CardUrl = $"https://{_cardService?.BaseUrl}/card/{cardId}",
                    Result = result,
                    Title = primaryResult.Title ?? "Unknown Title",
                    Artist = primaryResult.Artist ?? "Unknown Artist",
                    ArtUrl = primaryResult.ArtUrl,
                    IsAlbum = primaryResult.IsAlbum == true
                } );
            }
        }

        PlaylistViewModel viewModel = new( ) {
            PlaylistId = playlist.PlaylistId,
            Title = playlist.Title ?? "BridgeBeats Playlist",
            Description = playlist.Description,
            Items = items,
            BaseUrl = _playlistService.BaseUrl
        };

        return View( viewModel );
    }

    /// <summary>
    /// Request model for creating a playlist.
    /// </summary>
    public record CreatePlaylistRequest {
        /// <summary>
        /// List of card IDs to include in the playlist (max 20).
        /// </summary>
        public List<string> CardIds { get; init; } = [];

        /// <summary>
        /// List of original rkey values corresponding to CardIds (max 20).
        /// </summary>
        public List<string> CardRkeys { get; init; } = [];

        /// <summary>
        /// Optional title for the playlist.
        /// </summary>
        public string? Title { get; init; }

        /// <summary>
        /// Optional description for the playlist.
        /// </summary>
        public string? Description { get; init; }
    }
}
