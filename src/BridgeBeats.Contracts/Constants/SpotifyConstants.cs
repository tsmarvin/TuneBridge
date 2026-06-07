namespace BridgeBeats.Contracts.Constants;

/// <summary>
/// Constants for Spotify API integration, including batch operation limits
/// and rate-limit endpoint identifiers.
/// </summary>
public static class SpotifyConstants {

    /// <summary>
    /// Maximum number of track IDs that can be requested in a single bulk lookup.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /tracks?ids={comma-separated-ids}
    /// Documentation: https://developer.spotify.com/documentation/web-api/reference/get-several-tracks
    /// </remarks>
    public const int MaxTracksPerBatchLookup = 50;

    /// <summary>
    /// Maximum number of album IDs that can be requested in a single bulk lookup.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /albums?ids={comma-separated-ids}
    /// Documentation: https://developer.spotify.com/documentation/web-api/reference/get-multiple-albums
    /// </remarks>
    public const int MaxAlbumsPerBatchLookup = 20;

    /// <summary>
    /// Maximum number of artist IDs that can be requested in a single bulk lookup.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /artists?ids={comma-separated-ids}
    /// Documentation: https://developer.spotify.com/documentation/web-api/reference/get-multiple-artists
    /// </remarks>
    public const int MaxArtistsPerBatchLookup = 50;

    /// <summary>
    /// Rate limit endpoint key for bulk track lookups.
    /// Used to track rate limits separately from single-track lookups.
    /// </summary>
    public const string BulkTracksEndpoint = "BulkTracks";

    /// <summary>
    /// Rate limit endpoint key for bulk album lookups.
    /// Used to track rate limits separately from single-album lookups.
    /// </summary>
    public const string BulkAlbumsEndpoint = "BulkAlbums";

    /// <summary>
    /// Rate limit endpoint key for bulk artist lookups.
    /// Used to track rate limits separately from single-artist lookups.
    /// </summary>
    public const string BulkArtistsEndpoint = "BulkArtists";

    // -------------------------------------------------------------------------
    // Type-specific bulk stream names
    // -------------------------------------------------------------------------

    /// <summary>
    /// Redis stream key for <c>SongIdLookup</c> messages routed by
    /// <c>SpotifyBulkQueueDecorator</c> and consumed by <c>SpotifyBulkProcessorService</c>.
    /// </summary>
    /// <remarks>
    /// Using this constant in every component that reads or writes the stream prevents
    /// the silent key-mismatch failure where a producer and consumer disagree on the
    /// stream name and no messages are ever delivered.
    /// </remarks>
    public const string BulkTrackIdStream = "queue:spotify:bulk:track-id";

    /// <summary>
    /// Redis stream key for <c>AlbumIdLookup</c> messages routed by
    /// <c>SpotifyBulkQueueDecorator</c> and consumed by <c>SpotifyBulkProcessorService</c>.
    /// </summary>
    public const string BulkAlbumIdStream = "queue:spotify:bulk:album-id";

}
