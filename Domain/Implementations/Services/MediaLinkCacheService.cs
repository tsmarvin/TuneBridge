using Microsoft.EntityFrameworkCore;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Contracts.Entities;
using TuneBridge.Domain.Implementations.Database;
using TuneBridge.Domain.Interfaces;
using TuneBridge.Domain.Utils;

namespace TuneBridge.Domain.Implementations.Services {

    /// <summary>
    /// Implementation of <see cref="IMediaLinkCacheService"/> that uses SQLite to track Bluesky PDS record locations
    /// and Bluesky PDS for persistent storage. The SQLite database is used only for efficient lookups; all actual
    /// data is stored on and retrieved from the PDS.
    /// </summary>
    public class MediaLinkCacheService : IMediaLinkCacheService {

        private readonly MediaLinkCacheDbContext _dbContext;
        private readonly IBlueskyStorageService _blueskyStorage;
        private readonly ILogger<MediaLinkCacheService> _logger;
        private readonly int _cacheDays;

        public MediaLinkCacheService(
            MediaLinkCacheDbContext dbContext,
            IBlueskyStorageService blueskyStorage,
            ILogger<MediaLinkCacheService> logger,
            int cacheDays
        ) {
            _dbContext = dbContext;
            _blueskyStorage = blueskyStorage;
            _logger = logger;
            _cacheDays = cacheDays;
        }

        /// <inheritdoc/>
        public async Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultAsync( string inputLink ) {
            try {
                // Normalize the input link
                string normalizedLink = LinkNormalizer.Normalize( inputLink );

                // Check if we have a cache entry for this input link
                var inputLinkEntry = await _dbContext.InputLinks
                    .Include( il => il.MediaLinkCacheEntry )
                    .FirstOrDefaultAsync( il => il.Link == normalizedLink );

                if (inputLinkEntry?.MediaLinkCacheEntry is null) {
                    return null;
                }

                var cacheEntry = inputLinkEntry.MediaLinkCacheEntry;

                // Fetch the actual result from Bluesky PDS
                var result = await _blueskyStorage.GetMediaLinkResultAsync( cacheEntry.RecordUri );

                if (result is null) {
                    _logger.LogWarning( "Record not found on PDS, removing cache entry: {uri}", cacheEntry.RecordUri );
                    // Remove the cache entry if the record no longer exists on PDS
                    _ = _dbContext.CacheEntries.Remove( cacheEntry );
                    _ = await _dbContext.SaveChangesAsync( );
                    return null;
                }

                // Check if the record needs to be refreshed (older than cache window)
                var expirationDate = DateTime.UtcNow.AddDays( -_cacheDays );
                bool isStale = cacheEntry.LastLookedUpAt < expirationDate;
                
                if (isStale) {
                    _logger.LogInformation( "Record is stale, needs refresh: {uri}", cacheEntry.RecordUri );
                } else {
                    _logger.LogInformation( "Cache hit for link: {link}", normalizedLink );
                }

                return (result, cacheEntry.RecordUri, isStale);
            } catch (Exception ex) {
                _logger.LogError( ex, "Error while trying to get cached result for link: {link}", inputLink );
                return null;
            }
        }

        /// <inheritdoc/>
        public async Task<string> CacheResultAsync( MediaLinkResult result, IEnumerable<string> inputLinks ) {
            try {
                // Store on Bluesky PDS first
                string recordUri = await _blueskyStorage.StoreMediaLinkResultAsync( result );

                // Create cache entry (without storing the result data in SQLite)
                var cacheEntry = new MediaLinkCacheEntry {
                    RecordUri = recordUri,
                    CreatedAt = DateTime.UtcNow,
                    LastLookedUpAt = DateTime.UtcNow
                };

                _ = _dbContext.CacheEntries.Add( cacheEntry );
                _ = await _dbContext.SaveChangesAsync( );

                // Add input links with conflict handling
                await AddLinksToEntryAsync( cacheEntry.Id, inputLinks );

                _logger.LogInformation( "Cached result with input links to Bluesky record: {uri}", recordUri );

                return recordUri;
            } catch (Exception ex) {
                _logger.LogError( ex, "Error while caching result" );
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task UpdateCacheEntryAsync( string recordUri, MediaLinkResult result, IEnumerable<string> inputLinks ) {
            try {
                // Update the record on Bluesky PDS
                bool updated = await _blueskyStorage.UpdateMediaLinkResultAsync( recordUri, result );
                
                if (!updated) {
                    _logger.LogWarning( "Failed to update PDS record: {uri}", recordUri );
                    return;
                }

                // Find the cache entry by record URI
                var cacheEntry = await _dbContext.CacheEntries
                    .FirstOrDefaultAsync( ce => ce.RecordUri == recordUri );

                if (cacheEntry is null) {
                    _logger.LogWarning( "Cache entry not found for record URI: {uri}", recordUri );
                    return;
                }

                // Update the LastLookedUpAt timestamp
                cacheEntry.LastLookedUpAt = DateTime.UtcNow;
                _ = await _dbContext.SaveChangesAsync( );

                // Add any new input links
                await AddLinksToEntryAsync( cacheEntry.Id, inputLinks );

                _logger.LogInformation( "Updated cache entry with fresh lookup: {uri}", recordUri );
            } catch (Exception ex) {
                _logger.LogError( ex, "Error while updating cache entry for record URI: {uri}", recordUri );
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task AddInputLinksAsync( string recordUri, IEnumerable<string> newLinks ) {
            try {
                // Find the cache entry by record URI
                var cacheEntry = await _dbContext.CacheEntries
                    .FirstOrDefaultAsync( ce => ce.RecordUri == recordUri );

                if (cacheEntry is null) {
                    _logger.LogWarning( "Cache entry not found for record URI: {uri}", recordUri );
                    return;
                }

                // Add new input links to the SQLite cache for lookup
                // Note: Input links are NOT stored on PDS for user privacy
                await AddLinksToEntryAsync( cacheEntry.Id, newLinks );

                _logger.LogInformation( "Added new input links to cache entry: {uri}", recordUri );
            } catch (Exception ex) {
                _logger.LogError( ex, "Error while adding input links for record URI: {uri}", recordUri );
                throw;
            }
        }

        /// <summary>
        /// Helper method to add links to a cache entry with conflict handling.
        /// Batches all adds and saves once to reduce database I/O.
        /// </summary>
        private async Task AddLinksToEntryAsync( int cacheEntryId, IEnumerable<string> links ) {
            var normalizedLinks = links.Select( LinkNormalizer.Normalize ).Distinct( ).ToList( );
            
            if (normalizedLinks.Count == 0) {
                return;
            }

            // Fetch existing links to avoid conflicts
            var existingLinks = await _dbContext.InputLinks
                .Where( il => normalizedLinks.Contains( il.Link ) )
                .Select( il => il.Link )
                .ToListAsync( );

            var existingLinkSet = new HashSet<string>( existingLinks, StringComparer.OrdinalIgnoreCase );

            // Create new entries for links that don't exist
            var newEntries = normalizedLinks
                .Where( link => !existingLinkSet.Contains( link ) )
                .Select( link => new InputLinkEntry {
                    Link = link,
                    MediaLinkCacheEntryId = cacheEntryId,
                    CreatedAt = DateTime.UtcNow
                } )
                .ToList( );

            if (newEntries.Count > 0) {
                _dbContext.InputLinks.AddRange( newEntries );
                try {
                    _ = await _dbContext.SaveChangesAsync( );
                } catch (DbUpdateException) {
                    // If we still hit a unique constraint (race condition), detach all entries
                    foreach (var entry in newEntries) {
                        _dbContext.Entry( entry ).State = EntityState.Detached;
                    }
                }
            }
        }
    }
}
