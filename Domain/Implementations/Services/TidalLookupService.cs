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
    /// An <see cref="IMusicLookupService"/> implementation for <see cref="SupportedProviders.Tidal"/>
    /// </summary>
    /// <param name="handler">The <see cref="TidalTokenHandler"/> used to authenticate the API calls performed by the service. Added via dependency injection in <see cref="StartupExtensions.AddTuneBridgeServices"/></param>
    /// <param name="factory">The pre-configured HttpClientFactory used to perform the API calls for the service. Added via dependency injection in <see cref="StartupExtensions.AddTuneBridgeServices"/></param>
    /// <param name="logger">The logger used to record errors. Added via dependency injection in <see cref="StartupExtensions.AddTuneBridgeServices"/></param>
    /// <param name="serializerOptions">The Json Serializer Options used to record the body of the API results on error when using trace logging. Added via dependency injection in <see cref="StartupExtensions.AddTuneBridgeServices"/></param>
    public sealed partial class TidalLookupService(
        TidalTokenHandler handler,
        IHttpClientFactory factory,
        ILogger<TidalLookupService> logger,
        JsonSerializerOptions serializerOptions
    ) : MusicLookupServiceBase( logger, serializerOptions ), IMusicLookupService {

        public override SupportedProviders Provider => SupportedProviders.Tidal;

        public override async Task<MusicLookupResultDto?> GetInfoByISRCAsync( string isrc )
            => ParseTidalResponse(
                await NewMusicApiRequest( TidalLinkParser.GetTracksIsrcURI( isrc ), IsrcLookupKey ),
                IsrcLookupKey,
                TidalEntity.Track,
                null
            );

        public override async Task<MusicLookupResultDto?> GetInfoByUPCAsync( string upc )
            => ParseTidalResponse(
                await NewMusicApiRequest( TidalLinkParser.GetAlbumUpcURI( upc ), UpcLookupKey ),
                UpcLookupKey,
                TidalEntity.Album,
                null
            );

        public override async Task<MusicLookupResultDto?> GetInfoAsync( string title, string artist ) {
            List<(string id, string artistName)>? artistResults = ParseTidalArtistList(
                await NewMusicApiRequest(
                    TidalLinkParser.GetArtistSearchUri(artist),
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
                    await NewMusicApiRequest( TidalLinkParser.GetArtistAlbumsURI( id ), lookupKey ),
                    lookupKey
                )) {
                    if (ValidateSanitizedAlbumTitle( album, sanitizedAlbumTitle )) {
                        // The Artist Album endpoint doesn't return the albums with external Id's (UPC's) included.
                        // So we'll perform another direct lookup to get the UPC.
                        string? body = await NewMusicApiRequest( TidalLinkParser.GetAlbumIdURI( albumId ), lookupKey );

                        // This really shouldnt happen, but if something goes wrong we can return what we've already matched.
                        if (string.IsNullOrWhiteSpace( body )) { return album; }

                        using JsonDocument jsonDoc = JsonDocument.Parse( body );
                        return ParseTidalResponse(
                            jsonDoc.RootElement,
                            lookupKey,
                            TidalEntity.Album,
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
                        await NewMusicApiRequest(TidalLinkParser.GetAlbumTracksURI(albumId), trackLookup),
                        sanitizedSongTitle,
                        trackLookup
                    );
                    if (result != null) { return result; }
                }
            }

            return null;
        }

        public override async Task<MusicLookupResultDto?> GetInfoAsync( string uri ) {
            if (TidalLinkParser.TryParseUri( uri, out TidalEntity kind, out string id )) {
                if (kind == TidalEntity.Album) {
                    string? body = await NewMusicApiRequest( TidalLinkParser.GetAlbumIdURI( id ), AlbumLookupKey );
                    if (body != null) {
                        using JsonDocument jsonDoc = JsonDocument.Parse(body);
                        return ParseTidalResponse(
                            jsonDoc.RootElement,
                            AlbumLookupKey,
                            kind,
                            true
                        );
                    }
                } else if (kind == TidalEntity.Track) {
                    return ParseTidalResponse(
                        await NewMusicApiRequest( TidalLinkParser.GetTrackIdURI( id ), SongLookupKey ),
                        SongLookupKey,
                        kind,
                        true
                    );
                }
            }
            return null;
        }

        private protected override async Task<HttpClient> CreateAuthenticatedClientAsync( ) {
            HttpClient client = factory.CreateClient("tidal-api");
            client.DefaultRequestHeaders.Authorization = await handler.NewBearerAuthenticationHeader( );
            return client;
        }

        private MusicLookupResultDto? ParseTidalResponse(
            string? body,
            string lookupKey,
            TidalEntity kind,
            bool? isPrimary
        ) {
            if (string.IsNullOrWhiteSpace( body )) { return null; }
            try {
                using JsonDocument jsonDoc = JsonDocument.Parse(body);
                if (CanParseJsonElement( jsonDoc.RootElement, out JsonElement element )) {
                    return ParseTidalResponse( element, lookupKey, kind, isPrimary );
                }
            } catch (Exception ex) {
                Logger.LogError( ex, $"An error occurred while parsing the {lookupKey}json response from tidal." );
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
            } else if (element.TryGetProperty( "artist", out JsonElement artistProp )) {
                if (artistProp.TryGetProperty( "name", out JsonElement nameProp )) {
                    result = nameProp.GetString( ) ?? string.Empty;
                }
            }
            return result;
        }

        private MusicLookupResultDto? ParseTidalResponse(
            JsonElement element,
            string lookupKey,
            TidalEntity kind,
            bool? isPrimary
        ) {
            bool isAlbum = kind == TidalEntity.Album;
            MusicLookupResultDto result = new() {
                IsAlbum = isAlbum,
                IsPrimary = isPrimary ?? false
            };

            try {
                result.Artist = GetArtistName( element );

                result.Title = element
                                .GetProperty( "title" )
                                .GetString( ) ?? string.Empty;

                result.ExternalId = GetExternalIdFromJson( element, isAlbum );

                // Construct Tidal URL
                string tidalId = element.GetProperty( "id" ).GetString() ?? string.Empty;
                result.URL = kind == TidalEntity.Album
                    ? $"https://tidal.com/browse/album/{tidalId}"
                    : $"https://tidal.com/browse/track/{tidalId}";

                result.ArtUrl = GetAlbumArtUrl( element );

                return result;
            } catch (Exception ex) {
                Logger.LogError( ex, $"An error occurred while parsing the {lookupKey}json response from tidal." );
                Logger.LogTrace( JsonSerializer.Serialize( element, SerializerOptions ) );
                return null;
            }
        }

        private static string GetAlbumArtUrl( JsonElement element ) {
            if (element.TryGetProperty( "cover", out JsonElement coverProp )) {
                string? cover = coverProp.GetString();
                if (!string.IsNullOrEmpty(cover)) {
                    // Tidal cover IDs need to be formatted as URLs
                    return $"https://resources.tidal.com/images/{cover.Replace('-', '/')}/320x320.jpg";
                }
            } else if (element.TryGetProperty( "album", out JsonElement albumProps )) {
                if (albumProps.TryGetProperty( "cover", out JsonElement albumCoverProp )) {
                    string? cover = albumCoverProp.GetString();
                    if (!string.IsNullOrEmpty(cover)) {
                        return $"https://resources.tidal.com/images/{cover.Replace('-', '/')}/320x320.jpg";
                    }
                }
            }
            return string.Empty;
        }

        private List<(string id, string artistName)>? ParseTidalArtistList( string? body ) {
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
                Logger.LogError( ex, $"An error occurred while parsing the artist list json response from tidal." );
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
                    yield return (item.GetProperty( "id" ).GetString( )!, ParseTidalResponse( item, lookupKey, TidalEntity.Album, null )!);
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
                        string name = (item.GetProperty("title").GetString() ?? string.Empty).Trim();

                        if (SanitizeSongTitle( name ).Equals( sanitizedSongTitle, StringComparison.InvariantCultureIgnoreCase ) &&
                            CanParseJsonElement( item, out JsonElement element )
                        ) {
                            string trackId = item.GetProperty("id").GetString()!;

                            // The Album Track endpoint doesn't return the tracks with external Id's (ISRC's) included.
                            // So we'll perform another direct lookup to get the ISRC.
                            string? trackBody = await NewMusicApiRequest( TidalLinkParser.GetTrackIdURI( trackId ), lookupKey );

                            // This really shouldnt happen, but if something goes wrong we can return what we've already matched.
                            if (string.IsNullOrWhiteSpace( trackBody )) { return ParseTidalResponse( element, lookupKey, TidalEntity.Track, null ); }

                            using JsonDocument trackJson = JsonDocument.Parse( trackBody );
                            return ParseTidalResponse(
                                trackJson.RootElement,
                                lookupKey,
                                TidalEntity.Track,
                                null
                            );
                        }
                    }
                }
            } catch (Exception ex) {
                Logger.LogError( ex, $"An error occurred while parsing the {lookupKey}json response from tidal." );
                Logger.LogTrace( JsonSerializer.Serialize( body, SerializerOptions ) );
            }
            return null;
        }

    }
}
