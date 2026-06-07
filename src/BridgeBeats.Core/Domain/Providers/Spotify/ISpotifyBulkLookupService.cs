using BridgeBeats.Contracts.DTOs;

namespace BridgeBeats.Core.Domain.Providers.Spotify;

/// <summary>
/// Provides bulk (batch) lookup operations for Spotify tracks and albums.
/// </summary>
/// <remarks>
/// This interface is the testability seam for <c>SpotifyBulkProcessorService</c>.
/// The concrete implementation is <c>SpotifyLookupService</c>, which is sealed and
/// constructed with HTTP/token dependencies that make direct instantiation in unit
/// tests impractical.
/// </remarks>
public interface ISpotifyBulkLookupService {

    /// <summary>
    /// Performs a bulk lookup of tracks by their Spotify IDs.
    /// </summary>
    /// <param name="trackIds">The Spotify track IDs to look up (max 50).</param>
    /// <returns>
    /// A dictionary mapping track ID to result (null value = not found).
    /// An <b>empty dictionary</b> indicates a request-level failure
    /// (auth error, network error); callers should requeue without writing saga state.
    /// </returns>
    Task<Dictionary<string, MusicLookupResult?>> GetTracksByIdsAsync( IEnumerable<string> trackIds );

    /// <summary>
    /// Performs a bulk lookup of albums by their Spotify IDs.
    /// </summary>
    /// <param name="albumIds">The Spotify album IDs to look up (max 20).</param>
    /// <returns>
    /// A dictionary mapping album ID to result (null value = not found).
    /// An <b>empty dictionary</b> indicates a request-level failure; callers should
    /// requeue without writing saga state.
    /// </returns>
    Task<Dictionary<string, MusicLookupResult?>> GetAlbumsByIdsAsync( IEnumerable<string> albumIds );
}
