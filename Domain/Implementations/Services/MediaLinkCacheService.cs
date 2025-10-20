using Microsoft.EntityFrameworkCore;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Contracts.Entities;
using TuneBridge.Domain.Implementations.Database;
using TuneBridge.Domain.Interfaces;

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
                string normalizedLink = NormalizeLink( inputLink );

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
        /// </summary>
        private async Task AddLinksToEntryAsync( int cacheEntryId, IEnumerable<string> links ) {
            var normalizedLinks = links.Select( NormalizeLink ).Distinct( );
            foreach (string link in normalizedLinks) {
                var inputLinkEntry = new InputLinkEntry {
                    Link = link,
                    MediaLinkCacheEntryId = cacheEntryId,
                    CreatedAt = DateTime.UtcNow
                };
                
                try {
                    _ = _dbContext.InputLinks.Add( inputLinkEntry );
                    _ = await _dbContext.SaveChangesAsync( );
                } catch (DbUpdateException) {
                    // Link already exists (unique constraint violation), which is fine
                    // Detach the entity to prevent tracking issues
                    _dbContext.Entry( inputLinkEntry ).State = EntityState.Detached;
                }
            }
        }

        /// <summary>
        /// Normalizes a link by removing protocol and trailing slashes for consistent comparison.
        /// </summary>
        private static string NormalizeLink( string link ) {
            if (string.IsNullOrWhiteSpace( link )) {
                return string.Empty;
            }

            // Remove protocol (http:// or https://)
            string normalized = link.Trim( );
            if (normalized.StartsWith( "https://", StringComparison.OrdinalIgnoreCase )) {
                normalized = normalized[8..];
            } else if (normalized.StartsWith( "http://", StringComparison.OrdinalIgnoreCase )) {
                normalized = normalized[7..];
            }

            // Remove trailing slash
            normalized = normalized.TrimEnd( '/' );

            return normalized.ToLowerInvariant( );
        }
    }
}
