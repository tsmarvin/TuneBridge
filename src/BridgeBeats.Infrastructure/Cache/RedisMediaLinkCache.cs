using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Infrastructure.Storage;
using BridgeBeats.Infrastructure.Utilities;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BridgeBeats.Infrastructure.Cache;

/// <summary>
/// Redis-based implementation of <see cref="IMediaLinkCacheRepository"/> that uses Redis for lookup indices
/// and ATProto PDS for persistent storage. Redis stores only the RecordUri pointers and metadata;
/// all actual data is retrieved from the PDS which is the source of truth.
/// </summary>
/// <remarks>
/// Redis Key Patterns:
/// - lookup:isrc:{isrc} → RecordUri
/// - lookup:upc:{upc} → RecordUri
/// - lookup:url:{urlHash} → RecordUri
/// - lookup:card:{cardId} → RecordUri
/// - lookup:provider:{provider}:{id} → RecordUri
/// - lookup:metadata:{metadataHash} → RecordUri
/// - meta:{rkey} → JSON { CreatedAt, LastLookedUpAt, CardId }
/// - keys:{rkey} → Set of all lookup keys associated with this rkey (for cleanup on refresh)
/// </remarks>
public sealed class RedisMediaLinkCache : IMediaLinkCacheRepository {

    private readonly IConnectionMultiplexer _redis;
    private readonly IATProtoStorageService _atprotoStorage;
    private readonly ILogger<RedisMediaLinkCache> _logger;
    private readonly int _cacheDays;
    private readonly string _userDID;
    private readonly JsonSerializerOptions _jsonOptions;

    // Key prefixes
    private const string LookupIsrcPrefix = "lookup:isrc:";
    private const string LookupUpcPrefix = "lookup:upc:";
    private const string LookupUrlPrefix = "lookup:url:";
    private const string LookupCardPrefix = "lookup:card:";
    private const string LookupProviderPrefix = "lookup:provider:";
    private const string LookupMetadataPrefix = "lookup:metadata:";
    private const string MetaPrefix = "meta:";
    private const string KeysPrefix = "keys:";

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisMediaLinkCache"/> class.
    /// </summary>
    /// <param name="redis">The Redis connection multiplexer.</param>
    /// <param name="atprotoStorage">Service for storing and retrieving results from ATProto PDS.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="cacheDays">Number of days to consider cache entries fresh.</param>
    /// <param name="userDID">The user DID (Decentralized Identifier) that will post the records to the PDS.</param>
    public RedisMediaLinkCache(
        IConnectionMultiplexer redis,
        IATProtoStorageService atprotoStorage,
        ILogger<RedisMediaLinkCache> logger,
        int cacheDays,
        string userDID
    ) {
        _redis = redis ?? throw new ArgumentNullException( nameof( redis ) );
        _atprotoStorage = atprotoStorage ?? throw new ArgumentNullException( nameof( atprotoStorage ) );
        _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
        _cacheDays = cacheDays;
        _userDID = userDID ?? throw new ArgumentNullException( nameof( userDID ) );
        _jsonOptions = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <inheritdoc/>
    public async Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultAsync( string inputLink ) {
        if (string.IsNullOrWhiteSpace( inputLink )) { return null; }

        IDatabase db = _redis.GetDatabase( );
        string urlHash = HashUtility.HashUrl( inputLink );
        string key = $"{LookupUrlPrefix}{urlHash}";

        RedisValue recordUri = await db.StringGetAsync( key );
        return recordUri.IsNullOrEmpty
            ? null
            : await GetResultFromPdsAsync( recordUri.ToString( ) );
    }

    /// <inheritdoc/>
    public async Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByISRCAsync( string isrc ) {
        if (string.IsNullOrWhiteSpace( isrc )) { return null; }

        IDatabase db = _redis.GetDatabase( );
        string key = $"{LookupIsrcPrefix}{isrc.Trim().ToUpperInvariant()}";

        RedisValue recordUri = await db.StringGetAsync( key );

        // If not in Redis, try to generate the expected rkey and check PDS directly
        return recordUri.IsNullOrEmpty
            ? await TryGetFromPdsByExternalIdAsync( isrc.Trim( ).ToUpperInvariant( ), false )
            : await GetResultFromPdsAsync( recordUri.ToString( ) );
    }

    /// <inheritdoc/>
    public async Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByUPCAsync( string upc ) {
        if (string.IsNullOrWhiteSpace( upc )) { return null; }

        IDatabase db = _redis.GetDatabase( );
        string key = $"{LookupUpcPrefix}{upc.Trim().ToUpperInvariant()}";

        RedisValue recordUri = await db.StringGetAsync( key );

        // If not in Redis, try to generate the expected rkey and check PDS directly
        return recordUri.IsNullOrEmpty
            ? await TryGetFromPdsByExternalIdAsync( upc.Trim( ).ToUpperInvariant( ), true )
            : await GetResultFromPdsAsync( recordUri.ToString( ) );
    }

    /// <inheritdoc/>
    public async Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByMetadataAsync( string title, string artist ) {
        if (string.IsNullOrWhiteSpace( title ) && string.IsNullOrWhiteSpace( artist )) { return null; }

        IDatabase db = _redis.GetDatabase( );
        string metadataKey = $"{title?.Trim().ToLowerInvariant() ?? ""}|{artist?.Trim().ToLowerInvariant() ?? ""}";
        string metadataHash = HashUtility.ComputeSha256Base32( metadataKey );
        string key = $"{LookupMetadataPrefix}{metadataHash}";

        RedisValue recordUri = await db.StringGetAsync( key );
        return recordUri.IsNullOrEmpty
            ? null
            : await GetResultFromPdsAsync( recordUri.ToString( ) );
    }

    /// <inheritdoc/>
    public async Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByProviderIdAsync(
        string providerId,
        SupportedProviders provider,
        bool isAlbum
    ) {
        if (string.IsNullOrWhiteSpace( providerId )) { return null; }

        IDatabase db = _redis.GetDatabase( );
        string key = $"{LookupProviderPrefix}{provider}:{providerId.Trim()}";

        RedisValue recordUri = await db.StringGetAsync( key );
        return recordUri.IsNullOrEmpty
            ? null
            : await GetResultFromPdsAsync( recordUri.ToString( ) );
    }

    /// <inheritdoc/>
    public async Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByCardIdAsync( string cardId ) {
        if (string.IsNullOrWhiteSpace( cardId )) { return null; }

        IDatabase db = _redis.GetDatabase( );
        string key = $"{LookupCardPrefix}{cardId.Trim().ToLowerInvariant()}";

        RedisValue recordUri = await db.StringGetAsync( key );
        return recordUri.IsNullOrEmpty
            ? null
            : await GetResultFromPdsAsync( recordUri.ToString( ) );
    }

    /// <inheritdoc/>
    public async Task<string> CacheResultAsync( MediaLinkResult result ) {
        ArgumentNullException.ThrowIfNull( result );

        try {
            // Store on ATProto PDS first (creates/updates with deterministic rkey)
            string recordUri = await _atprotoStorage.StoreMediaLinkResultAsync( result );
            string rkey = RecordKeyGenerator.GenerateRkey( result );

            // Remove old lookup keys before adding new ones (explicit cleanup on refresh)
            await RemoveExistingLookupKeysAsync( rkey );

            // Add lookup indices to Redis
            await AddInputLinksAsync( recordUri, result );

            _logger.LogInformation( "Cached result with rkey: {Rkey}, recordUri: {RecordUri}",
                rkey.SanitizeForLogging( ),
                recordUri.SanitizeForLogging( ) );
            return recordUri;
        } catch (Exception ex) {
            _logger.LogError( ex, "Error while caching result" );
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task AddInputLinksAsync( string recordUri, MediaLinkResult result ) {
        ArgumentNullException.ThrowIfNull( result );
        if (string.IsNullOrWhiteSpace( recordUri )) {
            throw new ArgumentException( "Record URI cannot be null or empty", nameof( recordUri ) );
        }

        try {
            IDatabase db = _redis.GetDatabase( );
            string rkey = RecordKeyGenerator.GenerateRkey( result );
            string cardId = RecordKeyGenerator.GenerateCardId( rkey );
            TimeSpan expiry = TimeSpan.FromDays( _cacheDays );
            bool isAlbum = result.Results.Values.First( ).IsAlbum ?? false;

            // Track all keys we create for this rkey (for cleanup on refresh)
            List<string> allKeys = [];

            // Store metadata
            CacheEntryMetadata metadata = new( ) {
                Rkey = rkey,
                CardId = cardId,
                RecordUri = recordUri,
                CreatedAt = DateTime.UtcNow,
                LastLookedUpAt = DateTime.UtcNow
            };
            string metaKey = $"{MetaPrefix}{rkey}";
            _ = await db.StringSetAsync( metaKey, JsonSerializer.Serialize( metadata, _jsonOptions ), expiry );
            allKeys.Add( metaKey );

            // Store card ID lookup
            string cardKey = $"{LookupCardPrefix}{cardId}";
            _ = await db.StringSetAsync( cardKey, recordUri, expiry );
            allKeys.Add( cardKey );

            // Store URL lookups (input links + service URLs)
            List<string> allUrls = [.. result._inputLinks];
            allUrls.AddRange( result.Results.Values
                .Where( r => !string.IsNullOrWhiteSpace( r.URL ) )
                .Select( r => r.URL ) );

            foreach (string url in allUrls.Where( u => !string.IsNullOrWhiteSpace( u ) ).Distinct( )) {
                string urlHash = HashUtility.HashUrl( url );
                string urlKey = $"{LookupUrlPrefix}{urlHash}";
                _ = await db.StringSetAsync( urlKey, recordUri, expiry );
                allKeys.Add( urlKey );
            }

            // Store external ID lookups and metadata lookups
            foreach (MusicLookupResult lookupResult in result.Results.Values) {
                // External ID (ISRC or UPC)
                if (!string.IsNullOrWhiteSpace( lookupResult.ExternalId )) {
                    string prefix = isAlbum ? LookupUpcPrefix : LookupIsrcPrefix;
                    string externalIdKey = $"{prefix}{lookupResult.ExternalId.Trim().ToUpperInvariant()}";
                    _ = await db.StringSetAsync( externalIdKey, recordUri, expiry );
                    allKeys.Add( externalIdKey );
                }

                // Metadata lookup
                if (!string.IsNullOrWhiteSpace( lookupResult.Title ) || !string.IsNullOrWhiteSpace( lookupResult.Artist )) {
                    string metadataValue = $"{lookupResult.Title?.Trim().ToLowerInvariant() ?? ""}|{lookupResult.Artist?.Trim().ToLowerInvariant() ?? ""}";
                    if (metadataValue != "|") {
                        string metadataHash = HashUtility.ComputeSha256Base32( metadataValue );
                        string metadataLookupKey = $"{LookupMetadataPrefix}{metadataHash}";
                        _ = await db.StringSetAsync( metadataLookupKey, recordUri, expiry );
                        allKeys.Add( metadataLookupKey );
                    }
                }
            }

            // Store provider ID lookups
            foreach ((SupportedProviders provider, MusicLookupResult dto) in result.Results) {
                string? providerId = ExtractProviderIdFromUrl( provider, dto.URL );
                if (!string.IsNullOrWhiteSpace( providerId )) {
                    string providerKey = $"{LookupProviderPrefix}{provider}:{providerId}";
                    _ = await db.StringSetAsync( providerKey, recordUri, expiry );
                    allKeys.Add( providerKey );
                }
            }

            // Store the set of all keys for this rkey (for cleanup on refresh)
            string keysSetKey = $"{KeysPrefix}{rkey}";
            _ = await db.KeyDeleteAsync( keysSetKey );
            if (allKeys.Count > 0) {
                RedisValue[] keyValues = [.. allKeys.Select( k => (RedisValue)k )];
                _ = await db.SetAddAsync( keysSetKey, keyValues );
                _ = await db.KeyExpireAsync( keysSetKey, expiry );
            }

            _logger.LogInformation(
                "Created Redis lookup entries for rkey: {Rkey}, cardId: {CardId}, {KeyCount} keys",
                rkey.SanitizeForLogging( ),
                cardId.SanitizeForLogging( ),
                allKeys.Count
            );
        } catch (Exception ex) {
            _logger.LogError( ex, "Error while adding input links to Redis cache" );
            throw;
        }
    }

    /// <summary>
    /// Removes all existing lookup keys for an rkey before adding new ones.
    /// This ensures stale keys are cleaned up when a record is refreshed.
    /// </summary>
    private async Task RemoveExistingLookupKeysAsync( string rkey ) {
        IDatabase db = _redis.GetDatabase( );
        string keysSetKey = $"{KeysPrefix}{rkey}";

        RedisValue[] existingKeys = await db.SetMembersAsync( keysSetKey );
        if (existingKeys.Length > 0) {
            RedisKey[] keysToDelete = [.. existingKeys.Select( k => (RedisKey)k.ToString( ) )];
            _ = await db.KeyDeleteAsync( keysToDelete );
            _ = await db.KeyDeleteAsync( keysSetKey );

            _logger.LogInformation( "Removed {KeyCount} existing lookup keys for rkey: {Rkey}",
                existingKeys.Length,
                rkey.SanitizeForLogging( ) );
        }
    }

    /// <summary>
    /// Attempts to retrieve a result from PDS by generating the expected recordUri from an external ID.
    /// This is a fallback when the lookup key isn't in Redis but the record may exist on PDS.
    /// </summary>
    private async Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetFromPdsByExternalIdAsync(
        string externalId,
        bool isAlbum
    ) {
        try {
            string rkey = RecordKeyGenerator.GenerateRkey( externalId, isAlbum );
            string recordUri = $"at://{_userDID}/link.bridgebeats.lookup/{rkey}";

            MediaLinkResult? pdsResult = await _atprotoStorage.GetMediaLinkResultAsync( recordUri );
            if (pdsResult != null) {
                // Populate Redis cache for future lookups
                await AddInputLinksAsync( recordUri, pdsResult );
                return CheckRecordFreshness( pdsResult, recordUri );
            }
        } catch (Exception ex) {
            _logger.LogWarning( ex, "Failed to retrieve result from PDS by external ID: {ExternalId}",
                externalId.SanitizeForLogging( ) );
        }

        return null;
    }

    /// <summary>
    /// Retrieves a result from ATProto PDS and checks its freshness.
    /// </summary>
    private async Task<(MediaLinkResult result, string recordUri, bool isStale)?> GetResultFromPdsAsync( string recordUri ) {
        try {
            MediaLinkResult? result = await _atprotoStorage.GetMediaLinkResultAsync( recordUri );
            if (result != null) {
                _logger.LogInformation( "Cache hit for RecordUri: {RecordUri}",
                    recordUri.SanitizeForLogging( ) );
                return CheckRecordFreshness( result, recordUri );
            }

            // Record not found on PDS - remove from Redis
            _logger.LogWarning( "Record not found on PDS, will be cleaned up on TTL expiry: {RecordUri}",
                recordUri.SanitizeForLogging( ) );
        } catch (HttpRequestException ex) {
            _logger.LogError( ex, "HTTP error while trying to get cached result, StatusCode: {StatusCode}", ex.StatusCode );
        } catch (Exception ex) {
            _logger.LogError( ex, "Error while trying to get cached result from PDS" );
        }

        return null;
    }

    /// <summary>
    /// Checks if a result is stale based on the cache days configuration.
    /// </summary>
    private (MediaLinkResult result, string recordUri, bool isStale) CheckRecordFreshness(
        MediaLinkResult result,
        string recordUri
    ) {
        DateTime expirationDate = DateTime.UtcNow.AddDays( -_cacheDays );
        bool isStale = result.LookedUpAt < expirationDate;
        return (result, recordUri, isStale);
    }

    /// <summary>
    /// Extracts the provider-specific ID from a service URL.
    /// </summary>
    private string? ExtractProviderIdFromUrl( SupportedProviders provider, string url ) {
        if (string.IsNullOrWhiteSpace( url )) {
            return null;
        }

        try {
            return provider switch {
                SupportedProviders.AppleMusic => ExtractAppleMusicId( url ),
                SupportedProviders.Spotify => ExtractSpotifyId( url ),
                SupportedProviders.Tidal => ExtractTidalId( url ),
                _ => null
            };
        } catch (Exception ex) {
            _logger.LogWarning( ex, "Failed to extract provider ID from URL for {Provider}.", provider );
            return null;
        }
    }

    /// <summary>
    /// Extracts the Apple Music catalog ID from a URL.
    /// </summary>
    private static string? ExtractAppleMusicId( string url ) {
        // Apple Music has a special query parameter format for track IDs
        if (url.Contains( "?i=" )) {
            int queryIndex = url.IndexOf( "?i=" );
            if (queryIndex > 0) {
                string trackId = url[ (queryIndex + 3)..].Split( '&' )[0];
                if (!string.IsNullOrWhiteSpace( trackId )) {
                    return trackId;
                }
            }
        }

        // Apple Music URLs: .../album/name/id or .../song/name/id (offset +2 after type)
        return ExtractIdFromUrlPath( url, ["album", "song"], offsetAfterType: 2, validateDigitsOnly: true );
    }

    /// <summary>
    /// Extracts the Spotify track or album ID from a URL.
    /// </summary>
    private static string? ExtractSpotifyId( string url ) {
        // Spotify URLs: .../track/id or .../album/id (offset +1 after type)
        return ExtractIdFromUrlPath( url, ["track", "album"], offsetAfterType: 1, validateDigitsOnly: false );
    }

    /// <summary>
    /// Extracts the Tidal track or album ID from a URL.
    /// </summary>
    private static string? ExtractTidalId( string url ) {
        // Tidal URLs: .../track/id or .../album/id (offset +1 after type)
        return ExtractIdFromUrlPath( url, ["track", "album"], offsetAfterType: 1, validateDigitsOnly: true );
    }

    /// <summary>
    /// Helper method to extract an ID from a URL path based on type keywords and offset.
    /// </summary>
    /// <param name="url">The URL to parse.</param>
    /// <param name="typeKeywords">Array of type keywords to search for (e.g., "track", "album").</param>
    /// <param name="offsetAfterType">Number of segments after the type keyword where the ID is located.</param>
    /// <param name="validateDigitsOnly">Whether to validate that the extracted ID contains only digits.</param>
    /// <returns>The extracted ID, or null if not found or invalid.</returns>
    private static string? ExtractIdFromUrlPath( string url, string[] typeKeywords, int offsetAfterType, bool validateDigitsOnly ) {
        string[] parts = url.Split( '/' );
        
        for (int i = 0; i < parts.Length; i++) {
            // Check if current part matches any type keyword
            bool matchesType = typeKeywords.Any( keyword => parts[i].Equals( keyword, StringComparison.OrdinalIgnoreCase ) );

            if (matchesType && i + offsetAfterType < parts.Length) {
                // Extract ID from the specified offset and remove query parameters
                string id = parts[i + offsetAfterType].Split( '?' )[0];
                
                if (string.IsNullOrWhiteSpace( id )) {
                    continue;
                }

                // Validate digits only if required
                if (validateDigitsOnly && !id.All( char.IsDigit )) {
                    continue;
                }

                return id;
            }
        }

        return null;
    }

    /// <summary>
    /// Metadata stored in Redis for a cache entry.
    /// </summary>
    private sealed class CacheEntryMetadata {
        public string Rkey { get; set; } = string.Empty;
        public string CardId { get; set; } = string.Empty;
        public string RecordUri { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime LastLookedUpAt { get; set; }
    }
}
