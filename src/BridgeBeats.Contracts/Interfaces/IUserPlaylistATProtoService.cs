using BridgeBeats.Contracts.DTOs;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Service for managing user playlists on their ATProto PDS using OAuth authentication.
/// This service writes to individual user PDSs (not the server's PDS), requiring
/// user-specific OAuth tokens obtained through ATProto OAuth login.
/// </summary>
public interface IUserPlaylistATProtoService {

    /// <summary>
    /// Creates a new playlist on the user's ATProto PDS.
    /// Uses a TID (Timestamp Identifier) for the record key.
    /// </summary>
    /// <param name="userDid">The DID of the user whose PDS will store the playlist.</param>
    /// <param name="playlist">The playlist data to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The AT-URI of the created playlist record.</returns>
    Task<string> CreatePlaylistAsync(
        string userDid,
        ATProtoPlaylistDto playlist,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Retrieves a playlist from a user's ATProto PDS by its AT-URI.
    /// </summary>
    /// <param name="playlistUri">The AT-URI of the playlist record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The playlist DTO, or null if not found.</returns>
    Task<ATProtoPlaylistDto?> GetPlaylistAsync(
        string playlistUri,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Updates an existing playlist on the user's ATProto PDS.
    /// The updatedAt timestamp will be set to the current UTC time.
    /// </summary>
    /// <param name="userDid">The DID of the user who owns the playlist.</param>
    /// <param name="rkey">The record key of the playlist to update.</param>
    /// <param name="playlist">The updated playlist data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The AT-URI of the updated playlist record.</returns>
    Task<string> UpdatePlaylistAsync(
        string userDid,
        string rkey,
        ATProtoPlaylistDto playlist,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes a playlist from the user's ATProto PDS.
    /// </summary>
    /// <param name="userDid">The DID of the user who owns the playlist.</param>
    /// <param name="rkey">The record key of the playlist to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the playlist was deleted, false if not found.</returns>
    Task<bool> DeletePlaylistAsync(
        string userDid,
        string rkey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Lists all playlists for a user from their ATProto PDS.
    /// Uses cursor-based pagination to handle large collections.
    /// </summary>
    /// <param name="userDid">The DID of the user whose playlists to list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of playlist DTOs.</returns>
    IAsyncEnumerable<ATProtoPlaylistDto> ListUserPlaylistsAsync(
        string userDid,
        CancellationToken cancellationToken = default
    );
}
