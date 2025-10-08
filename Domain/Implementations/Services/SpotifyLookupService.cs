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
    /// An <see cref="IMusicLookupService"/> implementation for <see cref="SupportedProviders.Spotify"/>
    /// </summary>
    /// <param name="handler">The <see cref="SpotifyTokenHandler"/> used to authenticate the API calls performed by the service. Added via dependency injection in <see cref="StartupExtensions.AddTuneBridgeServices"/></param>
    /// <param name="factory">The pre-configured HttpClientFactory used to perform the API calls for the service. Added via dependency injection in <see cref="StartupExtensions.AddTuneBridgeServices"/></param>
    /// <param name="logger">The logger used to record errors. Added via dependency injection in <see cref="StartupExtensions.AddTuneBridgeServices"/></param>
    /// <param name="serializerOptions">The Json Serializer Options used to record the body of the API results on error when using trace logging. Added via dependency injection in <see cref="StartupExtensions.AddTuneBridgeServices"/></param>
    public sealed partial class SpotifyLookupService(
        SpotifyTokenHandler handler,
        IHttpClientFactory factory,
        ILogger<SpotifyLookupService> logger,
        JsonSerializerOptions serializerOptions
    ) : MusicLookupServiceBase( logger, serializerOptions ), IMusicLookupService {

        public override SupportedProviders Provider => SupportedProviders.Spotify;

        public override async Task<MusicLookupResultDto?> GetInfoByISRCAsync( string isrc )
            => ParseSpotifyResponse(
                await NewMusicApiRequest( SpotifyLinkParser.GetTracksIsrcURI( isrc ), IsrcLookupKey ),
                IsrcLookupKey,
                SpotifyEntity.Track,
                null
            );

        public override async Task<MusicLookupResultDto?> GetInfoByUPCAsync( string upc )
            => ParseSpotifyResponse(
                await NewMusicApiRequest( SpotifyLinkParser.GetAlbumUpcURI( upc ), UpcLookupKey ),
                UpcLookupKey,
                SpotifyEntity.Album,
                null
            );

        public override async Task<MusicLookupResultDto?> GetInfoAsync( string title, string artist ) {
            List<(string id, string artistName)>? artistResults = ParseSpotifyArtistList(
                await NewMusicApiRequest(
                    SpotifyLinkParser.GetArtistSearchUri(artist),
                    ArtistLookupKey
                )
            );

            // Bail out early if we have no results
            if (artistResults == null || artistResults.Count == 0) { return null; }

            string sanitizedAlbumTitle = SanitizeAlbumTitle( title );
            foreach ((string id, string artistName) in artistResults) {
                string lookupKey = $"albums for artist {artistName} {id} ";

                List<(string id, string albumName)> artistAlbumIds = [];
                foreach ((string albumId, MusicLookupResultDto album) in ParseArtistAlbumLists(
                    await NewMusicApiRequest( SpotifyLinkParser.GetArtistAlbumsURI( id ), lookupKey ),
                    lookupKey
                )) {
                    if (ValidateSanitizedAlbumTitle( album, sanitizedAlbumTitle )) {
                        // The Artist Album endpoint doesn't return the albums with external Id's (UPC's) included.
                        // So we'll perform another direct lookup to get the UPC.
                        string? body = await NewMusicApiRequest( SpotifyLinkParser.GetAlbumIdURI( albumId ), lookupKey );

                        // This really shouldnt happen, but if something goes wrong we can return what we've already matched.
                        if (string.IsNullOrWhiteSpace( body )) { return album; }

                        using JsonDocument jsonDoc = JsonDocument.Parse( body );
                        return ParseSpotifyResponse(
                            jsonDoc.RootElement,
                            lookupKey,
                            SpotifyEntity.Album,
                            null
                        );
                    } else {
                        artistAlbumIds.Add( (albumId, album.Title) );
                    }
                }

                // If we couldnt match the artist + album combination, try searching for the
                // individual tracks on those albums instead.
                string sanitizedSongTitle = SanitizeSongTitle( title );
                foreach ((string albumId, string albumName) in artistAlbumIds) {
                    string trackLookup = $"tracks for artist {artistName} album {albumName} id#{albumId} ";

                    MusicLookupResultDto? result = await ParseAlbumTrackListsAsync(
                        await NewMusicApiRequest(SpotifyLinkParser.GetAlbumTracksURI(albumId), trackLookup),
                        sanitizedSongTitle,
                        trackLookup
                    );
                    if (result != null) { return result; }
                }
            }

            return null;
        }

        public override async Task<MusicLookupResultDto?> GetInfoAsync( string uri ) {
            if (SpotifyLinkParser.TryParseUri( uri, out SpotifyEntity kind, out string id )) {
                if (kind == SpotifyEntity.Album) {
                    string? body = await NewMusicApiRequest( $"albums/{id}", AlbumLookupKey );
                    if (body != null) {
                        using JsonDocument jsonDoc = JsonDocument.Parse(body);
                        return ParseSpotifyResponse(
                            jsonDoc.RootElement,
                            AlbumLookupKey,
                            kind,
                            true
                        );
                    }
                } else if (kind == SpotifyEntity.Track) {
                    return ParseSpotifyResponse(
                        await NewMusicApiRequest( $"tracks/{id}", SongLookupKey ),
                        SongLookupKey,
                        kind,
                        true
                    );
                }
            }
            return null;
        }

        private protected override async Task<HttpClient> CreateAuthenticatedClientAsync( ) {
            HttpClient client = factory.CreateClient("spotify-api");
            client.DefaultRequestHeaders.Authorization = await handler.NewBearerAuthenticationHeader( );
            return client;
        }

        private MusicLookupResultDto? ParseSpotifyResponse(
            string? body,
            string lookupKey,
            SpotifyEntity kind,
            bool? isPrimary
        ) {
            if (string.IsNullOrWhiteSpace( body )) { return null; }
            try {
                using JsonDocument jsonDoc = JsonDocument.Parse(body);
                if (CanParseJsonElement( jsonDoc.RootElement, out JsonElement element )) {
                    return ParseSpotifyResponse( element, lookupKey, kind, isPrimary );
                }
            } catch (Exception ex) {
                Logger.LogError( ex, $"An error occurred while parsing the {lookupKey}json response from spotify." );
                Logger.LogTrace( JsonSerializer.Serialize( body, SerializerOptions ) );
            }
            return null;
        }

        private static bool CanParseJsonElement( JsonElement root, out JsonElement output ) {
            output = root;

            if (root.TryGetProperty( "tracks", out JsonElement trackProps )) {
                output = trackProps;
            } else if (root.TryGetProperty( "albums", out JsonElement albumProps )) {
                output = albumProps;
            }

            if (output.TryGetProperty( "items", out JsonElement items )) {
                output = items;
            }

            if (output.ValueKind == JsonValueKind.Array) {
                if (output.GetArrayLength( ) > 0) {
                    output = output.EnumerateArray( ).First( );
                } else {
                    return false;
                }
            }

            return true;
        }

        private static string GetArtistName( JsonElement element ) {
            string result = string.Empty;
            if (element.TryGetProperty( "artists", out JsonElement artistsProps )) {
                if (artistsProps.GetArrayLength( ) > 0) {
                    result = artistsProps
                                .EnumerateArray( )
                                .First( )
                                .GetProperty( "name" )
                                .GetString( ) ?? string.Empty;
                }
            }
            return result;
        }

        private MusicLookupResultDto? ParseSpotifyResponse(
            JsonElement element,
            string lookupKey,
            SpotifyEntity kind,
            bool? isPrimary
        ) {
            bool isAlbum = kind == SpotifyEntity.Album;
            MusicLookupResultDto result = new() {
                IsAlbum = isAlbum,
                IsPrimary = isPrimary ?? false
            };

            try {
                result.Artist = GetArtistName( element );

                result.Title = element
                                .GetProperty( "name" )
                                .GetString( ) ?? string.Empty;

                result.ExternalId = GetExternalIdFromJson( element, isAlbum );

                result.URL = element
                                .GetProperty( "external_urls" )
                                .GetProperty( "spotify" )
                                .GetString( ) ?? string.Empty;

                switch (kind) {
                    case SpotifyEntity.Album:
                        result.ArtUrl = GetAlbumArtUrl( element );
                        break;
                    case SpotifyEntity.Track:
                        if (element.TryGetProperty( "album", out JsonElement albumProps )) {
                            result.ArtUrl = GetAlbumArtUrl( albumProps );
                        }
                        break;
                }

                return result;
            } catch (Exception ex) {
                Logger.LogError( ex, $"An error occurred while parsing the {lookupKey}json response from spotify." );
                Logger.LogTrace( JsonSerializer.Serialize( element, SerializerOptions ) );
                return null;
            }
        }

        private static string GetAlbumArtUrl( JsonElement element ) {
            if (element.TryGetProperty( "images", out JsonElement imagesProps )) {
                if (imagesProps.GetArrayLength( ) > 0 && imagesProps
                                .EnumerateArray( )
                                .First( )
                                .TryGetProperty( "url", out JsonElement urlProps )
                ) {
                    return urlProps.GetString( ) ?? string.Empty;
                }
            }
            return string.Empty;
        }

        private List<(string id, string artistName)>? ParseSpotifyArtistList( string? body ) {
            if (body == null) { return null; }

            List<(string id, string artistName)> results = [];
            try {
                using JsonDocument jsonDoc = JsonDocument.Parse(body);
                JsonElement root = jsonDoc.RootElement;
                if (root.TryGetProperty( "artists", out JsonElement artistsProps ) &&
                    artistsProps.TryGetProperty( "items", out JsonElement itemsProps )
                ) {
                    if (itemsProps.GetArrayLength( ) == 0) { return null; }

                    foreach (JsonElement artist in itemsProps.EnumerateArray( )) {
                        results.Add(
                            (artist.GetProperty( "id" ).GetString( )!,
                            artist.GetProperty( "name" ).GetString( )!)
                        );
                    }
                    return results;
                }
                return null;
            } catch (Exception ex) {
                Logger.LogError( ex, $"An error occurred while parsing the artist list json response from spotify." );
                Logger.LogTrace( JsonSerializer.Serialize( body, SerializerOptions ) );
                return null;
            }
        }

        private IEnumerable<(string id, MusicLookupResultDto album)> ParseArtistAlbumLists(
            string? body,
            string lookupKey
        ) {
            if (body == null) { yield break; }

            using JsonDocument jsonDoc = JsonDocument.Parse(body);
            JsonElement root = jsonDoc.RootElement;
            if (root.TryGetProperty( "items", out JsonElement itemProps )) {
                if (itemProps.GetArrayLength( ) == 0) { yield break; }

                foreach (JsonElement item in itemProps.EnumerateArray( )) {
                    yield return (item.GetProperty( "id" ).GetString( )!, ParseSpotifyResponse( item, lookupKey, SpotifyEntity.Album, null )!);
                }
            }
        }

        private async Task<MusicLookupResultDto?> ParseAlbumTrackListsAsync(
            string? body,
            string sanitizedSongTitle,
            string lookupKey
        ) {
            if (body == null) { return null; }

            try {
                using JsonDocument jsonDoc = JsonDocument.Parse(body);
                JsonElement root = jsonDoc.RootElement;
                if (root.TryGetProperty( "items", out JsonElement itemProps )) {
                    if (itemProps.GetArrayLength( ) == 0) { return null; }

                    foreach (JsonElement item in itemProps.EnumerateArray( )) {
                        string name = (item.GetProperty("name").GetString() ?? string.Empty).Trim();

                        if (SanitizeSongTitle( name ).Equals( sanitizedSongTitle, StringComparison.InvariantCultureIgnoreCase ) &&
                            CanParseJsonElement( item, out JsonElement element )
                        ) {
                            string trackId = item.GetProperty("id").GetString()!;

                            // The Album Track endpoint doesn't return the tracks with external Id's (ISRC's) included.
                            // So we'll perform another direct lookup to get the ISRC.
                            string? trackBody = await NewMusicApiRequest( SpotifyLinkParser.GetTrackIdURI( trackId ), lookupKey );

                            // This really shouldnt happen, but if something goes wrong we can return what we've already matched.
                            if (string.IsNullOrWhiteSpace( trackBody )) { return ParseSpotifyResponse( element, lookupKey, SpotifyEntity.Track, null ); }

                            using JsonDocument trackJson = JsonDocument.Parse( trackBody );
                            return ParseSpotifyResponse(
                                trackJson.RootElement,
                                lookupKey,
                                SpotifyEntity.Track,
                                null
                            );
                        }
                    }
                }
            } catch (Exception ex) {
                Logger.LogError( ex, $"An error occurred while parsing the {lookupKey}json response from spotify." );
                Logger.LogTrace( JsonSerializer.Serialize( body, SerializerOptions ) );
            }
            return null;
        }

    }
}
