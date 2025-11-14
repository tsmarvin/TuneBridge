using Microsoft.EntityFrameworkCore;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Contracts.Entities;
using TuneBridge.Domain.Implementations.Database;
using TuneBridge.Domain.Implementations.LinkParsers;
using TuneBridge.Domain.Implementations.Utilities;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Domain.Implementations.Services {

    /// <summary>
    /// Implementation of <see cref="IMediaLinkCacheService"/> that uses SQLite to track ATProto PDS record locations
    /// and ATProto PDS for persistent storage. The SQLite database is used only for efficient lookups; all actual
    /// data is stored on and retrieved from the PDS.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="MediaLinkCacheService"/> class.
    /// </remarks>
    /// <param name="dbContextFactory">Factory for creating database contexts.</param>
    /// <param name="atprotoStorage">Service for storing and retrieving results from ATProto PDS.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="cacheDays">Number of days to consider cache entries fresh.</param>
    public class MediaLinkCacheService(
        IDbContextFactory<MediaLinkCacheDbContext> dbContextFactory,
        IATProtoStorageService atprotoStorage,
        ILogger<MediaLinkCacheService> logger,
        int cacheDays
    ) : IMediaLinkCacheService {

        /// <inheritdoc/>
        public async Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultAsync( string inputLink ) {
            try {
                // Normalize the input link
                string normalizedLink = LinkNormalizer.Normalize( inputLink );

                using MediaLinkCacheDbContext dbContext = dbContextFactory.CreateDbContext( );

                // Check if we have a cache entry for this input link
                InputLinkEntry? inputLinkEntry = await dbContext.InputLinks
                    .Include( il => il.MediaLinkCacheEntry )
                    .FirstOrDefaultAsync( il => il.Link == normalizedLink );

                if (inputLinkEntry?.MediaLinkCacheEntry is null) {
                    return null;
                }

                MediaLinkCacheEntry cacheEntry = inputLinkEntry.MediaLinkCacheEntry;

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

                // Extract rkey from the recordUri (format: at://did:plc:xxx/collection/rkey)
                string? rkey = RecordKeyGenerator.GenerateRkey( result );
                if (rkey == null) {
                    logger.LogWarning( "Cannot cache result without deterministic rkey" );
                    return recordUri;
                }

                // Check if cache entry already exists for this rkey
                MediaLinkCacheEntry? existingEntry = await dbContext.CacheEntries
                    .FirstOrDefaultAsync( ce => ce.Rkey == rkey );

                if (existingEntry != null) {
                    // Update existing entry
                    existingEntry.RecordUri = recordUri;
                    existingEntry.LastLookedUpAt = DateTime.UtcNow;
                    _ = await dbContext.SaveChangesAsync( );

                    // Add new input links
                    await AddLinksToEntryAsync( dbContext, existingEntry.Id, inputLinks );

                    logger.LogInformation( "Updated existing cache entry with rkey: {rkey}", rkey );
                    return recordUri;
                }

                // Create new cache entry (without storing the result data in SQLite)
                MediaLinkCacheEntry cacheEntry = new( ) {
                    Rkey = rkey,
                    RecordUri = recordUri,
                    CreatedAt = DateTime.UtcNow,
                    LastLookedUpAt = DateTime.UtcNow
                };

                _ = dbContext.CacheEntries.Add( cacheEntry );
                _ = await dbContext.SaveChangesAsync( );

                // Add input links with conflict handling
                await AddLinksToEntryAsync( dbContext, cacheEntry.Id, inputLinks );

                logger.LogInformation( "Cached result with input links to ATProto record: {uri} (rkey: {rkey})", recordUri, rkey );

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

                // Add any new input links
                await AddLinksToEntryAsync( dbContext, cacheEntry.Id, inputLinks );

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

                // Add new input links to the SQLite cache for lookup
                // Note: Input links are NOT stored on PDS for user privacy
                await AddLinksToEntryAsync( dbContext, cacheEntry.Id, newLinks );

                logger.LogInformation( "Added new input links to cache entry: {uri}", recordUri );
            } catch (Exception ex) {
                logger.LogError( ex, "Error while adding input links for record URI: {uri}", recordUri );
                throw;
            }
        }

        /// <summary>
        /// Helper method to add links to a cache entry with conflict handling.
        /// Batches all adds and saves once to reduce database I/O.
        /// </summary>
        private async Task AddLinksToEntryAsync( MediaLinkCacheDbContext dbContext, int cacheEntryId, IEnumerable<string> links ) {
            List<string> normalizedLinks = [.. links
                .Select( LinkNormalizer.Normalize )
                .Where( link => !string.IsNullOrEmpty( link ) ) // Filter out empty/null normalized links
                .Distinct( )];

            if (normalizedLinks.Count == 0) {
                return;
            }

            // Fetch existing links to avoid conflicts
            List<string> existingLinks = await dbContext.InputLinks
                .Where( il => normalizedLinks.Contains( il.Link ) )
                .Select( il => il.Link )
                .ToListAsync( );

            HashSet<string> existingLinkSet = new( existingLinks, StringComparer.OrdinalIgnoreCase );

            // Create new entries for links that don't exist
            List<InputLinkEntry> newEntries = [.. normalizedLinks
                .Where( link => !existingLinkSet.Contains( link ) )
                .Select( link => new InputLinkEntry {
                    Link = link,
                    MediaLinkCacheEntryId = cacheEntryId,
                    CreatedAt = DateTime.UtcNow
                } )];

            if (newEntries.Count > 0) {
                dbContext.InputLinks.AddRange( newEntries );
                try {
                    _ = await dbContext.SaveChangesAsync( );
                } catch (DbUpdateException ex) {
                    // Log the conflict for diagnostics (without logging user input for security)
                    logger.LogWarning( ex, "Unique constraint violation when adding {LinkCount} input links to cache entry {CacheEntryId}. This typically occurs due to concurrent requests for the same links.",
                        normalizedLinks.Count, cacheEntryId );

                    // Detach conflicting entries to avoid tracking issues
                    foreach (InputLinkEntry? entry in newEntries) {
                        dbContext.Entry( entry ).State = EntityState.Detached;
                    }

                    // Could optionally re-query and retry only non-conflicting links, but for now we accept the race condition
                    // as the links are likely already in the database from the concurrent request
                }
            }
        }
    }
}
