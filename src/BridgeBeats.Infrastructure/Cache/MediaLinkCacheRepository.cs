using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Infrastructure.Cache.Entities;
using BridgeBeats.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BridgeBeats.Infrastructure.Cache;

/// <summary>
/// Implementation of <see cref="IMediaLinkCacheRepository"/> that uses SQLite to track ATProto PDS record locations
/// and ATProto PDS for persistent storage. The SQLite database is used only for efficient lookups; all actual
/// data is stored on and retrieved from the PDS.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="MediaLinkCacheRepository"/> class.
/// </remarks>
/// <param name="dbContextFactory">Factory for creating database contexts.</param>
/// <param name="atprotoStorage">Service for storing and retrieving results from ATProto PDS.</param>
/// <param name="logger">Logger for diagnostic information.</param>
/// <param name="cacheDays">Number of days to consider cache entries fresh.</param>
/// <param name="userDID">The user DID (Decentralized Identifier) that will post the records to the PDS.</param>
public class MediaLinkCacheRepository(
    IDbContextFactory<MediaLinkCacheDbContext> dbContextFactory,
    IATProtoStorageService atprotoStorage,
    ILogger<MediaLinkCacheRepository> logger,
    int cacheDays,
    string userDID
) : IMediaLinkCacheRepository {

    /// <inheritdoc/>
    public async Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultAsync( string inputLink ) {
        using MediaLinkCacheDbContext dbContext = dbContextFactory.CreateDbContext( );
        MediaLookupEntry? lookupEntry = dbContext
                                        .LookupEntries
                                        .Include( le => le.MediaLinkCacheEntry )
                                        .FirstOrDefault( le => le.LookupType == LookupEntryType.Url && le.LookupValue == inputLink );
        return lookupEntry is null || lookupEntry.MediaLinkCacheEntry is null
            ? null
            : await ValidatePDSRecordState( lookupEntry.MediaLinkCacheEntry );
    }

    /// <inheritdoc/>
    public async Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByISRCAsync( string isrc ) {
        using MediaLinkCacheDbContext dbContext = dbContextFactory.CreateDbContext( );
        MediaLookupEntry? lookupEntry = await dbContext
                                                .LookupEntries
                                                .Include( le => le.MediaLinkCacheEntry )
                                                .FirstOrDefaultAsync( le => le.LookupValue == isrc.Trim() &&
                                                                            le.LookupType == LookupEntryType.ExternalId &&
                                                                            !le.IsAlbum );
        return await TryGetPDSResultByExternalIDAsync( lookupEntry, isrc, false );
    }

    /// <inheritdoc/>
    public async Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByUPCAsync( string upc ) {
        using MediaLinkCacheDbContext dbContext = dbContextFactory.CreateDbContext( );
        MediaLookupEntry? lookupEntry = await dbContext
                                                .LookupEntries
                                                .Include( le => le.MediaLinkCacheEntry )
                                                .FirstOrDefaultAsync( le => le.LookupValue == upc.Trim() &&
                                                                            le.LookupType == LookupEntryType.ExternalId &&
                                                                            le.IsAlbum );
        return await TryGetPDSResultByExternalIDAsync( lookupEntry, upc, true );
    }

    private async Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetPDSResultByExternalIDAsync(
        MediaLookupEntry? lookupEntry,
        string externalID,
        bool isAlbum
    ) {
        if (lookupEntry is null || lookupEntry.MediaLinkCacheEntry is null) {
            string rkey = RecordKeyGenerator.GenerateRkey( externalID, isAlbum );
            string recordUri = $"at://did:plc:{userDID}/link.bridgebeats.lookup/{rkey}";
            MediaLinkResult? pdsResult = await atprotoStorage.GetMediaLinkResultAsync( recordUri ); ;
            if (pdsResult != null) {
                return await CheckRecordFreshness( pdsResult, await AddResultToLocalCacheAsync( pdsResult, recordUri ) );
            }
        }

        return (lookupEntry is null || lookupEntry.MediaLinkCacheEntry is null)
            ? null
            : await ValidatePDSRecordState( lookupEntry.MediaLinkCacheEntry );
    }

    /// <inheritdoc/>
    public async Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByMetadataAsync( string title, string artist ) {
        using MediaLinkCacheDbContext dbContext = dbContextFactory.CreateDbContext( );
        string metadataKey = $"{title.Trim().ToLowerInvariant()}|{artist.Trim().ToLowerInvariant()}"; // Build metadata key (same format as when storing)
        MediaLookupEntry? lookupEntry = await dbContext
                                                .LookupEntries
                                                .Include( le => le.MediaLinkCacheEntry )
                                                .FirstOrDefaultAsync( le => le.LookupValue == metadataKey &&
                                                                            le.LookupType == LookupEntryType.Metadata );
        return lookupEntry is null || lookupEntry.MediaLinkCacheEntry is null
            ? null
            : await ValidatePDSRecordState( lookupEntry.MediaLinkCacheEntry );
    }

    /// <inheritdoc/>
    public async Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByProviderIdAsync(
        string providerId,
        SupportedProviders provider,
        bool isAlbum
    ) {
        using MediaLinkCacheDbContext dbContext = dbContextFactory.CreateDbContext( );
        MediaProviderEntry? lookupEntry = await dbContext
                                                    .ProviderEntries
                                                    .Include( pe => pe.MediaLinkCacheEntry )
                                                    .FirstOrDefaultAsync( pe => pe.ProviderId == providerId.Trim() &&
                                                                                pe.Provider == provider );
        return lookupEntry is null || lookupEntry.MediaLinkCacheEntry is null
            ? null
            : await ValidatePDSRecordState( lookupEntry.MediaLinkCacheEntry );
    }

    /// <inheritdoc/>
    public async Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByCardIdAsync( string cardId ) {
        using MediaLinkCacheDbContext dbContext = dbContextFactory.CreateDbContext( );
        MediaLinkCacheEntry? cacheEntry = await dbContext
                                                    .CacheEntries
                                                    .FirstOrDefaultAsync( ce => ce.CardId == cardId );
        return cacheEntry is null
            ? null
            : await ValidatePDSRecordState( cacheEntry );
    }

    /// <inheritdoc/>
    public async Task<string> CacheResultAsync( MediaLinkResult result ) {
        try {
            // Store on ATProto PDS first (this creates/updates with deterministic rkey)
            return await AddResultToLocalCacheAsync( result, await atprotoStorage.StoreMediaLinkResultAsync( result ) );
        } catch (Exception ex) {
            logger.LogError( ex, "Error while caching result" );
            throw;
        }
    }

    /// <summary>
    /// Adds a result entry and its corresponding lookup values to the local cache.
    /// </summary>
    /// <param name="result">The result entry to add to the local cache</param>
    /// <param name="recordUri">The location on the PDS the record is saved to</param>
    /// <returns></returns>
    private async Task<string> AddResultToLocalCacheAsync( MediaLinkResult result, string recordUri ) {
        try {
            await AddInputLinksAsync( recordUri, result );
            return recordUri;
        } catch (Exception ex) {
            logger.LogError( ex, "Error while saving cache entry to local cache." );
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task AddInputLinksAsync( string recordUri, MediaLinkResult result ) {
        try {
            using MediaLinkCacheDbContext dbContext = dbContextFactory.CreateDbContext( );
            string rkey = RecordKeyGenerator.GenerateRkey( result );
            string cardId = RecordKeyGenerator.GenerateCardId( rkey );
            MediaLinkCacheEntry? existingEntry = await dbContext.CacheEntries.FirstOrDefaultAsync( ce => ce.Rkey == rkey );
            bool isAlbum = result.Results.Values.First( ).IsAlbum ?? false;
            if (existingEntry == null) {
                // Create new cache entry
                MediaLinkCacheEntry cacheEntry = new( ) {
                    Rkey = rkey,
                    CardId = cardId,
                    RecordUri = recordUri,
                    CreatedAt = DateTime.UtcNow,
                    LastLookedUpAt = DateTime.UtcNow
                };
                _ = dbContext.CacheEntries.Add( cacheEntry );
                logger.LogInformation( "Created new local lookup cache entry with rkey: {rkey} and cardId: {cardId}", rkey, cardId );
            } else {
                // Update existing entry
                existingEntry.LastLookedUpAt = DateTime.UtcNow;
                logger.LogInformation( "Updated existing local lookup cache entry with rkey: {rkey} and cardId: {cardId}", rkey, cardId );
            }
            _ = await dbContext.SaveChangesAsync( );
            await AddLinkLookupEntriesAsync( dbContext, rkey, result, isAlbum );
            logger.LogInformation( "Cached lookup entries associated with ATProto record: {uri}", recordUri );
        } catch (Exception ex) {
            logger.LogError( ex, "Error while caching result" );
            throw;
        }
    }

    /// <summary>
    /// Helper method to add all types of lookup entries for a cache entry.
    /// This includes URLs (from both user input and service results), external IDs, and metadata.
    /// </summary>
    private async Task AddLinkLookupEntriesAsync(
        MediaLinkCacheDbContext dbContext,
        string rkey,
        MediaLinkResult result,
        bool isAlbum
    ) {
        // Add service URLs from the result
        List<string> allUrls = [.. result._inputLinks];
        List<string> serviceUrls = [.. result
                                        .Results.Values
                                        .Where( r => !string.IsNullOrWhiteSpace( r.URL ) )
                                        .Select( r => r.URL )];
        allUrls.AddRange( serviceUrls );
        await AddLookupEntriesOfTypeAsync( dbContext, rkey, allUrls, LookupEntryType.Url, isAlbum );

        foreach (MusicLookupResult value in result.Results.Values) {
            // Add external ID if available
            await AddLookupEntriesOfTypeAsync( dbContext, rkey, [value.ExternalId], LookupEntryType.ExternalId, isAlbum );
            if (!(string.IsNullOrWhiteSpace( value.Title ) && string.IsNullOrWhiteSpace( value.Artist ))) {
                string metadataKey = $"{value.Title.Trim().ToLowerInvariant()}|{value.Artist.Trim().ToLowerInvariant()}";
                if (metadataKey == "|") { continue; }
                await AddLookupEntriesOfTypeAsync( dbContext, rkey, [metadataKey], LookupEntryType.Metadata, isAlbum );
            }
        }

        // Add provider-specific entries
        await AddProviderEntriesAsync( dbContext, rkey, result );
    }

    /// <summary>
    /// Helper method to add lookup entries of a specific type with conflict handling.
    /// Batches all adds and saves once to reduce database I/O.
    /// For URLs: stores the original un-normalized value for tracking, but uses normalized value for deduplication.
    /// </summary>
    private async Task AddLookupEntriesOfTypeAsync(
        MediaLinkCacheDbContext dbContext,
        string rkey,
        IEnumerable<string> lookupValues,
        LookupEntryType lookupType,
        bool isAlbum
    ) {
        List<string> cleanLookups = [.. lookupValues
                                        .Where( v => !string.IsNullOrWhiteSpace( v ) )
                                        .Select( v => v.Trim() )
                                        .Distinct( )
                                    ];
        if (cleanLookups.Count == 0) { return; }
        List<MediaLookupEntry> existingEntries = await dbContext
                                                        .LookupEntries
                                                        .Where( le => le.MediaLinkCacheEntryRkey == rkey
                                                                      && le.LookupType == lookupType
                                                                      && le.IsAlbum == isAlbum )
                                                        .ToListAsync( );

        // Remove duplicate entries
        foreach (MediaLookupEntry entry in existingEntries) {
            _ = cleanLookups.RemoveAll( v => v.Equals( entry.LookupValue, StringComparison.OrdinalIgnoreCase ) );
        }
        if (cleanLookups.Count == 0) { return; }

        // Create new entries for values that don't exist (store original values)
        List<MediaLookupEntry> newEntries = [.. cleanLookups
                                                .Select( cl => new MediaLookupEntry {
                                                    LookupValue             = cl,
                                                    LookupType              = lookupType,
                                                    IsAlbum                 = isAlbum,
                                                    MediaLinkCacheEntryRkey = rkey,
                                                    CreatedAt               = DateTime.UtcNow
                                                } )
                                            ];

        if (newEntries.Count > 0) {
            dbContext.LookupEntries.AddRange( newEntries );
            try {
                _ = await dbContext.SaveChangesAsync( );
            } catch (DbUpdateException ex) {
                // Log the conflict for diagnostics
                logger.LogWarning(
                    ex,
                    "A database error occurred when adding {EntryCount} lookup entries of type {LookupType}" +
                    " to cache entry {Rkey}. This typically occurs due to concurrent requests.",
                    cleanLookups.Count, lookupType, rkey
                );
                // Detach conflicting entries to avoid tracking issues
                foreach (MediaLookupEntry? entry in newEntries) {
                    dbContext.Entry( entry ).State = EntityState.Detached;
                }
            }
        }
    }

    /// <summary>
    /// Helper method to add provider-specific entries for each provider in the result.
    /// Extracts the provider ID from the service URLs and stores them for fast provider-based lookups.
    /// </summary>
    private async Task AddProviderEntriesAsync(
        MediaLinkCacheDbContext dbContext,
        string rkey,
        MediaLinkResult result
    ) {
        List<(SupportedProviders provider, string providerId)> providerIds = [];

        // Extract provider IDs from each result
        foreach ((SupportedProviders provider, MusicLookupResult dto) in result.Results) {
            string? providerId = ExtractProviderIdFromUrl( provider, dto.URL );
            if (!string.IsNullOrWhiteSpace( providerId )) {
                providerIds.Add( (provider, providerId) );
            }
        }

        if (providerIds.Count == 0) { return; }

        // Fetch existing provider entries to avoid conflicts
        List<MediaProviderEntry> existingEntries = await dbContext
                                                            .ProviderEntries
                                                            .Where( pe =>
                                                                providerIds
                                                                    .Select( p => p.provider )
                                                                    .Contains( pe.Provider ) &&
                                                                providerIds
                                                                    .Select( p => p.providerId )
                                                                    .Contains( pe.ProviderId )
                                                            )
                                                            .ToListAsync( );

        HashSet<(SupportedProviders, string)> existingSet = [.. existingEntries.Select( e => (e.Provider, e.ProviderId) )];

        // Create new entries for provider IDs that don't exist
        List<MediaProviderEntry> newEntries = [.. providerIds
                                                    .Where( p => !existingSet.Contains( p ) )
                                                    .Select( p => new MediaProviderEntry {
                                                                    Provider                = p.provider,
                                                                    ProviderId              = p.providerId,
                                                                    MediaLinkCacheEntryRkey = rkey,
                                                                    CreatedAt               = DateTime.UtcNow
                                                                } ) ];

        if (newEntries.Count > 0) {
            dbContext.ProviderEntries.AddRange( newEntries );
            try {
                _ = await dbContext.SaveChangesAsync( );
                logger.LogInformation(
                    "Associated {Count} provider entries to cache entry {Rkey}",
                    newEntries.Count, rkey
                );
            } catch (DbUpdateException ex) {
                logger.LogWarning(
                    ex,
                    "DBException when adding {EntryCount} provider entries to cache entry {Rkey}. " +
                    "This typically occurs due to concurrent requests.",
                    newEntries.Count, rkey
                );
                // Detach conflicting entries to avoid tracking issues
                foreach (MediaProviderEntry? entry in newEntries) {
                    dbContext.Entry( entry ).State = EntityState.Detached;
                }
            }
        }
    }

    /// <summary>
    /// Consistent method for validating the state of a cached PDS record.
    /// </summary>
    /// <param name="cacheEntry">The local cache entry pointer to the PDS record.</param>
    /// <returns>Either null or, if results are found, a tuple containing the media link result, record URI, and a stale flag.</returns>
    private async Task<(MediaLinkResult result, string recordUri, bool isStale)?> ValidatePDSRecordState( MediaLinkCacheEntry cacheEntry ) {
        try {
            // Fetch the actual result from ATProto PDS
            MediaLinkResult? result = await atprotoStorage.GetMediaLinkResultAsync( cacheEntry.RecordUri );

            (MediaLinkResult result, string recordUri, bool isStale)? cachehit = await CheckRecordFreshness( result, cacheEntry.RecordUri );

            if (cachehit is not null) {
                return cachehit;
            } else if (result is not null) {
                // Remove the cache entry if the record no longer exists on PDS
                using MediaLinkCacheDbContext dbContext = dbContextFactory.CreateDbContext( );
                logger.LogWarning( "Record not found on PDS, removing local cache entry: {uri}", cacheEntry.RecordUri );
                _ = dbContext.CacheEntries.Remove( cacheEntry );
                _ = await dbContext.SaveChangesAsync( );
            }
        } catch (HttpRequestException ex) {
            logger.LogError( ex, "HTTP error while trying to get cached result, StatusCode: {StatusCode}", ex.StatusCode );
        } catch (DbUpdateException ex) {
            logger.LogError( ex, "Database error while trying to get cached result" );
        } catch (InvalidOperationException ex) {
            logger.LogError( ex, "Invalid operation while trying to get cached result" );
        }
        return null;
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="result">The potential media link result to check for freshness.</param>
    /// <param name="recordUri">The recordUri to the result on the PDS.</param>
    /// <returns>Either null or, if results are found, a tuple containing the media link result, record URI, and a stale flag.</returns>
    private async Task<(MediaLinkResult result, string recordUri, bool isStale)?> CheckRecordFreshness( MediaLinkResult? result, string recordUri ) {
        if (result is not null) {
            // Check if the record needs to be refreshed (older than cache window)
            DateTime expirationDate = DateTime.UtcNow.AddDays( -cacheDays );
            bool isStale = result.LookedUpAt < expirationDate;

            logger.LogInformation( "Cache hit for RecordUri: {uri}", recordUri );
            return (result, recordUri, isStale);
        }
        return null;
    }

    /// <summary>
    /// Extracts the provider-specific ID from a service URL.
    /// </summary>
    /// <param name="provider">The music provider.</param>
    /// <param name="url">The service URL.</param>
    /// <returns>The provider ID, or null if it cannot be extracted.</returns>
    private string? ExtractProviderIdFromUrl( SupportedProviders provider, string url ) {
        if (string.IsNullOrWhiteSpace( url )) {
            return null;
        }

        try {
            return provider switch {
                // Apple Music URLs: https://music.apple.com/us/album/album-name/1234567890
                // or https://music.apple.com/us/album/album-name/1234567890?i=9876543210 (track in album)
                SupportedProviders.AppleMusic => ExtractAppleMusicId( url ),

                // Spotify URLs: https://open.spotify.com/track/3n3Ppam7vgaVa1iaRUc9Lp
                // or https://open.spotify.com/album/4aawyAB9vmqN3uQ7FjRGTy
                SupportedProviders.Spotify => ExtractSpotifyId( url ),

                // Tidal URLs: https://tidal.com/browse/track/123456789
                // or https://tidal.com/browse/album/123456789
                SupportedProviders.Tidal => ExtractTidalId( url ),

                _ => null
            };
        } catch (Exception ex) {
            logger.LogWarning( ex, "Failed to extract provider ID from URL for {Provider}.", provider );
            return null;
        }
    }

    /// <summary>
    /// Extracts the Apple Music catalog ID from a URL.
    /// Handles both album URLs and track-in-album URLs (with ?i= parameter).
    /// </summary>
    private static string? ExtractAppleMusicId( string url ) {
        // Check for track ID in query parameter (?i=trackId)
        if (url.Contains( "?i=" )) {
            int queryIndex = url.IndexOf( "?i=" );
            if (queryIndex > 0) {
                string trackId = url[ (queryIndex + 3)..].Split( '&' )[0];
                if (!string.IsNullOrWhiteSpace( trackId )) {
                    return trackId;
                }
            }
        }

        // Extract ID from path: /album/name/1234567890 or /song/name/1234567890
        string[] parts = url.Split( '/' );
        for (int i = 0; i < parts.Length; i++) {
            if ((parts[i].Equals( "album", StringComparison.OrdinalIgnoreCase ) ||
                 parts[i].Equals( "song", StringComparison.OrdinalIgnoreCase )) &&
                i + 2 < parts.Length) {
                string id = parts[i + 2].Split( '?' )[0]; // Remove query params
                if (!string.IsNullOrWhiteSpace( id ) && id.All( char.IsDigit )) {
                    return id;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the Spotify track or album ID from a URL.
    /// </summary>
    private static string? ExtractSpotifyId( string url ) {
        // URL format: https://open.spotify.com/track/3n3Ppam7vgaVa1iaRUc9Lp
        string[] parts = url.Split( '/' );
        for (int i = 0; i < parts.Length; i++) {
            if ((parts[i].Equals( "track", StringComparison.OrdinalIgnoreCase ) ||
                 parts[i].Equals( "album", StringComparison.OrdinalIgnoreCase )) &&
                i + 1 < parts.Length) {
                string id = parts[i + 1].Split( '?' )[0]; // Remove query params
                if (!string.IsNullOrWhiteSpace( id )) {
                    return id;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the Tidal track or album ID from a URL.
    /// </summary>
    private static string? ExtractTidalId( string url ) {
        // URL format: https://tidal.com/browse/track/123456789 or https://listen.tidal.com/track/123456789
        string[] parts = url.Split( '/' );
        for (int i = 0; i < parts.Length; i++) {
            if ((parts[i].Equals( "track", StringComparison.OrdinalIgnoreCase ) ||
                 parts[i].Equals( "album", StringComparison.OrdinalIgnoreCase )) &&
                i + 1 < parts.Length) {
                string id = parts[i + 1].Split( '?' )[0]; // Remove query params
                if (!string.IsNullOrWhiteSpace( id ) && id.All( char.IsDigit )) {
                    return id;
                }
            }
        }

        return null;
    }
}
