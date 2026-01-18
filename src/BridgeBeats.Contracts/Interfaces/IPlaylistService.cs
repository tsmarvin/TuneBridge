using BridgeBeats.Contracts.DTOs;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Service for managing playlists of music cards.
/// </summary>
public interface IPlaylistService {

    /// <summary>
    /// An indicator whether the playlist service is enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// The base URL for the application (e.g., https://bridgebeats.link).
    /// </summary>
    string BaseUrl { get; }

    /// <summary>
    /// Creates a playlist from a list of card IDs (rkeys).
    /// </summary>
    /// <param name="cardIds">List of card IDs to include in the playlist (max 20).</param>
    /// <param name="cardRkeys">List of original rkey values corresponding to cardIds (max 20).</param>
    /// <param name="title">Optional title for the playlist.</param>
    /// <param name="description">Optional description for the playlist.</param>
    /// <param name="userId">Optional user ID if the playlist is created by a logged-in user.</param>
    /// <returns>A URL to the created playlist card.</returns>
    /// <exception cref="ArgumentException">Thrown when cardIds is null, empty, or contains more than 20 items.</exception>
    Task<string> CreatePlaylistAsync( List<string> cardIds, List<string> cardRkeys, string? title = null, string? description = null, string? userId = null );

    /// <summary>
    /// Retrieves a playlist by its unique identifier.
    /// </summary>
    /// <param name="playlistId">The unique identifier of the playlist.</param>
    /// <returns>The playlist entry DTO, or null if not found or expired.</returns>
    Task<PlaylistEntryDto?> GetPlaylistAsync( string playlistId );

    /// <summary>
    /// Retrieves all playlists for a specific user.
    /// </summary>
    /// <param name="userId">The user ID to retrieve playlists for.</param>
    /// <returns>List of playlists owned by the user.</returns>
    Task<List<PlaylistEntryDto>> GetUserPlaylistsAsync( string userId );

    /// <summary>
    /// Deletes a playlist by its unique identifier.
    /// Only the owner can delete their playlists.
    /// </summary>
    /// <param name="playlistId">The unique identifier of the playlist.</param>
    /// <param name="userId">The user ID of the requestor.</param>
    /// <returns>True if deleted successfully, false if not found or unauthorized.</returns>
    Task<bool> DeletePlaylistAsync( string playlistId, string userId );

    /// <summary>
    /// Deletes expired playlists from the store.
    /// </summary>
    Task CleanExpiredPlaylistsAsync( );

    /// <summary>
    /// Exports all playlist data for a user (for GDPR compliance).
    /// </summary>
    /// <param name="userId">The user ID to export data for.</param>
    /// <returns>List of playlists with full details.</returns>
    Task<List<PlaylistEntryDto>> ExportUserDataAsync( string userId );
}
