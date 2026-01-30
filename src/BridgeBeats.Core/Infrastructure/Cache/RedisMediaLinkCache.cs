using System.Net;
using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Domain.Utilities;
using BridgeBeats.Core.Infrastructure.Logging;
using BridgeBeats.Core.Infrastructure.Storage;
using BridgeBeats.Core.Infrastructure.Utilities;
using StackExchange.Redis;

namespace BridgeBeats.Core.Infrastructure.Cache;

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
public sealed partial class RedisMediaLinkCache : IMediaLinkCacheRepository {

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

            LogCachedResult( _logger, rkey.SanitizeForLogging( ), recordUri.SanitizeForLogging( ) );
            return recordUri;
        } catch (Exception ex) {
            LogCacheError( _logger, ex );
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task AddInputLinksAsync( string recordUri, MediaLinkResult result ) {
        ArgumentNullException.ThrowIfNull( result );
        if (string.IsNullOrWhiteSpace( recordUri )) {
            throw new ArgumentException( "Record URI cannot be null or empty", nameof( recordUri ) );
        }

        if (result.Results.Count == 0) {
            throw new ArgumentException( "MediaLinkResult must contain at least one provider result", nameof( result ) );
        }

        try {
            IDatabase db = _redis.GetDatabase( );
            string rkey = RecordKeyGenerator.GenerateRkey( result );
            string cardId = RecordKeyGenerator.GenerateCardId( rkey );
            TimeSpan expiry = TimeSpan.FromDays( _cacheDays );
            bool isAlbum = result.Results.Values.First( ).IsAlbum ?? false;

            // Check if this record already exists in cache with the same RecordUri
            // If so, just refresh TTL on existing keys and skip the full write
            string metaKey = $"{MetaPrefix}{rkey}";
            RedisValue existingMetaJson = await db.StringGetAsync( metaKey );
            if (!existingMetaJson.IsNullOrEmpty) {
                CacheEntryMetadata? existingMeta = JsonSerializer.Deserialize<CacheEntryMetadata>(
                    existingMetaJson.ToString( ),
                    _jsonOptions
                );

                if (existingMeta?.RecordUri == recordUri) {
                    // Record already cached with same URI - just refresh TTL on all keys
                    await RefreshTtlForRkeyAsync( db, rkey, expiry );
                    LogCacheEntryExists( _logger, rkey.SanitizeForLogging( ) );
                    return;
                }
            }

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

            // Verify the meta key was actually written (spot check)
            RedisValue verifyValue = await db.StringGetAsync( metaKey );
            if (verifyValue.IsNullOrEmpty) {
                LogWriteVerificationFailed( _logger, rkey.SanitizeForLogging( ) );
            }

            LogCreatedLookupEntries( _logger, rkey.SanitizeForLogging( ), cardId.SanitizeForLogging( ), allKeys.Count );
        } catch (Exception ex) {
            LogAddInputLinksError( _logger, ex );
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

            LogRemovedLookupKeys( _logger, existingKeys.Length, rkey.SanitizeForLogging( ) );
        }
    }

    /// <summary>
    /// Refreshes the TTL for all lookup keys associated with an rkey.
    /// This is used during bootstrap to extend TTL for records that already exist with the same RecordUri.
    /// </summary>
    private static async Task RefreshTtlForRkeyAsync( IDatabase db, string rkey, TimeSpan expiry ) {
        string keysSetKey = $"{KeysPrefix}{rkey}";
        RedisValue[] existingKeys = await db.SetMembersAsync( keysSetKey );

        if (existingKeys.Length == 0) {
            return;
        }

        // Refresh TTL on all keys associated with this rkey
        foreach (RedisValue key in existingKeys) {
            _ = await db.KeyExpireAsync( key.ToString( ), expiry );
        }

        // Also refresh TTL on the keys set itself
        _ = await db.KeyExpireAsync( keysSetKey, expiry );
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
            LogPdsLookupFailed( _logger, ex, externalId.SanitizeForLogging( ) );
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
                LogCacheHit( _logger, recordUri.SanitizeForLogging( ) );
                return CheckRecordFreshness( result, recordUri );
            }

            // Record not found on PDS - remove from Redis
            LogRecordNotFound( _logger, recordUri.SanitizeForLogging( ) );
        } catch (HttpRequestException ex) {
            LogHttpError( _logger, ex, ex.StatusCode );
        } catch (Exception ex) {
            LogPdsError( _logger, ex );
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
    /// Extracts the provider-specific ID from a service URL using shared parser utility.
    /// </summary>
    private string? ExtractProviderIdFromUrl( SupportedProviders provider, string url ) {
        try {
            return ProviderUrlParser.ExtractId( provider, url );
        } catch (Exception ex) {
            LogExtractProviderIdFailed( _logger, ex, provider );
            return null;
        }
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

    #region LoggerMessage Methods

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Cache.RedisMediaLinkCacheCachedResult,
        Level = LogLevel.Information,
        Message = "Cached result with rkey: {Rkey}, recordUri: {RecordUri}" )]
    internal static partial void LogCachedResult( ILogger logger, string rkey, string recordUri );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Cache.RedisMediaLinkCacheCacheError,
        Level = LogLevel.Error,
        Message = "Error while caching result" )]
    internal static partial void LogCacheError( ILogger logger, Exception ex );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Cache.RedisMediaLinkCacheEntryExists,
        Level = LogLevel.Debug,
        Message = "Cache entry already exists for rkey: {Rkey}, refreshed TTL" )]
    internal static partial void LogCacheEntryExists( ILogger logger, string rkey );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Cache.RedisMediaLinkCacheWriteVerificationFailed,
        Level = LogLevel.Error,
        Message = "Redis write verification FAILED for rkey: {Rkey} - meta key not found after write!" )]
    internal static partial void LogWriteVerificationFailed( ILogger logger, string rkey );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Cache.RedisMediaLinkCacheCreatedLookupEntries,
        Level = LogLevel.Information,
        Message = "Created Redis lookup entries for rkey: {Rkey}, cardId: {CardId}, {KeyCount} keys" )]
    internal static partial void LogCreatedLookupEntries( ILogger logger, string rkey, string cardId, int keyCount );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Cache.RedisMediaLinkCacheAddInputLinksError,
        Level = LogLevel.Error,
        Message = "Error while adding input links to Redis cache" )]
    internal static partial void LogAddInputLinksError( ILogger logger, Exception ex );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Cache.RedisMediaLinkCacheRemovedLookupKeys,
        Level = LogLevel.Information,
        Message = "Removed {KeyCount} existing lookup keys for rkey: {Rkey}" )]
    internal static partial void LogRemovedLookupKeys( ILogger logger, int keyCount, string rkey );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Cache.RedisMediaLinkCachePdsLookupFailed,
        Level = LogLevel.Warning,
        Message = "Failed to retrieve result from PDS by external ID: {ExternalId}" )]
    internal static partial void LogPdsLookupFailed( ILogger logger, Exception ex, string externalId );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Cache.RedisMediaLinkCacheCacheHit,
        Level = LogLevel.Information,
        Message = "Cache hit for RecordUri: {RecordUri}" )]
    internal static partial void LogCacheHit( ILogger logger, string recordUri );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Cache.RedisMediaLinkCacheRecordNotFound,
        Level = LogLevel.Warning,
        Message = "Record not found on PDS, will be cleaned up on TTL expiry: {RecordUri}" )]
    internal static partial void LogRecordNotFound( ILogger logger, string recordUri );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Cache.RedisMediaLinkCacheHttpError,
        Level = LogLevel.Error,
        Message = "HTTP error while trying to get cached result, StatusCode: {StatusCode}" )]
    internal static partial void LogHttpError( ILogger logger, Exception ex, HttpStatusCode? statusCode );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Cache.RedisMediaLinkCachePdsError,
        Level = LogLevel.Error,
        Message = "Error while trying to get cached result from PDS" )]
    internal static partial void LogPdsError( ILogger logger, Exception ex );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Cache.RedisMediaLinkCacheExtractProviderIdFailed,
        Level = LogLevel.Warning,
        Message = "Failed to extract provider ID from URL for {Provider}." )]
    internal static partial void LogExtractProviderIdFailed( ILogger logger, Exception ex, SupportedProviders provider );

    #endregion
}
