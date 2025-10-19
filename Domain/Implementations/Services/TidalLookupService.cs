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

        /// <summary>
        /// The default market region/storefront used for Tidal API requests.
        /// </summary>
        public const string DefaultStorefront = "US";

        public override SupportedProviders Provider => SupportedProviders.Tidal;

        public override async Task<MusicLookupResultDto?> GetInfoByISRCAsync( string isrc )
            => await ParseTidalResponse(
                await NewMusicApiRequest( TidalLinkParser.GetTracksIsrcURI( DefaultStorefront, isrc ), IsrcLookupKey ),
                IsrcLookupKey,
                TidalEntity.Track,
                null
            );

        public override async Task<MusicLookupResultDto?> GetInfoByUPCAsync( string upc )
            => await ParseTidalResponse(
                await NewMusicApiRequest( TidalLinkParser.GetAlbumUpcURI( DefaultStorefront, upc ), UpcLookupKey ),
                UpcLookupKey,
                TidalEntity.Album,
                null
            );

        public override async Task<MusicLookupResultDto?> GetInfoAsync( string title, string artist ) {
            List<(string id, string artistName)>? artistResults = ParseTidalArtistList(
                await NewMusicApiRequest(
                    TidalLinkParser.GetArtistSearchUri(DefaultStorefront, artist),
                    ArtistLookupKey
                )
            );
            MusicLookupResultDto? result = null;

            // Bail out early if we have no results
            if (artistResults == null || artistResults.Count == 0) { return result; }

            string sanitizedAlbumTitle = SanitizeAlbumTitle( title );
            string sanitizedTrackTitle = SanitizeSongTitle( title );
            foreach ((string id, string artistName) in artistResults) {
                result = await ParseArtistAlbums( id, artistName, sanitizedAlbumTitle );
                if (result != null) { return result; }

                result = await ParseArtistTracks( id, artistName, sanitizedTrackTitle );
                if (result != null) { return result; }
            }

            return null;
        }

        public override async Task<MusicLookupResultDto?> GetInfoAsync( string uri ) {
            if (TidalLinkParser.TryParseUri( uri, out TidalEntity kind, out string id )) {
                if (kind == TidalEntity.Album) {
                    return await NewAlbumIdLookup( id, true );
                } else if (kind == TidalEntity.Track) {
                    return await NewTrackIdLookup( id, true );
                }
            }
            return null;
        }

        private async Task<MusicLookupResultDto?> ParseArtistAlbums( string artistId, string artistName, string title ) {
            string lookupKey = $"albums for artist {artistName} {artistId} ";

            string? body = await NewMusicApiRequest( TidalLinkParser.GetArtistAlbumsUri( DefaultStorefront, artistId ), lookupKey );
            if (body == null) { return null; }

            using JsonDocument jsonDoc = JsonDocument.Parse(body);
            if (CanParseJsonElement( jsonDoc.RootElement, out JsonElement dataOutput, out JsonElement includedOutput )) {
                MusicLookupResultDto? albumResult = await ParseIncludedElementList( includedOutput, title, true );
                if (albumResult != null) { return albumResult; }
            }
            return null;
        }

        private async Task<MusicLookupResultDto?> ParseArtistTracks( string artistId, string artistName, string title ) {
            string lookupKey = $"tracks for artist {artistName} {artistId} ";

            string? body = await NewMusicApiRequest( TidalLinkParser.GetArtistTracksUri( DefaultStorefront, artistId ), lookupKey );
            if (body == null) { return null; }

            using JsonDocument jsonDoc = JsonDocument.Parse(body);
            if (CanParseJsonElement( jsonDoc.RootElement, out JsonElement dataOutput, out JsonElement includedOutput )) {
                MusicLookupResultDto? trackResult = await ParseIncludedElementList( includedOutput, title, false );
                if (trackResult != null) { return trackResult; }
            }
            return null;
        }

        private async Task<MusicLookupResultDto?> ParseIncludedElementList( JsonElement includedOutput, string title, bool isAlbum ) {
            foreach (JsonElement item in includedOutput.EnumerateArray( )) {
                if (item.TryGetProperty( "type", out JsonElement itemType )
                    && item.TryGetProperty( "id", out JsonElement itemIdProp )
                    && string.IsNullOrWhiteSpace( itemIdProp.GetString( ) ) == false
                    && ((isAlbum && itemType.GetString( ) == "albums") || (!isAlbum && itemType.GetString( ) == "tracks"))
                    && item.TryGetProperty( "attributes", out JsonElement itemAttributes )
                    && itemAttributes.TryGetProperty( "title", out JsonElement itemTitleProp )
                    && string.IsNullOrWhiteSpace( itemTitleProp.GetString( ) ) == false
                    && (isAlbum
                        ? SanitizeAlbumTitle( itemTitleProp.GetString( )! )
                        : SanitizeSongTitle( itemTitleProp.GetString( )! )
                       ).Equals( title, StringComparison.InvariantCultureIgnoreCase )
                ) {
                    string elementId = itemIdProp.GetString( )!;
                    return isAlbum
                            ? await NewAlbumIdLookup( elementId, false )
                            : await NewTrackIdLookup( elementId, false );
                }
            }
            return null;
        }

        private async Task<MusicLookupResultDto?> NewAlbumIdLookup( string albumId, bool isPrimary ) {
            string? body = await NewMusicApiRequest( TidalLinkParser.GetAlbumIdURI( DefaultStorefront, albumId ), AlbumLookupKey );
            if (body != null) {
                using JsonDocument jsonDoc = JsonDocument.Parse(body);
                if (CanParseJsonElement( jsonDoc.RootElement, out JsonElement dataOutput, out JsonElement includedOutput )) {
                    return await ParseTidalResponse( dataOutput, includedOutput, AlbumLookupKey, TidalEntity.Album, isPrimary );
                }
            }
            return null;
        }

        private async Task<MusicLookupResultDto?> NewTrackIdLookup( string trackId, bool isPrimary ) {
            string? body = await NewMusicApiRequest( TidalLinkParser.GetTrackIdURI( DefaultStorefront, trackId ), SongLookupKey );
            if (body != null) {
                using JsonDocument jsonDoc = JsonDocument.Parse(body);
                if (CanParseJsonElement( jsonDoc.RootElement, out JsonElement dataOutput, out JsonElement includedOutput )) {
                    return await ParseTidalResponse( dataOutput, includedOutput, SongLookupKey, TidalEntity.Track, isPrimary );
                }
            }
            return null;
        }

        private protected override async Task<HttpClient> CreateAuthenticatedClientAsync( ) {
            HttpClient client = factory.CreateClient("tidal-api");
            client.DefaultRequestHeaders.Authorization = await handler.NewBearerAuthenticationHeader( );
            return client;
        }

        private async Task<MusicLookupResultDto?> ParseTidalResponse(
            string? body,
            string lookupKey,
            TidalEntity kind,
            bool? isPrimary
        ) {
            if (string.IsNullOrWhiteSpace( body )) { return null; }
            try {
                using JsonDocument jsonDoc = JsonDocument.Parse(body);
                if (CanParseJsonElement( jsonDoc.RootElement, out JsonElement dataOutput, out JsonElement includedOutput )) {
                    return await ParseTidalResponse( dataOutput, includedOutput, lookupKey, kind, isPrimary );
                }
            } catch (Exception ex) {
                Logger.LogError( ex, $"An error occurred while parsing the {lookupKey}json response from tidal." );
                Logger.LogTrace( JsonSerializer.Serialize( body, SerializerOptions ) );
            }
            return null;
        }

        private static bool CanParseJsonElement( JsonElement root, out JsonElement dataOutput, out JsonElement includedOutput ) {
            dataOutput = root;
            includedOutput = root;
            bool result1 = false;
            bool result2 = false;

            if (root.TryGetProperty( "data", out JsonElement dataProps )) {
                dataOutput = dataProps.ValueKind == JsonValueKind.Array ? dataProps.EnumerateArray( ).First( ) : dataProps;
                result1 = true;
            }

            if (root.TryGetProperty( "included", out JsonElement includedProps )) {
                includedOutput = includedProps;
                result2 = true;
            }

            return result1 && result2;
        }

        private async Task<MusicLookupResultDto?> ParseTidalResponse(
            JsonElement dataOutput,
            JsonElement includedOutput,
            string lookupKey,
            TidalEntity kind,
            bool? isPrimary
        ) {
            bool isAlbum = kind == TidalEntity.Album;
            MusicLookupResultDto result = new() {
                IsAlbum = isAlbum,
                IsPrimary = isPrimary ?? false,
                MarketRegion = DefaultStorefront
            };

            try {
                if (dataOutput.TryGetProperty( "relationships", out JsonElement dataRelationships )) {
                    result.Artist = GetArtistName( dataRelationships, includedOutput );
                }

                if (dataOutput.TryGetProperty( "attributes", out JsonElement dataAttributes )) {
                    result.Title = dataAttributes
                                .GetProperty( "title" )
                                .GetString( ) ?? string.Empty;

                    result.ExternalId = GetExternalIdFromJson( dataAttributes, isAlbum );

                    if (dataAttributes.TryGetProperty( "externalLinks", out JsonElement extLinks )
                        && extLinks.GetArrayLength( ) > 0
                        && extLinks
                            .EnumerateArray( )
                            .First( )
                            .TryGetProperty( "href", out JsonElement hrefInfo )
                    ) {
                        result.URL = hrefInfo.GetString( ) ?? string.Empty;
                    }
                }

                result.ArtUrl = await GetAlbumArtUrl( includedOutput, isAlbum );

                return result;
            } catch (Exception ex) {
                Logger.LogError( ex, $"An error occurred while parsing the {lookupKey}json response from tidal." );
                Logger.LogTrace( JsonSerializer.Serialize( dataOutput, SerializerOptions ) );
                Logger.LogTrace( JsonSerializer.Serialize( includedOutput, SerializerOptions ) );
                return null;
            }
        }


        private static string GetArtistName(
            JsonElement dataRelationships,
            JsonElement includedOutput
        ) {
            string result = string.Empty;
            if (dataRelationships.TryGetProperty( "artists", out JsonElement artistsProps ) && artistsProps.TryGetProperty( "data", out JsonElement artistsArray )) {
                List<string> artistIds = [];
                foreach (JsonElement artistInfo in artistsArray.EnumerateArray( )) {
                    if (artistInfo.TryGetProperty( "type", out JsonElement contributorType ) && contributorType.GetString( ) == "artists" && artistInfo.TryGetProperty( "id", out JsonElement contributorId )) {
                        string artistId = contributorId.GetString( ) ?? string.Empty;
                        if (string.IsNullOrWhiteSpace( artistId ) == false) {
                            artistIds.Add( artistId );
                            result += $"|{artistId}| & ";
                        }
                    }
                }

                result = result.TrimEnd( ' ', '&' );

                // Find the matching artist in the included output
                foreach (JsonElement includedItem in includedOutput.EnumerateArray( )) {
                    if (includedItem.TryGetProperty( "type", out JsonElement includedType )
                        && includedType.GetString( ) == "artists"
                        && includedItem.TryGetProperty( "id", out JsonElement includedId )
                        && includedItem.TryGetProperty( "attributes", out JsonElement includedAttributes )
                        && includedAttributes.TryGetProperty( "name", out JsonElement includedName )
                    ) {
                        string artistId = includedId.GetString( ) ?? string.Empty;
                        string artistName = includedName.GetString( ) ?? string.Empty;
                        if (string.IsNullOrWhiteSpace( artistId ) == false && string.IsNullOrWhiteSpace( artistName ) == false) {
                            result = result.Replace( $"|{artistId}|", artistName );
                        }
                    }
                }
            }
            return result;
        }

        private async Task<string> GetAlbumArtUrl(
            JsonElement includedOutput,
            bool isAlbum
        ) {
            if (isAlbum == false) {
                // Lookup album from album details and then return album art
                foreach (JsonElement item in includedOutput.EnumerateArray( )) {
                    if (item.TryGetProperty( "type", out JsonElement itemType )
                        && itemType.GetString( ) == "albums"
                        && item.TryGetProperty( "id", out JsonElement albumIdProp )
                    ) {
                        MusicLookupResultDto? album = await NewAlbumIdLookup( albumIdProp.GetString( )!, false );
                        if (string.IsNullOrWhiteSpace( album?.ArtUrl ) == false) {
                            return album.ArtUrl;
                        }
                    }
                }
            } else {
                // Parse Album art directly from included output
                foreach (JsonElement item in includedOutput.EnumerateArray( )) {
                    if (item.TryGetProperty( "type", out JsonElement itemType )
                        && itemType.GetString( ) == "artworks"
                        && item.TryGetProperty( "attributes", out JsonElement itemAttributes )
                        && itemAttributes.TryGetProperty( "mediaType", out JsonElement itemMediaTypeProp )
                        && itemMediaTypeProp.GetString( ) == "IMAGE"
                        && itemAttributes.TryGetProperty( "files", out JsonElement itemFilesProp )
                        && itemFilesProp.GetArrayLength( ) > 0
                        && itemFilesProp.EnumerateArray( ).First( ).TryGetProperty( "href", out JsonElement itemUrlProp )
                    ) {
                        return itemUrlProp.GetString( ) ?? string.Empty;
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
                if (root.TryGetProperty( "included", out JsonElement includedDetails ) &&
                    includedDetails.GetArrayLength( ) > 0
                ) {
                    foreach (JsonElement item in includedDetails.EnumerateArray( )) {
                        if (item.GetProperty( "type" ).GetString( ) == "artists"
                            && item.TryGetProperty( "attributes", out JsonElement artistAttributes )
                            && artistAttributes.TryGetProperty( "name", out JsonElement artistNameProp )
                            && item.TryGetProperty( "id", out JsonElement artistIdProp )
                        ) {
                            results.Add(
                                (artistIdProp.GetString( )!,
                                artistNameProp.GetString( )!)
                            );
                        }
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

    }
}
