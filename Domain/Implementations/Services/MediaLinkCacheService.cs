using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Contracts.Entities;
using TuneBridge.Domain.Implementations.Database;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Domain.Implementations.Services {

    /// <summary>
    /// Implementation of <see cref="IMediaLinkCacheService"/> that uses SQLite for caching
    /// and Bluesky PDS for persistent storage.
    /// </summary>
    public class MediaLinkCacheService : IMediaLinkCacheService {

        private readonly MediaLinkCacheDbContext _dbContext;
        private readonly IBlueskyStorageService _blueskyStorage;
        private readonly ILogger<MediaLinkCacheService> _logger;
        private readonly JsonSerializerOptions _serializerOptions;
        private readonly int _cacheDays;

        public MediaLinkCacheService(
            MediaLinkCacheDbContext dbContext,
            IBlueskyStorageService blueskyStorage,
            ILogger<MediaLinkCacheService> logger,
            JsonSerializerOptions serializerOptions,
            int cacheDays
        ) {
            _dbContext = dbContext;
            _blueskyStorage = blueskyStorage;
            _logger = logger;
            _serializerOptions = serializerOptions;
            _cacheDays = cacheDays;
        }

        /// <inheritdoc/>
        public async Task<(MediaLinkResult result, string recordUri)?> TryGetCachedResultAsync( string inputLink ) {
            try {
                // Normalize the input link
                string normalizedLink = NormalizeLink( inputLink );

                // Check if we have a cached entry for this input link
                var inputLinkEntry = await _dbContext.InputLinks
                    .Include( il => il.MediaLinkCacheEntry )
                    .FirstOrDefaultAsync( il => il.Link == normalizedLink );

                if (inputLinkEntry?.MediaLinkCacheEntry is null) {
                    return null;
                }

                var cacheEntry = inputLinkEntry.MediaLinkCacheEntry;

                // Check if the cache entry is still valid
                var expirationDate = DateTime.UtcNow.AddDays( -_cacheDays );
                if (cacheEntry.LastAccessedAt < expirationDate) {
                    _logger.LogInformation( "Cache entry expired for link: {link}", normalizedLink );
                    return null;
                }

                // Update last accessed time
                cacheEntry.LastAccessedAt = DateTime.UtcNow;
                _ = await _dbContext.SaveChangesAsync( );

                // Deserialize the cached result
                var result = JsonSerializer.Deserialize<MediaLinkResult>( cacheEntry.SerializedResult, _serializerOptions );

                if (result is null) {
                    _logger.LogWarning( "Failed to deserialize cached result for link: {link}", normalizedLink );
                    return null;
                }

                _logger.LogInformation( "Cache hit for link: {link}", normalizedLink );

                return (result, cacheEntry.RecordUri);
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

                // Serialize the result for SQLite storage
                string serializedResult = JsonSerializer.Serialize( result, _serializerOptions );

                // Create cache entry
                var cacheEntry = new MediaLinkCacheEntry {
                    RecordUri = recordUri,
                    SerializedResult = serializedResult,
                    CreatedAt = DateTime.UtcNow,
                    LastAccessedAt = DateTime.UtcNow
                };

                _ = _dbContext.CacheEntries.Add( cacheEntry );
                _ = await _dbContext.SaveChangesAsync( );

                // Add input links
                var normalizedLinks = inputLinks.Select( NormalizeLink ).Distinct( );
                foreach (string link in normalizedLinks) {
                    // Check if the link already exists
                    bool exists = await _dbContext.InputLinks.AnyAsync( il => il.Link == link );
                    if (!exists) {
                        var inputLinkEntry = new InputLinkEntry {
                            Link = link,
                            MediaLinkCacheEntryId = cacheEntry.Id,
                            CreatedAt = DateTime.UtcNow
                        };
                        _ = _dbContext.InputLinks.Add( inputLinkEntry );
                    }
                }

                _ = await _dbContext.SaveChangesAsync( );

                _logger.LogInformation( "Cached result with {linkCount} input links to Bluesky record: {uri}",
                    normalizedLinks.Count( ), recordUri );

                return recordUri;
            } catch (Exception ex) {
                _logger.LogError( ex, "Error while caching result" );
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

                // Add new input links
                var normalizedLinks = newLinks.Select( NormalizeLink ).Distinct( );
                foreach (string link in normalizedLinks) {
                    // Check if the link already exists
                    bool exists = await _dbContext.InputLinks.AnyAsync( il => il.Link == link );
                    if (!exists) {
                        var inputLinkEntry = new InputLinkEntry {
                            Link = link,
                            MediaLinkCacheEntryId = cacheEntry.Id,
                            CreatedAt = DateTime.UtcNow
                        };
                        _ = _dbContext.InputLinks.Add( inputLinkEntry );
                    }
                }

                _ = await _dbContext.SaveChangesAsync( );

                _logger.LogInformation( "Added {linkCount} new input links to cache entry: {uri}",
                    normalizedLinks.Count( ), recordUri );
            } catch (Exception ex) {
                _logger.LogError( ex, "Error while adding input links for record URI: {uri}", recordUri );
                throw;
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
