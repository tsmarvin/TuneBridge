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
    /// </summary>
    /// <remarks>
    /// IMPORTANT - QUOTA OPTIMIZATION REQUIRED:
    /// 
    /// YouTube Data API has a default quota of 10,000 units per day. Current implementation costs:
    /// - Search query (GetInfoAsync with title/artist): 100 units per call
    /// - Video details lookup: 1 unit per call
    /// - Playlist details lookup: 1 unit per call
    /// 
    /// Without optimization, this service can only handle ~100 cross-platform lookups per day (100 units × 100 = 10,000).
    /// 
    /// RECOMMENDED OPTIMIZATIONS:
    /// 
    /// 1. IN-MEMORY CACHING (Priority: HIGH)
    ///    - Cache search results for artist/title combinations for 1 hour
    ///    - Use normalized cache keys (lowercase, trimmed) to maximize hit rate
    ///    - Cache both successful and failed lookups to prevent retry storms
    ///    - Expected impact: 70-80% cache hit rate = ~3x capacity increase
    ///    - Implementation: ConcurrentDictionary with DateTimeOffset expiration
    /// 
    /// 2. QUOTA USAGE TRACKING (Priority: MEDIUM)
    ///    - Track approximate daily quota consumption
    ///    - Log warnings when approaching limits (e.g., at 8,000 units)
    ///    - Reset counter daily at midnight UTC
    ///    - Log each API call with unit cost for visibility
    /// 
    /// 3. SMART RESULT REUSE (Priority: LOW)
    ///    - When search returns results, extract metadata directly from snippet
    ///    - Avoid second API call to get video/playlist details if data is sufficient
    ///    - Saves 1 unit per lookup when applicable
    /// 
    /// 4. RATE LIMITING / CIRCUIT BREAKER (Priority: LOW)
    ///    - Implement circuit breaker pattern to pause lookups when quota exhausted
    ///    - Return null gracefully instead of making API calls that will fail
    ///    - Resume automatically after quota reset
    /// 
    /// Without these optimizations, this service may not be viable for production use in high-traffic scenarios.
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

        public override SupportedProviders Provider => SupportedProviders.YouTubeMusic;

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
            string searchQuery = $"{artist} {title}";
            string? body = await NewMusicApiRequest(
                YouTubeMusicLinkParser.GetSearchUri( searchQuery ) + $"&key={apiKey}",
                $"search for '{searchQuery}' "
            );

            return ParseYouTubeSearchResponse( body, false );
        }

        public override async Task<MusicLookupResultDto?> GetInfoAsync( string uri ) {
            if (YouTubeMusicLinkParser.TryParseUri( uri, out YouTubeMusicLinkParser.YouTubeMusicEntity kind, out string id )) {
                if (kind == YouTubeMusicLinkParser.YouTubeMusicEntity.Video) {
                    string? body = await NewMusicApiRequest(
                        YouTubeMusicLinkParser.GetVideoDetailsUri( id ) + $"&key={apiKey}",
                        SongLookupKey
                    );
                    return ParseYouTubeVideoResponse( body, id, true );
                } else if (kind == YouTubeMusicLinkParser.YouTubeMusicEntity.Playlist) {
                    string? body = await NewMusicApiRequest(
                        YouTubeMusicLinkParser.GetPlaylistDetailsUri( id ) + $"&key={apiKey}",
                        AlbumLookupKey
                    );
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
