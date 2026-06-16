using BridgeBeats.Contracts.DTOs;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Manages user playlists built from lookup cards: create, read, list, delete (with ownership
/// checks), expiry cleanup, and user-data export.
/// </summary>
/// <remarks>
/// Implemented in <c>BridgeBeats.Core</c> by <c>PlaylistService</c>
/// (<c>Domain/Services/Cards/PlaylistService.cs</c>), backed by EF Core; expiry cleanup also
/// runs from <c>PlaylistCleanupService</c>. <see cref="IsEnabled"/> is the feature flag and
/// <see cref="Domain"/> the public base domain used to build playlist links. Playlists pair a
/// list of card ids with their matching record keys (rkeys).
/// </remarks>
public interface IPlaylistService {

    /// <summary>
    /// Whether the playlist feature is enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// The public base domain used to build playlist links (for example <c>https://bridgebeats.link</c>).
    /// </summary>
    string Domain { get; }

    /// <summary>
    /// Creates a playlist from a set of cards and returns a URL to the created playlist card.
    /// </summary>
    /// <param name="cardIds">The card ids that make up the playlist (at most 20).</param>
    /// <param name="cardRkeys">The record keys (rkeys) matching <paramref name="cardIds"/> positionally (at most 20).</param>
    /// <param name="title">An optional playlist title.</param>
    /// <param name="description">An optional playlist description.</param>
    /// <param name="userId">The owning user's id, or <see langword="null"/> for an unowned playlist.</param>
    /// <returns>A task whose result is the URL of the created playlist card.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="cardIds"/> is null, empty, or contains more than 20 items.</exception>
    Task<string> CreatePlaylistAsync( List<string> cardIds, List<string> cardRkeys, string? title = null, string? description = null, string? userId = null );

    /// <summary>
    /// Reads a playlist projection by its id.
    /// </summary>
    /// <param name="playlistId">The id of the playlist to read.</param>
    /// <returns>
    /// A task whose result is the <see cref="PlaylistEntryDto"/>, or <see langword="null"/> when
    /// no playlist exists with the given id or it has expired.
    /// </returns>
    Task<PlaylistEntryDto?> GetPlaylistAsync( string playlistId );

    /// <summary>
    /// Lists all playlists owned by a user.
    /// </summary>
    /// <param name="userId">The owning user's id.</param>
    /// <returns>
    /// A task whose result is the user's playlists; an empty list when the user owns none.
    /// </returns>
    Task<List<PlaylistEntryDto>> GetUserPlaylistsAsync( string userId );

    /// <summary>
    /// Deletes a playlist, succeeding only when the given user owns it.
    /// </summary>
    /// <param name="playlistId">The id of the playlist to delete.</param>
    /// <param name="userId">The id of the user requesting deletion; must own the playlist.</param>
    /// <returns>
    /// A task whose result is <see langword="true"/> when the playlist was deleted, or
    /// <see langword="false"/> when it does not exist or is not owned by the user.
    /// </returns>
    Task<bool> DeletePlaylistAsync( string playlistId, string userId );

    /// <summary>
    /// Removes playlists whose expiry has passed.
    /// </summary>
    /// <returns>A task that completes when expired playlists have been cleaned up.</returns>
    Task CleanExpiredPlaylistsAsync( );

    /// <summary>
    /// Returns every playlist owned by a user, with full details, for a GDPR user-data export.
    /// </summary>
    /// <param name="userId">The owning user's id.</param>
    /// <returns>A task whose result is the user's playlists for export.</returns>
    Task<List<PlaylistEntryDto>> ExportUserDataAsync( string userId );
}
