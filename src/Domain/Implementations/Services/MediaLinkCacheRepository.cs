using Microsoft.EntityFrameworkCore;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Contracts.Entities;
using TuneBridge.Domain.Implementations.Database;
using TuneBridge.Domain.Implementations.LinkParsers;
using TuneBridge.Domain.Implementations.Utilities;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Domain.Implementations.Services {

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
    public class MediaLinkCacheRepository(
        IDbContextFactory<MediaLinkCacheDbContext> dbContextFactory,
        IATProtoStorageService atprotoStorage,
        ILogger<MediaLinkCacheRepository> logger,
        int cacheDays
    ) : IMediaLinkCacheRepository {

        /// <inheritdoc/>
        public async Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultAsync( string inputLink ) {
            try {
                // Normalize the input link
                string normalizedLink = LinkNormalizer.Normalize( inputLink );

                using MediaLinkCacheDbContext dbContext = dbContextFactory.CreateDbContext( );

                // Check if we have a cache entry for this lookup value
                MediaLookupEntry? lookupEntry = await dbContext.LookupEntries
                    .Include( le => le.MediaLinkCacheEntry )
                    .FirstOrDefaultAsync( le => le.LookupValue == normalizedLink );

                if (lookupEntry?.MediaLinkCacheEntry is null) {
                    return null;
                }

                MediaLinkCacheEntry cacheEntry = lookupEntry.MediaLinkCacheEntry;

                // Fetch the actual result from ATProto PDS
                MediaLinkResult? result = await atprotoStorage.GetMediaLinkResultAsync( cacheEntry.RecordUri );

                if (result is null) {
                    logger.LogWarning( "Record not found on PDS, removing cache entry: {uri}", cacheEntry.RecordUri );
                    // Remove the cache entry if the record no longer exists on PDS
                    _ = dbContext.CacheEntries.Remove( cacheEntry );
                    _ = await dbContext.SaveChangesAsync( );
                    return null;
                }

                // Check if the record needs to be refreshed (older than cache window)
                DateTime expirationDate = DateTime.UtcNow.AddDays( -cacheDays );
                bool isStale = cacheEntry.LastLookedUpAt < expirationDate;

                if (isStale) {
                    logger.LogInformation( "Record is stale, needs refresh: {uri}", cacheEntry.RecordUri );
                } else {
                    logger.LogInformation( "Cache hit for RecordUri: {uri}", cacheEntry.RecordUri );
                }

                return (result, cacheEntry.RecordUri, isStale);
            } catch (Exception ex) {
                logger.LogError( ex, "Error while trying to get cached result" );
                return null;
            }
        }

        /// <inheritdoc/>
        public async Task<string> CacheResultAsync( MediaLinkResult result, IEnumerable<string> inputLinks ) {
            try {
                using MediaLinkCacheDbContext dbContext = dbContextFactory.CreateDbContext( );

                // Store on ATProto PDS first (this creates/updates with deterministic rkey)
                string recordUri = await atprotoStorage.StoreMediaLinkResultAsync( result );

                // Extract rkey from the result
                string rkey = RecordKeyGenerator.GenerateRkey( result );

                // Get first result to determine IsAlbum
                MusicLookupResultDto firstResult = result.Results.Values.First( );
                bool isAlbum = firstResult.IsAlbum == true;

                // Check if cache entry already exists for this rkey
                MediaLinkCacheEntry? existingEntry = await dbContext.CacheEntries
                    .FirstOrDefaultAsync( ce => ce.Rkey == rkey );

                if (existingEntry != null) {
                    // Update existing entry
                    existingEntry.RecordUri = recordUri;
                    existingEntry.LastLookedUpAt = DateTime.UtcNow;
                    _ = await dbContext.SaveChangesAsync( );

                    // Add new lookup entries
                    await AddLookupEntriesAsync( dbContext, rkey, inputLinks, result, isAlbum );

                    logger.LogInformation( "Updated existing cache entry with rkey: {rkey}", rkey );
                    return recordUri;
                }

                // Create new cache entry
                MediaLinkCacheEntry cacheEntry = new( ) {
                    Rkey = rkey,
                    RecordUri = recordUri,
                    CreatedAt = DateTime.UtcNow,
                    LastLookedUpAt = DateTime.UtcNow
                };

                _ = dbContext.CacheEntries.Add( cacheEntry );
                _ = await dbContext.SaveChangesAsync( );

                // Add lookup entries
                await AddLookupEntriesAsync( dbContext, rkey, inputLinks, result, isAlbum );

                logger.LogInformation( "Cached result with lookup entries to ATProto record: {uri} (rkey: {rkey})", recordUri, rkey );

                return recordUri;
            } catch (Exception ex) {
                logger.LogError( ex, "Error while caching result" );
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task UpdateCacheEntryAsync( string recordUri, MediaLinkResult result, IEnumerable<string> inputLinks ) {
            try {
                using MediaLinkCacheDbContext dbContext = dbContextFactory.CreateDbContext( );

                // Update the record on ATProto PDS
                bool updated = await atprotoStorage.UpdateMediaLinkResultAsync( recordUri, result );

                if (!updated) {
                    logger.LogWarning( "Failed to update PDS record: {uri}", recordUri );
                    return;
                }

                // Find the cache entry by record URI
                MediaLinkCacheEntry? cacheEntry = await dbContext.CacheEntries
                    .FirstOrDefaultAsync( ce => ce.RecordUri == recordUri );

                if (cacheEntry is null) {
                    logger.LogWarning( "Cache entry not found for record URI: {uri}", recordUri );
                    return;
                }

                // Update the LastLookedUpAt timestamp
                cacheEntry.LastLookedUpAt = DateTime.UtcNow;
                _ = await dbContext.SaveChangesAsync( );

                // Get first result to determine IsAlbum
                MusicLookupResultDto firstResult = result.Results.Values.First( );
                bool isAlbum = firstResult.IsAlbum == true;

                // Add any new lookup entries
                await AddLookupEntriesAsync( dbContext, cacheEntry.Rkey, inputLinks, result, isAlbum );

                logger.LogInformation( "Updated cache entry with fresh lookup: {uri}", recordUri );
            } catch (Exception ex) {
                logger.LogError( ex, "Error while updating cache entry for record URI: {uri}", recordUri );
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task AddInputLinksAsync( string recordUri, IEnumerable<string> newLinks ) {
            try {
                using MediaLinkCacheDbContext dbContext = dbContextFactory.CreateDbContext( );

                // Find the cache entry by record URI
                MediaLinkCacheEntry? cacheEntry = await dbContext.CacheEntries
                    .FirstOrDefaultAsync( ce => ce.RecordUri == recordUri );

                if (cacheEntry is null) {
                    logger.LogWarning( "Cache entry not found for record URI: {uri}", recordUri );
                    return;
                }

                // Fetch the result from PDS to get IsAlbum info
                MediaLinkResult? result = await atprotoStorage.GetMediaLinkResultAsync( recordUri );
                if (result is null) {
                    logger.LogWarning( "Result not found on PDS for record URI: {uri}", recordUri );
                    return;
                }

                MusicLookupResultDto firstResult = result.Results.Values.First( );
                bool isAlbum = firstResult.IsAlbum == true;

                // Add new lookup entries (user input links only)
                await AddUserInputLookupEntriesAsync( dbContext, cacheEntry.Rkey, newLinks, isAlbum );

                logger.LogInformation( "Added new input links to cache entry: {uri}", recordUri );
            } catch (Exception ex) {
                logger.LogError( ex, "Error while adding input links for record URI: {uri}", recordUri );
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByISRCAsync( string isrc ) {
            try {
                using MediaLinkCacheDbContext dbContext = dbContextFactory.CreateDbContext( );

                // Lookup by external ID with IsAlbum = false (ISRC is for tracks)
                MediaLookupEntry? lookupEntry = await dbContext.LookupEntries
                    .Include( le => le.MediaLinkCacheEntry )
                    .FirstOrDefaultAsync( le => le.LookupValue == isrc.Trim() &&
                                                le.LookupType == LookupEntryType.ExternalId &&
                                                le.IsAlbum == false );

                if (lookupEntry?.MediaLinkCacheEntry is null) {
                    return null;
                }

                MediaLinkCacheEntry cacheEntry = lookupEntry.MediaLinkCacheEntry;

                // Fetch the actual result from ATProto PDS
                MediaLinkResult? result = await atprotoStorage.GetMediaLinkResultAsync( cacheEntry.RecordUri );

                if (result is null) {
                    logger.LogWarning( "Record not found on PDS, removing cache entry: {uri}", cacheEntry.RecordUri );
                    _ = dbContext.CacheEntries.Remove( cacheEntry );
                    _ = await dbContext.SaveChangesAsync( );
                    return null;
                }

                // Check if the record needs to be refreshed
                DateTime expirationDate = DateTime.UtcNow.AddDays( -cacheDays );
                bool isStale = cacheEntry.LastLookedUpAt < expirationDate;

                logger.LogInformation( isStale ? "ISRC cache hit (stale): {uri}" : "ISRC cache hit: {uri}", cacheEntry.RecordUri );

                return (result, cacheEntry.RecordUri, isStale);
            } catch (Exception ex) {
                logger.LogError( ex, "Error while trying to get cached result by ISRC" );
                return null;
            }
        }

        /// <inheritdoc/>
        public async Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByUPCAsync( string upc ) {
            try {
                using MediaLinkCacheDbContext dbContext = dbContextFactory.CreateDbContext( );

                // Lookup by external ID with IsAlbum = true (UPC is for albums)
                MediaLookupEntry? lookupEntry = await dbContext.LookupEntries
                    .Include( le => le.MediaLinkCacheEntry )
                    .FirstOrDefaultAsync( le => le.LookupValue == upc.Trim() &&
                                                le.LookupType == LookupEntryType.ExternalId &&
                                                le.IsAlbum == true );

                if (lookupEntry?.MediaLinkCacheEntry is null) {
                    return null;
                }

                MediaLinkCacheEntry cacheEntry = lookupEntry.MediaLinkCacheEntry;

                // Fetch the actual result from ATProto PDS
                MediaLinkResult? result = await atprotoStorage.GetMediaLinkResultAsync( cacheEntry.RecordUri );

                if (result is null) {
                    logger.LogWarning( "Record not found on PDS, removing cache entry: {uri}", cacheEntry.RecordUri );
                    _ = dbContext.CacheEntries.Remove( cacheEntry );
                    _ = await dbContext.SaveChangesAsync( );
                    return null;
                }

                // Check if the record needs to be refreshed
                DateTime expirationDate = DateTime.UtcNow.AddDays( -cacheDays );
                bool isStale = cacheEntry.LastLookedUpAt < expirationDate;

                logger.LogInformation( isStale ? "UPC cache hit (stale): {uri}" : "UPC cache hit: {uri}", cacheEntry.RecordUri );

                return (result, cacheEntry.RecordUri, isStale);
            } catch (Exception ex) {
                logger.LogError( ex, "Error while trying to get cached result by UPC" );
                return null;
            }
        }

        /// <inheritdoc/>
        public async Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByMetadataAsync( string title, string artist ) {
            try {
                using MediaLinkCacheDbContext dbContext = dbContextFactory.CreateDbContext( );

                // Build metadata key (same format as when storing)
                string metadataKey = $"{title.Trim().ToLowerInvariant()}|{artist.Trim().ToLowerInvariant()}";

                // Lookup by metadata
                MediaLookupEntry? lookupEntry = await dbContext.LookupEntries
                    .Include( le => le.MediaLinkCacheEntry )
                    .FirstOrDefaultAsync( le => le.LookupValue == metadataKey &&
                                                le.LookupType == LookupEntryType.Metadata );

                if (lookupEntry?.MediaLinkCacheEntry is null) {
                    return null;
                }

                MediaLinkCacheEntry cacheEntry = lookupEntry.MediaLinkCacheEntry;

                // Fetch the actual result from ATProto PDS
                MediaLinkResult? result = await atprotoStorage.GetMediaLinkResultAsync( cacheEntry.RecordUri );

                if (result is null) {
                    logger.LogWarning( "Record not found on PDS, removing cache entry: {uri}", cacheEntry.RecordUri );
                    _ = dbContext.CacheEntries.Remove( cacheEntry );
                    _ = await dbContext.SaveChangesAsync( );
                    return null;
                }

                // Check if the record needs to be refreshed
                DateTime expirationDate = DateTime.UtcNow.AddDays( -cacheDays );
                bool isStale = cacheEntry.LastLookedUpAt < expirationDate;

                logger.LogInformation( isStale ? "Metadata cache hit (stale): {uri}" : "Metadata cache hit: {uri}", cacheEntry.RecordUri );

                return (result, cacheEntry.RecordUri, isStale);
            } catch (Exception ex) {
                logger.LogError( ex, "Error while trying to get cached result by metadata" );
                return null;
            }
        }

        /// <summary>
        /// Helper method to add all types of lookup entries for a cache entry.
        /// This includes user input links, service links, external IDs, and metadata.
        /// </summary>
        private async Task AddLookupEntriesAsync( MediaLinkCacheDbContext dbContext, string rkey, IEnumerable<string> userInputLinks, MediaLinkResult result, bool isAlbum ) {
            // Add user input links
            await AddUserInputLookupEntriesAsync( dbContext, rkey, userInputLinks, isAlbum );

            // Extract and add service links from the result
            List<string> serviceLinks = [];
            foreach (MusicLookupResultDto resultDto in result.Results.Values) {
                if (!string.IsNullOrWhiteSpace( resultDto.URL )) {
                    serviceLinks.Add( resultDto.URL );
                }
            }
            await AddLookupEntriesOfTypeAsync( dbContext, rkey, serviceLinks, LookupEntryType.ServiceLink, isAlbum );

            // Add external ID if available
            MusicLookupResultDto? firstResultWithId = result.Results.Values
                .FirstOrDefault( r => !string.IsNullOrWhiteSpace( r.ExternalId ) );
            if (firstResultWithId != null) {
                await AddLookupEntriesOfTypeAsync( dbContext, rkey, [firstResultWithId.ExternalId], LookupEntryType.ExternalId, isAlbum );
            }

            // Add metadata lookup (title|artist)
            MusicLookupResultDto firstResult = result.Results.Values.First( );
            if (!string.IsNullOrWhiteSpace( firstResult.Title ) || !string.IsNullOrWhiteSpace( firstResult.Artist )) {
                string metadataKey = $"{firstResult.Title?.Trim().ToLowerInvariant() ?? ""}|{firstResult.Artist?.Trim().ToLowerInvariant() ?? ""}";
                await AddLookupEntriesOfTypeAsync( dbContext, rkey, [metadataKey], LookupEntryType.Metadata, isAlbum );
            }
        }

        /// <summary>
        /// Helper method to add user input lookup entries only.
        /// </summary>
        private async Task AddUserInputLookupEntriesAsync( MediaLinkCacheDbContext dbContext, string rkey, IEnumerable<string> links, bool isAlbum ) {
            await AddLookupEntriesOfTypeAsync( dbContext, rkey, links, LookupEntryType.UserInput, isAlbum );
        }

        /// <summary>
        /// Helper method to add lookup entries of a specific type with conflict handling.
        /// Batches all adds and saves once to reduce database I/O.
        /// </summary>
        private async Task AddLookupEntriesOfTypeAsync( MediaLinkCacheDbContext dbContext, string rkey, IEnumerable<string> lookupValues, LookupEntryType lookupType, bool isAlbum ) {
            List<string> normalizedValues = [.. lookupValues
                .Select( v => lookupType is LookupEntryType.UserInput or LookupEntryType.ServiceLink
                    ? LinkNormalizer.Normalize( v )
                    : v.Trim() )
                .Where( v => !string.IsNullOrEmpty( v ) )
                .Distinct( )];

            if (normalizedValues.Count == 0) {
                return;
            }

            // Fetch existing entries to avoid conflicts
            List<MediaLookupEntry> existingEntries = await dbContext.LookupEntries
                .Where( le => normalizedValues.Contains( le.LookupValue )
                    && le.LookupType == lookupType
                    && le.IsAlbum == isAlbum )
                .ToListAsync( );

            HashSet<string> existingValueSet = new( existingEntries.Select( e => e.LookupValue ), StringComparer.OrdinalIgnoreCase );

            // Create new entries for values that don't exist
            List<MediaLookupEntry> newEntries = [.. normalizedValues
                .Where( v => !existingValueSet.Contains( v ) )
                .Select( v => new MediaLookupEntry {
                    LookupValue = v,
                    LookupType = lookupType,
                    IsAlbum = isAlbum,
                    MediaLinkCacheEntryRkey = rkey,
                    CreatedAt = DateTime.UtcNow
                } )];

            if (newEntries.Count > 0) {
                dbContext.LookupEntries.AddRange( newEntries );
                try {
                    _ = await dbContext.SaveChangesAsync( );
                } catch (DbUpdateException ex) {
                    // Log the conflict for diagnostics
                    logger.LogWarning( ex, "Unique constraint violation when adding {EntryCount} lookup entries of type {LookupType} to cache entry {Rkey}. This typically occurs due to concurrent requests.",
                        normalizedValues.Count, lookupType, rkey );

                    // Detach conflicting entries to avoid tracking issues
                    foreach (MediaLookupEntry? entry in newEntries) {
                        dbContext.Entry( entry ).State = EntityState.Detached;
                    }
                }
            }
        }
    }
}
