using System.Collections.Concurrent;
using System.Text.Json;
using TuneBridge.Configuration;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Implementations.LinkParsers;
using TuneBridge.Domain.Interfaces;
using TuneBridge.Domain.Types.Bases;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Implementations.Services {

    /// <summary>
    /// An <see cref="IMusicLookupService"/> implementation for <see cref="SupportedProviders.YouTubeMusic"/>
    /// with built-in caching to minimize YouTube Data API quota usage (default: 10,000 units/day).
    /// </summary>
    /// <remarks>
    /// YouTube Data API quota costs:
    /// - Search query: 100 units
    /// - Video details: 1 unit
    /// - Playlist details: 1 unit
    /// 
    /// This implementation uses an in-memory cache to avoid redundant API calls for the same content.
    /// Cache entries expire after 1 hour to balance freshness with quota conservation.
    /// </remarks>
    /// <param name="apiKey">The YouTube Data API v3 API key for authentication.</param>
    /// <param name="factory">The pre-configured HttpClientFactory used to perform the API calls for the service. Added via dependency injection in <see cref="StartupExtensions.AddTuneBridgeServices"/></param>
    /// <param name="logger">The logger used to record errors. Added via dependency injection in <see cref="StartupExtensions.AddTuneBridgeServices"/></param>
    /// <param name="serializerOptions">The Json Serializer Options used to record the body of the API results on error when using trace logging. Added via dependency injection in <see cref="StartupExtensions.AddTuneBridgeServices"/></param>
    public sealed partial class YouTubeMusicLookupService(
        string apiKey,
        IHttpClientFactory factory,
        ILogger<YouTubeMusicLookupService> logger,
        JsonSerializerOptions serializerOptions
    ) : MusicLookupServiceBase( logger, serializerOptions ), IMusicLookupService {

        // Cache to reduce API quota usage - expires after 1 hour
        private static readonly ConcurrentDictionary<string, CacheEntry> _searchCache = new();
        private static readonly TimeSpan _cacheExpiration = TimeSpan.FromHours( 1 );
        private static readonly Timer _cacheCleanupTimer;
        
        // Track approximate quota usage for monitoring
        private static long _approximateQuotaUsed;
        private static DateTimeOffset _quotaResetTime = DateTimeOffset.UtcNow.Date.AddDays( 1 );

        static YouTubeMusicLookupService( ) {
            // Clean up expired cache entries every 15 minutes
            _cacheCleanupTimer = new Timer( 
                callback: _ => CleanupExpiredCacheEntries(),
                state: null,
                dueTime: TimeSpan.FromMinutes( 15 ),
                period: TimeSpan.FromMinutes( 15 )
            );
        }

        public override SupportedProviders Provider => SupportedProviders.YouTubeMusic;

        /// <summary>
        /// Removes expired entries from the cache to prevent unbounded memory growth.
        /// </summary>
        private static void CleanupExpiredCacheEntries( ) {
            foreach (KeyValuePair<string, CacheEntry> entry in _searchCache) {
                if (entry.Value.IsExpired) {
                    _searchCache.TryRemove( entry.Key, out _ );
                }
            }
        }

        /// <summary>
        /// Tracks API quota usage and resets daily counter.
        /// </summary>
        private void TrackQuotaUsage( int units, string operation ) {
            // Reset quota counter if it's a new day
            if (DateTimeOffset.UtcNow >= _quotaResetTime) {
                Interlocked.Exchange( ref _approximateQuotaUsed, 0 );
                _quotaResetTime = DateTimeOffset.UtcNow.Date.AddDays( 1 );
                Logger.LogInformation( "YouTube Data API quota counter reset for new day" );
            }

            long newTotal = Interlocked.Add( ref _approximateQuotaUsed, units );
            Logger.LogDebug( "YouTube API quota: +{units} units for {operation} (daily total: ~{total}/10000)", units, operation, newTotal );
            
            // Warn if approaching quota limit
            if (newTotal > 8000 && newTotal - units <= 8000) {
                Logger.LogWarning( "YouTube Data API quota usage is high: ~{total}/10000 units used today", newTotal );
            }
        }

        /// <summary>
        /// Represents a cached search result with expiration.
        /// </summary>
        private sealed class CacheEntry {
            public MusicLookupResultDto? Result { get; init; }
            public DateTimeOffset ExpiresAt { get; init; }
            
            public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
        }

        public override Task<MusicLookupResultDto?> GetInfoByISRCAsync( string isrc ) {
            // YouTube Data API doesn't support ISRC lookup directly
            // This would require searching and matching, which is unreliable
            Logger.LogDebug( "YouTube Music does not support ISRC lookup" );
            return Task.FromResult<MusicLookupResultDto?>( null );
        }

        public override Task<MusicLookupResultDto?> GetInfoByUPCAsync( string upc ) {
            // YouTube Data API doesn't support UPC lookup directly
            Logger.LogDebug( "YouTube Music does not support UPC lookup" );
            return Task.FromResult<MusicLookupResultDto?>( null );
        }

        public override async Task<MusicLookupResultDto?> GetInfoAsync( string title, string artist ) {
            // Create a cache key from normalized title and artist
            string cacheKey = $"{NormalizeForCache( artist )}|{NormalizeForCache( title )}";
            
            // Check cache first to avoid expensive search query (100 units)
            if (_searchCache.TryGetValue( cacheKey, out CacheEntry? cached ) && !cached.IsExpired) {
                Logger.LogDebug( "YouTube Music cache hit for '{artist} - {title}' (saved 100 quota units)", artist, title );
                return cached.Result;
            }

            // Remove expired entry if present
            if (cached?.IsExpired == true) {
                _searchCache.TryRemove( cacheKey, out _ );
            }

            Logger.LogDebug( "YouTube Music performing search for '{artist} - {title}'", artist, title );
            
            string searchQuery = $"{artist} {title}";
            string? body = await NewMusicApiRequest(
                YouTubeMusicLinkParser.GetSearchUri( searchQuery ) + $"&key={apiKey}",
                $"search for '{searchQuery}' "
            );

            // Track quota usage - search costs 100 units
            TrackQuotaUsage( 100, $"search '{artist} - {title}'" );

            MusicLookupResultDto? result = ParseYouTubeSearchResponse( body, false );
            
            // Cache the result (even if null) to avoid repeated failed searches
            _searchCache[cacheKey] = new CacheEntry {
                Result = result,
                ExpiresAt = DateTimeOffset.UtcNow.Add( _cacheExpiration )
            };
            
            return result;
        }

        /// <summary>
        /// Normalizes a string for use as a cache key by converting to lowercase and removing extra whitespace.
        /// </summary>
        private static string NormalizeForCache( string input ) {
            return string.Join( " ", input.Trim().ToLowerInvariant().Split( ' ', StringSplitOptions.RemoveEmptyEntries ) );
        }

        public override async Task<MusicLookupResultDto?> GetInfoAsync( string uri ) {
            if (YouTubeMusicLinkParser.TryParseUri( uri, out YouTubeMusicLinkParser.YouTubeMusicEntity kind, out string id )) {
                if (kind == YouTubeMusicLinkParser.YouTubeMusicEntity.Video) {
                    string? body = await NewMusicApiRequest(
                        YouTubeMusicLinkParser.GetVideoDetailsUri( id ) + $"&key={apiKey}",
                        SongLookupKey
                    );
                    TrackQuotaUsage( 1, $"video details for {id}" );
                    return ParseYouTubeVideoResponse( body, id, true );
                } else if (kind == YouTubeMusicLinkParser.YouTubeMusicEntity.Playlist) {
                    string? body = await NewMusicApiRequest(
                        YouTubeMusicLinkParser.GetPlaylistDetailsUri( id ) + $"&key={apiKey}",
                        AlbumLookupKey
                    );
                    TrackQuotaUsage( 1, $"playlist details for {id}" );
                    return ParseYouTubePlaylistResponse( body, id, true );
                }
            }
            return null;
        }

        private protected override Task<HttpClient> CreateAuthenticatedClientAsync( ) {
            HttpClient client = factory.CreateClient("youtube-api");
            return Task.FromResult( client );
        }

        private MusicLookupResultDto? ParseYouTubeSearchResponse( string? body, bool isPrimary ) {
            if (string.IsNullOrWhiteSpace( body )) { return null; }

            try {
                using JsonDocument jsonDoc = JsonDocument.Parse(body);
                JsonElement root = jsonDoc.RootElement;

                if (root.TryGetProperty( "items", out JsonElement items ) && items.GetArrayLength() > 0) {
                    JsonElement firstItem = items.EnumerateArray().First();
                    if (firstItem.TryGetProperty( "id", out JsonElement idObj ) &&
                        idObj.TryGetProperty( "videoId", out JsonElement videoId )) {
                        string id = videoId.GetString() ?? string.Empty;
                        return ParseYouTubeVideoFromSnippet( firstItem, id, isPrimary );
                    }
                }
            } catch (Exception ex) {
                Logger.LogError( ex, "An error occurred while parsing the YouTube search response." );
                Logger.LogTrace( JsonSerializer.Serialize( body, SerializerOptions ) );
            }
            return null;
        }

        private MusicLookupResultDto? ParseYouTubeVideoResponse( string? body, string videoId, bool isPrimary ) {
            if (string.IsNullOrWhiteSpace( body )) { return null; }

            try {
                using JsonDocument jsonDoc = JsonDocument.Parse(body);
                JsonElement root = jsonDoc.RootElement;

                if (root.TryGetProperty( "items", out JsonElement items ) && items.GetArrayLength() > 0) {
                    JsonElement firstItem = items.EnumerateArray().First();
                    return ParseYouTubeVideoFromSnippet( firstItem, videoId, isPrimary );
                }
            } catch (Exception ex) {
                Logger.LogError( ex, "An error occurred while parsing the YouTube video response." );
                Logger.LogTrace( JsonSerializer.Serialize( body, SerializerOptions ) );
            }
            return null;
        }

        private MusicLookupResultDto? ParseYouTubeVideoFromSnippet( JsonElement item, string videoId, bool isPrimary ) {
            try {
                if (!item.TryGetProperty( "snippet", out JsonElement snippet )) {
                    return null;
                }

                string title = snippet.GetProperty( "title" ).GetString() ?? string.Empty;
                string channelTitle = snippet.GetProperty( "channelTitle" ).GetString() ?? string.Empty;
                
                // Try to extract artist and song from title (common format: "Artist - Song")
                string artist = channelTitle;
                string songTitle = title;
                
                if (title.Contains( " - " )) {
                    string[] parts = title.Split( " - ", 2 );
                    if (parts.Length == 2) {
                        artist = parts[0].Trim();
                        songTitle = parts[1].Trim();
                    }
                }

                string artUrl = string.Empty;
                if (snippet.TryGetProperty( "thumbnails", out JsonElement thumbnails )) {
                    if (thumbnails.TryGetProperty( "high", out JsonElement highThumb )) {
                        artUrl = highThumb.GetProperty( "url" ).GetString() ?? string.Empty;
                    } else if (thumbnails.TryGetProperty( "default", out JsonElement defaultThumb )) {
                        artUrl = defaultThumb.GetProperty( "url" ).GetString() ?? string.Empty;
                    }
                }

                return new MusicLookupResultDto {
                    IsAlbum = false,
                    IsPrimary = isPrimary,
                    Title = songTitle,
                    Artist = artist,
                    URL = $"https://music.youtube.com/watch?v={videoId}",
                    ArtUrl = artUrl,
                    ExternalId = string.Empty // YouTube doesn't provide ISRC/UPC
                };
            } catch (Exception ex) {
                Logger.LogError( ex, "An error occurred while parsing YouTube video snippet." );
                return null;
            }
        }

        private MusicLookupResultDto? ParseYouTubePlaylistResponse( string? body, string playlistId, bool isPrimary ) {
            if (string.IsNullOrWhiteSpace( body )) { return null; }

            try {
                using JsonDocument jsonDoc = JsonDocument.Parse(body);
                JsonElement root = jsonDoc.RootElement;

                if (root.TryGetProperty( "items", out JsonElement items ) && items.GetArrayLength() > 0) {
                    JsonElement firstItem = items.EnumerateArray().First();
                    if (!firstItem.TryGetProperty( "snippet", out JsonElement snippet )) {
                        return null;
                    }

                    string title = snippet.GetProperty( "title" ).GetString() ?? string.Empty;
                    string channelTitle = snippet.GetProperty( "channelTitle" ).GetString() ?? string.Empty;

                    string artUrl = string.Empty;
                    if (snippet.TryGetProperty( "thumbnails", out JsonElement thumbnails )) {
                        if (thumbnails.TryGetProperty( "high", out JsonElement highThumb )) {
                            artUrl = highThumb.GetProperty( "url" ).GetString() ?? string.Empty;
                        } else if (thumbnails.TryGetProperty( "default", out JsonElement defaultThumb )) {
                            artUrl = defaultThumb.GetProperty( "url" ).GetString() ?? string.Empty;
                        }
                    }

                    return new MusicLookupResultDto {
                        IsAlbum = true,
                        IsPrimary = isPrimary,
                        Title = title,
                        Artist = channelTitle,
                        URL = $"https://music.youtube.com/playlist?list={playlistId}",
                        ArtUrl = artUrl,
                        ExternalId = string.Empty // YouTube doesn't provide ISRC/UPC
                    };
                }
            } catch (Exception ex) {
                Logger.LogError( ex, "An error occurred while parsing the YouTube playlist response." );
                Logger.LogTrace( JsonSerializer.Serialize( body, SerializerOptions ) );
            }
            return null;
        }
    }
}
