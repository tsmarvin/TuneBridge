using System.Text.Json;
using TuneBridge.Configuration;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Implementations.Auth;
using TuneBridge.Domain.Implementations.LinkParsers;
using TuneBridge.Domain.Interfaces;
using TuneBridge.Domain.Types.Bases;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Implementations.Services {

    /// <summary>
    /// An <see cref="IMusicLookupService"/> implementation for <see cref="SupportedProviders.SoundCloud"/>
    /// </summary>
    /// <param name="handler">The <see cref="SoundCloudTokenHandler"/> used to authenticate the API calls performed by the service. Added via dependency injection in <see cref="StartupExtensions.AddTuneBridgeServices"/></param>
    /// <param name="factory">The pre-configured HttpClientFactory used to perform the API calls for the service. Added via dependency injection in <see cref="StartupExtensions.AddTuneBridgeServices"/></param>
    /// <param name="logger">The logger used to record errors. Added via dependency injection in <see cref="StartupExtensions.AddTuneBridgeServices"/></param>
    /// <param name="serializerOptions">The Json Serializer Options used to record the body of the API results on error when using trace logging. Added via dependency injection in <see cref="StartupExtensions.AddTuneBridgeServices"/></param>
#pragma warning disable CS9113 // Parameter 'handler' is unread - kept for future OAuth implementation
    public sealed partial class SoundCloudLookupService(
        SoundCloudTokenHandler handler,
        IHttpClientFactory factory,
        ILogger<SoundCloudLookupService> logger,
        JsonSerializerOptions serializerOptions
    ) : MusicLookupServiceBase( logger, serializerOptions ), IMusicLookupService {
#pragma warning restore CS9113

        public override SupportedProviders Provider => SupportedProviders.SoundCloud;

        public override Task<MusicLookupResultDto?> GetInfoByISRCAsync( string isrc ) {
            // SoundCloud API does not support ISRC lookup directly
            Logger.LogDebug( "SoundCloud does not support ISRC lookup for: {isrc}", isrc );
            return Task.FromResult<MusicLookupResultDto?>( null );
        }

        public override Task<MusicLookupResultDto?> GetInfoByUPCAsync( string upc ) {
            // SoundCloud API does not support UPC lookup directly
            Logger.LogDebug( "SoundCloud does not support UPC lookup for: {upc}", upc );
            return Task.FromResult<MusicLookupResultDto?>( null );
        }

        public override async Task<MusicLookupResultDto?> GetInfoAsync( string title, string artist ) {
            string query = $"{artist} {title}";
            string? body = await NewMusicApiRequest(
                SoundCloudLinkParser.GetTrackSearchURI( query ),
                $"search for '{query}' "
            );

            return ParseSoundCloudSearchResponse( body, title, artist );
        }

        public override async Task<MusicLookupResultDto?> GetInfoAsync( string uri ) {
            if (SoundCloudLinkParser.TryParseUri( uri, out SoundCloudEntity kind, out string url )) {
                if (kind == SoundCloudEntity.Track) {
                    string? body = await NewMusicApiRequest(
                        SoundCloudLinkParser.GetResolveURI( url ),
                        UriLookupKey
                    );
                    return ParseSoundCloudResponse( body, UriLookupKey, kind, true );
                }
            }
            return null;
        }

        private protected override Task<HttpClient> CreateAuthenticatedClientAsync( ) {
            HttpClient client = factory.CreateClient("soundcloud-api");
            // SoundCloud API uses client_id query parameter for authentication
            // OAuth token is optional for public endpoints
            return Task.FromResult( client );
        }

        private MusicLookupResultDto? ParseSoundCloudSearchResponse(
            string? body,
            string title,
            string artist
        ) {
            if (string.IsNullOrWhiteSpace( body )) { return null; }
            
            try {
                using JsonDocument jsonDoc = JsonDocument.Parse(body);
                JsonElement root = jsonDoc.RootElement;
                
                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength( ) > 0) {
                    string sanitizedTitle = SanitizeSongTitle( title );
                    
                    // Try to find best match from search results
                    foreach (JsonElement track in root.EnumerateArray( )) {
                        if (track.TryGetProperty( "title", out JsonElement titleElement )) {
                            string trackTitle = titleElement.GetString( ) ?? string.Empty;
                            if (SanitizeSongTitle( trackTitle ).Equals( sanitizedTitle, StringComparison.InvariantCultureIgnoreCase )) {
                                return ParseSoundCloudResponse( track, "search result ", SoundCloudEntity.Track, null );
                            }
                        }
                    }
                    
                    // If no exact match, return first result
                    return ParseSoundCloudResponse( root[0], "search result ", SoundCloudEntity.Track, null );
                }
            } catch (Exception ex) {
                Logger.LogError( ex, "An error occurred while parsing the search json response from soundcloud." );
                Logger.LogTrace( JsonSerializer.Serialize( body, SerializerOptions ) );
            }
            
            return null;
        }

        private MusicLookupResultDto? ParseSoundCloudResponse(
            string? body,
            string lookupKey,
            SoundCloudEntity kind,
            bool? isPrimary
        ) {
            if (string.IsNullOrWhiteSpace( body )) { return null; }
            
            try {
                using JsonDocument jsonDoc = JsonDocument.Parse(body);
                return ParseSoundCloudResponse( jsonDoc.RootElement, lookupKey, kind, isPrimary );
            } catch (Exception ex) {
                Logger.LogError( ex, $"An error occurred while parsing the {lookupKey}json response from soundcloud." );
                Logger.LogTrace( JsonSerializer.Serialize( body, SerializerOptions ) );
            }
            
            return null;
        }

        private MusicLookupResultDto? ParseSoundCloudResponse(
            JsonElement element,
            string lookupKey,
            SoundCloudEntity kind,
            bool? isPrimary
        ) {
            bool isAlbum = kind == SoundCloudEntity.Playlist;
            MusicLookupResultDto result = new() {
                IsAlbum = isAlbum,
                IsPrimary = isPrimary ?? false
            };

            try {
                // Get track title
                result.Title = element.TryGetProperty( "title", out JsonElement titleElement )
                    ? titleElement.GetString( ) ?? string.Empty
                    : string.Empty;

                // Get artist/user name
                if (element.TryGetProperty( "user", out JsonElement userElement ) &&
                    userElement.TryGetProperty( "username", out JsonElement usernameElement )) {
                    result.Artist = usernameElement.GetString( ) ?? string.Empty;
                }

                // Get permalink URL
                result.URL = element.TryGetProperty( "permalink_url", out JsonElement urlElement )
                    ? urlElement.GetString( ) ?? string.Empty
                    : string.Empty;

                // Get artwork URL
                if (element.TryGetProperty( "artwork_url", out JsonElement artworkElement )) {
                    string artworkUrl = artworkElement.GetString( ) ?? string.Empty;
                    // SoundCloud returns artwork URLs with size qualifiers, get the largest version
                    result.ArtUrl = artworkUrl.Replace( "-large", "-t500x500" );
                }

                // SoundCloud doesn't provide ISRC/UPC in public API, leave ExternalId empty
                result.ExternalId = string.Empty;

                return result;
            } catch (Exception ex) {
                Logger.LogError( ex, $"An error occurred while parsing the {lookupKey}json response from soundcloud." );
                Logger.LogTrace( JsonSerializer.Serialize( element, SerializerOptions ) );
                return null;
            }
        }

    }
}
