using System.Text.Json;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Infrastructure.Logging;
using StackExchange.Redis;

namespace BridgeBeats.Core.Infrastructure.Cache;

/// <summary>
/// Redis-backed implementation of <see cref="IGenreCacheService"/> that caches track and artist
/// genres, plus a per-provider artist-refresh queue.
/// </summary>
/// <remarks>
/// Genres for a track are stored in hash <c>genre:{provider}:{id}</c> and for an artist in
/// <c>artist-genre:{provider}:{id}</c> (each hash holds a <c>genres</c> JSON array and an
/// ISO-8601 <c>cachedAt</c> timestamp); a track's artist ids are stored as a list in
/// <c>track-artists:{provider}:{trackId}</c>. Artists awaiting a genre refresh are held in the
/// sorted set <c>artist-refresh-queue:{provider}</c>, scored by enqueue time so they drain oldest
/// first. When a Spotify track has no cached genres, they are resolved on demand by merging the
/// genres of its artists and back-filling the track entry. Data persists until explicitly
/// overwritten (no TTL).
/// </remarks>
public sealed partial class RedisGenreCache : IGenreCacheService {

    /// <summary>Redis connection used for all genre-cache operations.</summary>
    private readonly IConnectionMultiplexer _redis;

    /// <summary>Logger for genre-cache diagnostics.</summary>
    private readonly ILogger<RedisGenreCache> _logger;

    /// <summary>Camel-case options used to serialize and deserialize genre lists.</summary>
    private readonly JsonSerializerOptions _jsonOptions;

    // Key prefixes

    /// <summary>Key prefix for track-genre hashes. Literal value: <c>"genre:"</c>.</summary>
    private const string GenrePrefix = "genre:";

    /// <summary>Key prefix for artist-genre hashes. Literal value: <c>"artist-genre:"</c>.</summary>
    private const string ArtistGenrePrefix = "artist-genre:";

    /// <summary>Key prefix for track-to-artists lists. Literal value: <c>"track-artists:"</c>.</summary>
    private const string TrackArtistsPrefix = "track-artists:";

    /// <summary>Key prefix for the artist-refresh sorted sets. Literal value: <c>"artist-refresh-queue:"</c>.</summary>
    private const string ArtistRefreshQueuePrefix = "artist-refresh-queue:";

    // Hash field names

    /// <summary>Hash field holding the serialized genre list. Literal value: <c>"genres"</c>.</summary>
    private const string GenresField = "genres";

    /// <summary>Hash field holding the cache timestamp. Literal value: <c>"cachedAt"</c>.</summary>
    private const string CachedAtField = "cachedAt";

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisGenreCache"/> class.
    /// </summary>
    /// <param name="redis">The Redis connection multiplexer, used for all genre-cache operations.</param>
    /// <param name="logger">Logger for genre-cache diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="redis"/> or <paramref name="logger"/> is null.</exception>
    public RedisGenreCache(
        IConnectionMultiplexer redis,
        ILogger<RedisGenreCache> logger
    ) {
        _redis = redis ?? throw new ArgumentNullException( nameof( redis ) );
        _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
        _jsonOptions = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <summary>
    /// Gets the cached genres for a track.
    /// </summary>
    /// <param name="provider">The provider that owns the track id.</param>
    /// <param name="providerId">The provider-specific track id.</param>
    /// <param name="ct">Token forwarded to on-demand resolution.</param>
    /// <returns>
    /// The cached genres, or null when none are cached. For Spotify, a miss triggers on-demand
    /// resolution from the track's artists; for other providers a miss returns null.
    /// </returns>
    public async Task<IReadOnlyList<string>?> GetGenresAsync(
        SupportedProviders provider,
        string providerId,
        CancellationToken ct = default
    ) {
        if (string.IsNullOrWhiteSpace( providerId )) { return null; }

        IDatabase db = _redis.GetDatabase( );
        string key = BuildGenreKey( provider, providerId );

        HashEntry[] hashEntries = await db.HashGetAllAsync( key );
        if (hashEntries.Length == 0) {
            // For Spotify, try on-demand resolution via track→artist mapping
            return provider == SupportedProviders.Spotify ? await ResolveSpotifyGenresOnDemandAsync( db, providerId, ct ) : null;
        }

        return ParseGenresFromHash( hashEntries );
    }

    /// <summary>
    /// Caches the genres for a track, with a cache timestamp.
    /// </summary>
    /// <param name="provider">The provider that owns the track id.</param>
    /// <param name="providerId">The provider-specific track id. Blank ids are ignored.</param>
    /// <param name="genres">The genres to cache.</param>
    /// <param name="ct">A cancellation token (not currently observed).</param>
    /// <returns>A task that completes once the hash is written.</returns>
    public async Task SetGenresAsync(
        SupportedProviders provider,
        string providerId,
        IEnumerable<string> genres,
        CancellationToken ct = default
    ) {
        if (string.IsNullOrWhiteSpace( providerId )) { return; }

        IDatabase db = _redis.GetDatabase( );
        string key = BuildGenreKey( provider, providerId );

        List<string> genreList = [.. genres];
        string genresJson = JsonSerializer.Serialize( genreList, _jsonOptions );
        string cachedAt = DateTimeOffset.UtcNow.ToString( "o" );

        HashEntry[] entries = [
            new HashEntry( GenresField, genresJson ),
            new HashEntry( CachedAtField, cachedAt )
        ];

        await db.HashSetAsync( key, entries );

        LogCachedTrackGenres( _logger, genreList.Count, provider, providerId );
    }

    /// <summary>
    /// Gets the cached genres for an artist.
    /// </summary>
    /// <param name="provider">The provider that owns the artist id.</param>
    /// <param name="artistId">The provider-specific artist id.</param>
    /// <param name="ct">A cancellation token (not currently observed).</param>
    /// <returns>The cached genres, or null when none are cached.</returns>
    public async Task<IReadOnlyList<string>?> GetArtistGenresAsync(
        SupportedProviders provider,
        string artistId,
        CancellationToken ct = default
    ) {
        if (string.IsNullOrWhiteSpace( artistId )) { return null; }

        IDatabase db = _redis.GetDatabase( );
        string key = BuildArtistGenreKey( provider, artistId );

        HashEntry[] hashEntries = await db.HashGetAllAsync( key );
        return hashEntries.Length == 0 ? null : ParseGenresFromHash( hashEntries );
    }

    /// <summary>
    /// Caches the genres for an artist, with a cache timestamp.
    /// </summary>
    /// <param name="provider">The provider that owns the artist id.</param>
    /// <param name="artistId">The provider-specific artist id. Blank ids are ignored.</param>
    /// <param name="genres">The genres to cache.</param>
    /// <param name="ct">A cancellation token (not currently observed).</param>
    /// <returns>A task that completes once the hash is written.</returns>
    public async Task SetArtistGenresAsync(
        SupportedProviders provider,
        string artistId,
        IEnumerable<string> genres,
        CancellationToken ct = default
    ) {
        if (string.IsNullOrWhiteSpace( artistId )) { return; }

        IDatabase db = _redis.GetDatabase( );
        string key = BuildArtistGenreKey( provider, artistId );

        List<string> genreList = [.. genres];
        string genresJson = JsonSerializer.Serialize( genreList, _jsonOptions );
        string cachedAt = DateTimeOffset.UtcNow.ToString( "o" );

        HashEntry[] entries = [
            new HashEntry( GenresField, genresJson ),
            new HashEntry( CachedAtField, cachedAt )
        ];

        await db.HashSetAsync( key, entries );

        LogCachedArtistGenres( _logger, genreList.Count, provider, artistId );
    }

    /// <summary>
    /// Gets the cached artist ids for a track.
    /// </summary>
    /// <param name="provider">The provider that owns the track id.</param>
    /// <param name="trackId">The provider-specific track id.</param>
    /// <param name="ct">A cancellation token (not currently observed).</param>
    /// <returns>The artist ids, or null when no mapping is cached.</returns>
    public async Task<IReadOnlyList<string>?> GetTrackArtistMappingAsync(
        SupportedProviders provider,
        string trackId,
        CancellationToken ct = default
    ) {
        if (string.IsNullOrWhiteSpace( trackId )) { return null; }

        IDatabase db = _redis.GetDatabase( );
        string key = BuildTrackArtistsKey( provider, trackId );

        RedisValue[] artistIds = await db.ListRangeAsync( key );
        return artistIds.Length == 0
            ? null
            : [.. artistIds
            .Where( v => !v.IsNullOrEmpty )
            .Select( v => v.ToString( ) )];
    }

    /// <summary>
    /// Caches the artist ids for a track, replacing any existing mapping.
    /// </summary>
    /// <param name="provider">The provider that owns the track id.</param>
    /// <param name="trackId">The provider-specific track id. Blank ids are ignored.</param>
    /// <param name="artistIds">The artist ids to store; blank entries are dropped.</param>
    /// <param name="ct">A cancellation token (not currently observed).</param>
    /// <returns>A task that completes once the list is replaced, or immediately if there is nothing to store.</returns>
    /// <remarks>The existing list is deleted and re-pushed within a transaction so the mapping is replaced atomically.</remarks>
    public async Task SetTrackArtistMappingAsync(
        SupportedProviders provider,
        string trackId,
        IEnumerable<string> artistIds,
        CancellationToken ct = default
    ) {
        if (string.IsNullOrWhiteSpace( trackId )) { return; }

        List<string> artistList = [.. artistIds.Where( id => !string.IsNullOrWhiteSpace( id ) )];
        if (artistList.Count == 0) { return; }

        IDatabase db = _redis.GetDatabase( );
        string key = BuildTrackArtistsKey( provider, trackId );

        // Delete existing and set new (atomic via transaction)
        ITransaction transaction = db.CreateTransaction( );
        _ = transaction.KeyDeleteAsync( key );
        _ = transaction.ListRightPushAsync( key, [.. artistList.Select( id => (RedisValue)id )] );
        _ = await transaction.ExecuteAsync( );

        LogCachedArtistMappings( _logger, artistList.Count, provider, trackId );
    }

    /// <summary>
    /// Enqueues artists for a genre refresh, scored by enqueue time.
    /// </summary>
    /// <param name="provider">The provider whose refresh queue to add to.</param>
    /// <param name="artistIds">The artist ids to enqueue; blank entries and duplicates are dropped.</param>
    /// <param name="ct">A cancellation token (not currently observed).</param>
    /// <returns>A task that completes once the artists are enqueued.</returns>
    /// <remarks>
    /// Each id is added to the sorted set only if not already present, so an artist already waiting
    /// keeps its original (older) score and position rather than being pushed to the back.
    /// </remarks>
    public async Task EnqueueArtistsForRefreshAsync(
        SupportedProviders provider,
        IEnumerable<string> artistIds,
        CancellationToken ct = default
    ) {
        List<string> artistList = [.. artistIds.Where( id => !string.IsNullOrWhiteSpace( id ) ).Distinct( )];
        if (artistList.Count == 0) { return; }

        IDatabase db = _redis.GetDatabase( );
        string queueKey = BuildArtistRefreshQueueKey( provider );

        // Score = Unix timestamp for FIFO ordering
        double score = DateTimeOffset.UtcNow.ToUnixTimeSeconds( );

        // SortedSetAdd with When.NotExists prevents duplicate entries
        foreach (string artistId in artistList) {
            _ = await db.SortedSetAddAsync( queueKey, artistId, score, When.NotExists );
        }

        LogEnqueuedArtists( _logger, artistList.Count, provider );
    }

    /// <summary>
    /// Pops a batch of the oldest-queued artists from the refresh queue.
    /// </summary>
    /// <param name="provider">The provider whose refresh queue to drain.</param>
    /// <param name="batchSize">Maximum number of artists to pop; a non-positive value pops none.</param>
    /// <param name="ct">A cancellation token (not currently observed).</param>
    /// <returns>The popped artist ids, oldest first; empty when the queue is empty or the batch size is non-positive.</returns>
    public async Task<IReadOnlyList<string>> DequeueArtistsForRefreshAsync(
        SupportedProviders provider,
        int batchSize,
        CancellationToken ct = default
    ) {
        if (batchSize <= 0) { return []; }

        IDatabase db = _redis.GetDatabase( );
        string queueKey = BuildArtistRefreshQueueKey( provider );

        // Pop the oldest entries (lowest scores = earliest queued)
        SortedSetEntry[] entries = await db.SortedSetPopAsync( queueKey, batchSize, Order.Ascending );

        List<string> artistIds = [..
            entries
            .Where( e => e.Element.HasValue )
            .Select( e => e.Element.ToString( ) )
        ];

        if (artistIds.Count > 0) {
            LogDequeuedArtists( _logger, artistIds.Count, provider );
        }

        return artistIds;
    }

    /// <summary>
    /// Returns the number of artists currently waiting in a provider's refresh queue.
    /// </summary>
    /// <param name="provider">The provider whose refresh queue to measure.</param>
    /// <param name="ct">A cancellation token (not currently observed).</param>
    /// <returns>The sorted-set length.</returns>
    public async Task<long> GetArtistRefreshQueueLengthAsync(
        SupportedProviders provider,
        CancellationToken ct = default
    ) {
        IDatabase db = _redis.GetDatabase( );
        string queueKey = BuildArtistRefreshQueueKey( provider );

        return await db.SortedSetLengthAsync( queueKey );
    }

    /// <summary>
    /// Resolves Spotify track genres on demand by merging the genres of the track's artists, then
    /// caches the result on the track.
    /// </summary>
    /// <param name="db">The Redis database to read from.</param>
    /// <param name="trackId">The Spotify track id to resolve.</param>
    /// <param name="ct">Token forwarded to the back-fill write.</param>
    /// <returns>
    /// The merged genres (also written back to the track entry), or null when the track has no
    /// artist mapping or none of its artists have cached genres.
    /// </returns>
    private async Task<IReadOnlyList<string>?> ResolveSpotifyGenresOnDemandAsync(
        IDatabase db,
        string trackId,
        CancellationToken ct
    ) {
        // Get track→artist mapping
        string trackArtistsKey = BuildTrackArtistsKey( SupportedProviders.Spotify, trackId );
        RedisValue[] artistIds = await db.ListRangeAsync( trackArtistsKey );

        if (artistIds.Length == 0) {
            LogNoArtistMapping( _logger, trackId );
            return null;
        }

        // Collect genres from all artists
        HashSet<string> mergedGenres = new( StringComparer.OrdinalIgnoreCase );
        bool anyArtistHasGenres = false;

        foreach (RedisValue artistId in artistIds) {
            if (artistId.IsNullOrEmpty) { continue; }

            string artistGenreKey = BuildArtistGenreKey( SupportedProviders.Spotify, artistId.ToString( ) );
            HashEntry[] artistHash = await db.HashGetAllAsync( artistGenreKey );

            if (artistHash.Length > 0) {
                IReadOnlyList<string>? artistGenres = ParseGenresFromHash( artistHash );
                if (artistGenres != null) {
                    anyArtistHasGenres = true;
                    foreach (string genre in artistGenres) {
                        _ = mergedGenres.Add( genre );
                    }
                }
            }
        }

        if (!anyArtistHasGenres) {
            LogNoArtistGenres( _logger, trackId );
            return null;
        }

        // Cache the merged result for future lookups
        List<string> genreList = [.. mergedGenres];
        await SetGenresAsync( SupportedProviders.Spotify, trackId, genreList, ct );

        LogResolvedGenres( _logger, genreList.Count, trackId, artistIds.Length );

        return genreList;
    }

    /// <summary>
    /// Parses the genres array from a Redis genre hash's <c>genres</c> field.
    /// </summary>
    /// <param name="hashEntries">The hash entries read from a genre key.</param>
    /// <returns>The parsed genres, or null when the field is empty or fails to parse.</returns>
    private List<string>? ParseGenresFromHash( HashEntry[] hashEntries ) {
        RedisValue genresValue = hashEntries.FirstOrDefault( e => e.Name == GenresField ).Value;

        if (genresValue.IsNullOrEmpty) { return null; }

        try {
            List<string>? genres = JsonSerializer.Deserialize<List<string>>( genresValue.ToString( ), _jsonOptions );
            return genres;
        } catch (JsonException ex) {
            LogParseError( _logger, ex );
            return null;
        }
    }

    /// <summary>Builds the Redis key for track-level genres: <c>genre:{provider}:{providerId}</c>.</summary>
    /// <param name="provider">The provider.</param>
    /// <param name="providerId">The track id.</param>
    /// <returns>The track-genre key.</returns>
    private static string BuildGenreKey( SupportedProviders provider, string providerId ) =>
        $"{GenrePrefix}{provider}:{providerId}";

    /// <summary>Builds the Redis key for artist-level genres: <c>artist-genre:{provider}:{artistId}</c>.</summary>
    /// <param name="provider">The provider.</param>
    /// <param name="artistId">The artist id.</param>
    /// <returns>The artist-genre key.</returns>
    private static string BuildArtistGenreKey( SupportedProviders provider, string artistId ) =>
        $"{ArtistGenrePrefix}{provider}:{artistId}";

    /// <summary>Builds the Redis key for the track→artist mapping: <c>track-artists:{provider}:{trackId}</c>.</summary>
    /// <param name="provider">The provider.</param>
    /// <param name="trackId">The track id.</param>
    /// <returns>The track-to-artists key.</returns>
    private static string BuildTrackArtistsKey( SupportedProviders provider, string trackId ) =>
        $"{TrackArtistsPrefix}{provider}:{trackId}";

    /// <summary>Builds the Redis key for the artist refresh queue: <c>artist-refresh-queue:{provider}</c>.</summary>
    /// <param name="provider">The provider.</param>
    /// <returns>The artist-refresh-queue key.</returns>
    private static string BuildArtistRefreshQueueKey( SupportedProviders provider ) =>
        $"{ArtistRefreshQueuePrefix}{provider}";

    #region LoggerMessage Methods

    /// <summary>Logs that track genres were cached.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="count">Number of genres cached.</param>
    /// <param name="provider">The provider.</param>
    /// <param name="providerId">The track id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Cache.RedisGenreCacheCachedTrackGenres,
        Level = LogLevel.Debug,
        Message = "Cached {Count} genres for {Provider} track {ProviderId}" )]
    internal static partial void LogCachedTrackGenres( ILogger logger, int count, SupportedProviders provider, string providerId );

    /// <summary>Logs that artist genres were cached.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="count">Number of genres cached.</param>
    /// <param name="provider">The provider.</param>
    /// <param name="artistId">The artist id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Cache.RedisGenreCacheCachedArtistGenres,
        Level = LogLevel.Debug,
        Message = "Cached {Count} genres for {Provider} artist {ArtistId}" )]
    internal static partial void LogCachedArtistGenres( ILogger logger, int count, SupportedProviders provider, string artistId );

    /// <summary>Logs that a track-to-artists mapping was cached.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="count">Number of artist ids stored.</param>
    /// <param name="provider">The provider.</param>
    /// <param name="trackId">The track id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Cache.RedisGenreCacheCachedArtistMappings,
        Level = LogLevel.Debug,
        Message = "Cached {Count} artist mappings for {Provider} track {TrackId}" )]
    internal static partial void LogCachedArtistMappings( ILogger logger, int count, SupportedProviders provider, string trackId );

    /// <summary>Logs that artists were enqueued for a genre refresh.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="count">Number of artists enqueued.</param>
    /// <param name="provider">The provider.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Cache.RedisGenreCacheEnqueuedArtists,
        Level = LogLevel.Debug,
        Message = "Enqueued {Count} artists for {Provider} genre refresh" )]
    internal static partial void LogEnqueuedArtists( ILogger logger, int count, SupportedProviders provider );

    /// <summary>Logs that artists were dequeued from the refresh queue.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="count">Number of artists dequeued.</param>
    /// <param name="provider">The provider.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Cache.RedisGenreCacheDequeuedArtists,
        Level = LogLevel.Debug,
        Message = "Dequeued {Count} artists from {Provider} refresh queue" )]
    internal static partial void LogDequeuedArtists( ILogger logger, int count, SupportedProviders provider );

    /// <summary>Logs that a Spotify track had no cached artist mapping during on-demand resolution.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="trackId">The track id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Cache.RedisGenreCacheNoArtistMapping,
        Level = LogLevel.Debug,
        Message = "No artist mapping found for Spotify track {TrackId}" )]
    internal static partial void LogNoArtistMapping( ILogger logger, string trackId );

    /// <summary>Logs that none of a Spotify track's artists had cached genres during on-demand resolution.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="trackId">The track id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Cache.RedisGenreCacheNoArtistGenres,
        Level = LogLevel.Debug,
        Message = "No artist genres cached yet for Spotify track {TrackId}" )]
    internal static partial void LogNoArtistGenres( ILogger logger, string trackId );

    /// <summary>Logs that track genres were resolved on demand from the track's artists.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="count">Number of merged genres.</param>
    /// <param name="trackId">The track id.</param>
    /// <param name="artistCount">Number of artists merged from.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Cache.RedisGenreCacheResolvedGenres,
        Level = LogLevel.Debug,
        Message = "Resolved {Count} genres for Spotify track {TrackId} from {ArtistCount} artists" )]
    internal static partial void LogResolvedGenres( ILogger logger, int count, string trackId, int artistCount );

    /// <summary>Logs that a cached genres JSON value failed to parse.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The parse exception.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Cache.RedisGenreCacheParseError,
        Level = LogLevel.Warning,
        Message = "Failed to parse cached genres JSON" )]
    internal static partial void LogParseError( ILogger logger, Exception ex );

    #endregion
}
