using System.Text.Json;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BridgeBeats.Infrastructure.Cache;

/// <summary>
/// Redis-based implementation of <see cref="IGenreCacheService"/> for caching music genre data.
/// </summary>
/// <remarks>
/// Redis Key Patterns:
/// - genre:{provider}:{providerId} → Hash { genres: JSON array, cachedAt: ISO8601 }
/// - artist-genre:{provider}:{artistId} → Hash { genres: JSON array, cachedAt: ISO8601 }
/// - track-artists:{provider}:{trackId} → List of artist IDs
/// - artist-refresh-queue:{provider} → Sorted set (score = Unix timestamp)
///
/// Data persists until explicitly overwritten (no TTL).
/// </remarks>
public sealed class RedisGenreCache : IGenreCacheService {

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisGenreCache> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    // Key prefixes
    private const string GenrePrefix = "genre:";
    private const string ArtistGenrePrefix = "artist-genre:";
    private const string TrackArtistsPrefix = "track-artists:";
    private const string ArtistRefreshQueuePrefix = "artist-refresh-queue:";

    // Hash field names
    private const string GenresField = "genres";
    private const string CachedAtField = "cachedAt";

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisGenreCache"/> class.
    /// </summary>
    /// <param name="redis">The Redis connection multiplexer.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public async Task SetGenresAsync(
        SupportedProviders provider,
        string providerId,
        IEnumerable<string> genres,
        CancellationToken ct = default
    ) {
        if (string.IsNullOrWhiteSpace( providerId )) { return; }

        IDatabase db = _redis.GetDatabase( );
        string key = BuildGenreKey( provider, providerId );

        List<string> genreList = genres.ToList( );
        string genresJson = JsonSerializer.Serialize( genreList, _jsonOptions );
        string cachedAt = DateTimeOffset.UtcNow.ToString( "o" );

        HashEntry[] entries = [
            new HashEntry( GenresField, genresJson ),
            new HashEntry( CachedAtField, cachedAt )
        ];

        await db.HashSetAsync( key, entries );

        _logger.LogDebug(
            "Cached {Count} genres for {Provider} track {ProviderId}",
            genreList.Count,
            provider,
            providerId
        );
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public async Task SetArtistGenresAsync(
        SupportedProviders provider,
        string artistId,
        IEnumerable<string> genres,
        CancellationToken ct = default
    ) {
        if (string.IsNullOrWhiteSpace( artistId )) { return; }

        IDatabase db = _redis.GetDatabase( );
        string key = BuildArtistGenreKey( provider, artistId );

        List<string> genreList = genres.ToList( );
        string genresJson = JsonSerializer.Serialize( genreList, _jsonOptions );
        string cachedAt = DateTimeOffset.UtcNow.ToString( "o" );

        HashEntry[] entries = [
            new HashEntry( GenresField, genresJson ),
            new HashEntry( CachedAtField, cachedAt )
        ];

        await db.HashSetAsync( key, entries );

        _logger.LogDebug(
            "Cached {Count} genres for {Provider} artist {ArtistId}",
            genreList.Count,
            provider,
            artistId
        );
    }

    /// <inheritdoc/>
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
            : (IReadOnlyList<string>)artistIds
            .Where( v => !v.IsNullOrEmpty )
            .Select( v => v.ToString( ) )
            .ToList( );
    }

    /// <inheritdoc/>
    public async Task SetTrackArtistMappingAsync(
        SupportedProviders provider,
        string trackId,
        IEnumerable<string> artistIds,
        CancellationToken ct = default
    ) {
        if (string.IsNullOrWhiteSpace( trackId )) { return; }

        List<string> artistList = artistIds.Where( id => !string.IsNullOrWhiteSpace( id ) ).ToList( );
        if (artistList.Count == 0) { return; }

        IDatabase db = _redis.GetDatabase( );
        string key = BuildTrackArtistsKey( provider, trackId );

        // Delete existing and set new (atomic via transaction)
        ITransaction transaction = db.CreateTransaction( );
        _ = transaction.KeyDeleteAsync( key );
        _ = transaction.ListRightPushAsync( key, artistList.Select( id => (RedisValue)id ).ToArray( ) );
        _ = await transaction.ExecuteAsync( );

        _logger.LogDebug(
            "Cached {Count} artist mappings for {Provider} track {TrackId}",
            artistList.Count,
            provider,
            trackId
        );
    }

    /// <inheritdoc/>
    public async Task EnqueueArtistsForRefreshAsync(
        SupportedProviders provider,
        IEnumerable<string> artistIds,
        CancellationToken ct = default
    ) {
        List<string> artistList = artistIds.Where( id => !string.IsNullOrWhiteSpace( id ) ).Distinct( ).ToList( );
        if (artistList.Count == 0) { return; }

        IDatabase db = _redis.GetDatabase( );
        string queueKey = BuildArtistRefreshQueueKey( provider );

        // Score = Unix timestamp for FIFO ordering
        double score = DateTimeOffset.UtcNow.ToUnixTimeSeconds( );

        // Use NX to avoid updating score if already in queue
        SortedSetEntry[] entries = artistList
            .Select( id => new SortedSetEntry( id, score ) )
            .ToArray( );

        // SortedSetAdd with When.NotExists prevents duplicate entries
        foreach (string artistId in artistList) {
            _ = await db.SortedSetAddAsync( queueKey, artistId, score, When.NotExists );
        }

        _logger.LogDebug(
            "Enqueued {Count} artists for {Provider} genre refresh",
            artistList.Count,
            provider
        );
    }

    /// <inheritdoc/>
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

        List<string> artistIds = entries
            .Where( e => e.Element.HasValue )
            .Select( e => e.Element.ToString( ) )
            .ToList( );

        if (artistIds.Count > 0) {
            _logger.LogDebug(
                "Dequeued {Count} artists from {Provider} refresh queue",
                artistIds.Count,
                provider
            );
        }

        return artistIds;
    }

    /// <inheritdoc/>
    public async Task<long> GetArtistRefreshQueueLengthAsync(
        SupportedProviders provider,
        CancellationToken ct = default
    ) {
        IDatabase db = _redis.GetDatabase( );
        string queueKey = BuildArtistRefreshQueueKey( provider );

        return await db.SortedSetLengthAsync( queueKey );
    }

    /// <summary>
    /// Resolves Spotify track genres on-demand by merging cached artist genres.
    /// </summary>
    private async Task<IReadOnlyList<string>?> ResolveSpotifyGenresOnDemandAsync(
        IDatabase db,
        string trackId,
        CancellationToken ct
    ) {
        // Get track→artist mapping
        string trackArtistsKey = BuildTrackArtistsKey( SupportedProviders.Spotify, trackId );
        RedisValue[] artistIds = await db.ListRangeAsync( trackArtistsKey );

        if (artistIds.Length == 0) {
            _logger.LogDebug( "No artist mapping found for Spotify track {TrackId}", trackId );
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
            _logger.LogDebug(
                "No artist genres cached yet for Spotify track {TrackId}",
                trackId
            );
            return null;
        }

        // Cache the merged result for future lookups
        List<string> genreList = mergedGenres.ToList( );
        await SetGenresAsync( SupportedProviders.Spotify, trackId, genreList, ct );

        _logger.LogDebug(
            "Resolved {Count} genres for Spotify track {TrackId} from {ArtistCount} artists",
            genreList.Count,
            trackId,
            artistIds.Length
        );

        return genreList;
    }

    /// <summary>
    /// Parses the genres array from a Redis hash.
    /// </summary>
    private IReadOnlyList<string>? ParseGenresFromHash( HashEntry[] hashEntries ) {
        RedisValue genresValue = hashEntries.FirstOrDefault( e => e.Name == GenresField ).Value;

        if (genresValue.IsNullOrEmpty) { return null; }

        try {
            List<string>? genres = JsonSerializer.Deserialize<List<string>>( genresValue.ToString( ), _jsonOptions );
            return genres;
        } catch (JsonException ex) {
            _logger.LogWarning( ex, "Failed to parse cached genres JSON" );
            return null;
        }
    }

    /// <summary>
    /// Builds the Redis key for track-level genres.
    /// </summary>
    private static string BuildGenreKey( SupportedProviders provider, string providerId ) =>
        $"{GenrePrefix}{provider}:{providerId}";

    /// <summary>
    /// Builds the Redis key for artist-level genres.
    /// </summary>
    private static string BuildArtistGenreKey( SupportedProviders provider, string artistId ) =>
        $"{ArtistGenrePrefix}{provider}:{artistId}";

    /// <summary>
    /// Builds the Redis key for track→artist mapping.
    /// </summary>
    private static string BuildTrackArtistsKey( SupportedProviders provider, string trackId ) =>
        $"{TrackArtistsPrefix}{provider}:{trackId}";

    /// <summary>
    /// Builds the Redis key for the artist refresh queue.
    /// </summary>
    private static string BuildArtistRefreshQueueKey( SupportedProviders provider ) =>
        $"{ArtistRefreshQueuePrefix}{provider}";
}
