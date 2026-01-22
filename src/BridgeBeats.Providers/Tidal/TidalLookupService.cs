using System.Text;
using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Providers.Common;
using BridgeBeats.Providers.Tidal.Models;
using Microsoft.Extensions.Logging;

namespace BridgeBeats.Providers.Tidal {

    /// <summary>
    /// An <see cref="IMusicLookupService"/> implementation for <see cref="SupportedProviders.Tidal"/>
    /// </summary>
    /// <param name="handler">The <see cref="TidalTokenHandler"/> used to authenticate the API calls performed by the service.</param>
    /// <param name="factory">The pre-configured HttpClientFactory used to perform the API calls for the service.</param>
    /// <param name="logger">The logger used to record errors.</param>
    /// <param name="serializerOptions">The Json Serializer Options used to record the body of the API results on error when using trace logging.</param>
    /// <param name="genreCache">Optional genre cache service for caching genre data.</param>
    public sealed partial class TidalLookupService(
        TidalTokenHandler handler,
        IHttpClientFactory factory,
        ILogger<TidalLookupService> logger,
        JsonSerializerOptions serializerOptions,
        IGenreCacheService? genreCache = null
    ) : MusicLookupServiceBase( logger, serializerOptions ), IMusicLookupService {

        /// <summary>
        /// The default market region/storefront used for Tidal API requests.
        /// </summary>
        public const string DefaultStorefront = "US";

        /// <inheritdoc/>
        public override SupportedProviders Provider => SupportedProviders.Tidal;

        /// <inheritdoc/>
        public override async Task<MusicLookupResult?> GetInfoByISRCAsync( string isrc )
            => await ParseTidalResponse(
                await NewMusicApiRequest( TidalLinkParser.GetTracksIsrcURI( DefaultStorefront, isrc ), LookupRequestType.IsrcLookup ),
                LookupRequestType.IsrcLookup,
                TidalEntity.Track,
                null
            );

        /// <inheritdoc/>
        public override async Task<MusicLookupResult?> GetInfoByUPCAsync( string upc )
            => await ParseTidalResponse(
                await NewMusicApiRequest( TidalLinkParser.GetAlbumUpcURI( DefaultStorefront, upc ), LookupRequestType.UpcLookup ),
                LookupRequestType.UpcLookup,
                TidalEntity.Album,
                null
            );

        /// <inheritdoc/>
        public override async Task<MusicLookupResult?> GetInfoAsync( string title, string artist ) {
            List<(string id, string artistName)>? artistResults = ParseTidalArtistList(
                await NewMusicApiRequest(
                    TidalLinkParser.GetArtistSearchUri(DefaultStorefront, artist),
                    LookupRequestType.ArtistLookup
                )
            );
            MusicLookupResult? result = null;

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

        /// <inheritdoc/>
        public override async Task<MusicLookupResult?> GetInfoAsync( string uri ) {
            if (TidalLinkParser.TryParseUri( uri, out TidalEntity kind, out string id )) {
                if (kind == TidalEntity.Album) {
                    return await NewAlbumIdLookup( id, true );
                } else if (kind == TidalEntity.Track) {
                    return await NewTrackIdLookup( id, true );
                }
            }
            return null;
        }

        /// <inheritdoc/>
        public override async Task<MusicLookupResult?> GetInfoByIDAsync( string providerId, bool isAlbum )
            => isAlbum
                ? await NewAlbumIdLookup( providerId, true )
                : await NewTrackIdLookup( providerId, true );

        private async Task<MusicLookupResult?> ParseArtistAlbums( string artistId, string artistName, string title ) {
            string? body = await NewMusicApiRequest( TidalLinkParser.GetArtistAlbumsUri( DefaultStorefront, artistId ), LookupRequestType.ArtistAlbumLookup );
            if (body == null) { return null; }

            TidalResponse<TidalResource>? response = JsonSerializer.Deserialize<TidalResponse<TidalResource>>( body, SerializerOptions );
            if (response?.Included != null) {
                MusicLookupResult? albumResult = await ParseIncludedElementList( response.Included, title, true );
                if (albumResult != null) { return albumResult; }
            }
            return null;
        }

        private async Task<MusicLookupResult?> ParseArtistTracks( string artistId, string artistName, string title ) {
            string? body = await NewMusicApiRequest( TidalLinkParser.GetArtistTracksUri( DefaultStorefront, artistId ), LookupRequestType.AlbumTrackLookup );
            if (body == null) { return null; }

            TidalResponse<TidalResource>? response = JsonSerializer.Deserialize<TidalResponse<TidalResource>>( body, SerializerOptions );
            if (response?.Included != null) {
                MusicLookupResult? trackResult = await ParseIncludedElementList( response.Included, title, false );
                if (trackResult != null) { return trackResult; }
            }
            return null;
        }

        private async Task<MusicLookupResult?> ParseIncludedElementList( List<TidalResource> included, string title, bool isAlbum ) {
            foreach (TidalResource item in included) {
                if (!string.IsNullOrWhiteSpace( item.Id )
                    && ((isAlbum && item.Type == "albums") || (!isAlbum && item.Type == "tracks"))
                    && item.Attributes != null
                    && !string.IsNullOrWhiteSpace( item.Attributes.Title )
                    && (isAlbum
                        ? SanitizeAlbumTitle( item.Attributes.Title )
                        : SanitizeSongTitle( item.Attributes.Title )
                       ).Equals( title, StringComparison.InvariantCultureIgnoreCase )
                ) {
                    return isAlbum
                            ? await NewAlbumIdLookup( item.Id, false )
                            : await NewTrackIdLookup( item.Id, false );
                }
            }
            return null;
        }

        private async Task<MusicLookupResult?> NewAlbumIdLookup( string albumId, bool isPrimary ) {
            string? body = await NewMusicApiRequest( TidalLinkParser.GetAlbumIdURI( DefaultStorefront, albumId ), LookupRequestType.AlbumLookup );
            if (body != null) {
                (TidalResource? data, List<TidalResource>? included) = ExtractSingleResource( body );
                if (data != null && included != null) {
                    return await ParseTidalResponse( data, included, LookupRequestType.AlbumLookup, TidalEntity.Album, isPrimary );
                }
            }
            return null;
        }

        private async Task<MusicLookupResult?> NewTrackIdLookup( string trackId, bool isPrimary ) {
            string? body = await NewMusicApiRequest( TidalLinkParser.GetTrackIdURI( DefaultStorefront, trackId ), LookupRequestType.SongLookup );
            if (body != null) {
                (TidalResource? data, List<TidalResource>? included) = ExtractSingleResource( body );
                if (data != null && included != null) {
                    return await ParseTidalResponse( data, included, LookupRequestType.SongLookup, TidalEntity.Track, isPrimary );
                }
            }
            return null;
        }

        private protected override async Task<HttpClient> CreateAuthenticatedClientAsync( ) {
            HttpClient client = factory.CreateClient("tidal-api");
            client.DefaultRequestHeaders.Authorization = await handler.NewBearerAuthenticationHeader( );
            return client;
        }

        /// <summary>
        /// Extracts a single TidalResource from the response data, handling both single objects and arrays.
        /// According to JSON:API specification, the 'data' field can be either a single resource or an array.
        /// </summary>
        /// <param name="body">The JSON response body.</param>
        /// <returns>A tuple containing the extracted TidalResource and the included resources, or null if parsing fails.</returns>
        private (TidalResource? data, List<TidalResource>? included) ExtractSingleResource( string body ) {
            try {
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                // Try to get included first
                List<TidalResource>? included = null;
                if (root.TryGetProperty( "included", out JsonElement includedElement )) {
                    included = JsonSerializer.Deserialize<List<TidalResource>>( includedElement.GetRawText( ), SerializerOptions );
                }

                // Try to get data
                if (root.TryGetProperty( "data", out JsonElement dataElement )) {
                    if (dataElement.ValueKind == JsonValueKind.Array) {
                        // Data is an array, take the first element
                        List<TidalResource>? dataArray = JsonSerializer.Deserialize<List<TidalResource>>( dataElement.GetRawText( ), SerializerOptions );
                        if (dataArray != null && dataArray.Count > 0) {
                            return (dataArray[0], included);
                        }
                    } else if (dataElement.ValueKind == JsonValueKind.Object) {
                        // Data is a single object
                        TidalResource? singleResource = JsonSerializer.Deserialize<TidalResource>( dataElement.GetRawText( ), SerializerOptions );
                        return (singleResource, included);
                    }
                }
            } catch (Exception ex) {
                Logger.LogError( ex, "An error occurred while extracting single resource from Tidal response." );
            }

            return (null, null);
        }


        private async Task<MusicLookupResult?> ParseTidalResponse(
            string? body,
            LookupRequestType lookupKey,
            TidalEntity kind,
            bool? isPrimary
        ) {
            if (string.IsNullOrWhiteSpace( body )) { return null; }
            try {
                (TidalResource? data, List<TidalResource>? included) = ExtractSingleResource( body );
                if (data != null && included != null) {
                    return await ParseTidalResponse( data, included, lookupKey, kind, isPrimary );
                }
            } catch (Exception ex) {
                Logger.LogError( ex, "An error occurred while parsing the {LookupKey} json response from tidal.", lookupKey );
                Logger.LogTrace( "{ResponseBody}", JsonSerializer.Serialize( body, SerializerOptions ) );
            }
            return null;
        }

        private async Task<MusicLookupResult?> ParseTidalResponse(
            TidalResource data,
            List<TidalResource> included,
            LookupRequestType lookupKey,
            TidalEntity kind,
            bool? isPrimary
        ) {
            bool isAlbum = kind == TidalEntity.Album;
            MusicLookupResult result = new() {
                IsAlbum = isAlbum,
                IsPrimary = isPrimary ?? false,
                MarketRegion = DefaultStorefront
            };

            try {
                if (data.Relationships != null) {
                    result.Artist = GetArtistName( data.Relationships, included );
                }

                if (data.Attributes != null) {
                    result.Title = data.Attributes.Title ?? data.Attributes.Name ?? string.Empty;

                    result.ExternalId = GetExternalIdFromAttributes( data.Attributes, isAlbum );

                    if (data.Attributes.ExternalLinks != null
                        && data.Attributes.ExternalLinks.Count > 0
                    ) {
                        result.URL = data.Attributes.ExternalLinks[0]?.Href ?? string.Empty;
                    }
                }

                result.ArtUrl = await GetAlbumArtUrl( included, isAlbum );

                // Cache genres if available (fire and forget - don't block the response)
                CacheGenresFromIncluded( data, included );

                return result;
            } catch (Exception ex) {
                Logger.LogError( ex, "An error occurred while parsing the {LookupKey} json response from tidal.", lookupKey );
                Logger.LogTrace( "Data: {Data}", JsonSerializer.Serialize( data, SerializerOptions ) );
                Logger.LogTrace( "Included: {Included}", JsonSerializer.Serialize( included, SerializerOptions ) );
                return null;
            }
        }


        private static string GetExternalIdFromAttributes( TidalAttributes attributes, bool isAlbum ) {
            return isAlbum
                ? (attributes.BarcodeId ?? string.Empty)
                : (attributes.Isrc ?? string.Empty);
        }

        private static string GetArtistName(
            TidalRelationships relationships,
            List<TidalResource> included
        ) {
            if (relationships.Artists?.Data == null) {
                return string.Empty;
            }

            List<string> artistIds = [];
            StringBuilder resultBuilder = new();

            foreach (TidalResourceIdentifier artistInfo in relationships.Artists.Data) {
                if (artistInfo.Type == "artists" && !string.IsNullOrWhiteSpace( artistInfo.Id )) {
                    artistIds.Add( artistInfo.Id );
                    _ = resultBuilder.Append( $"|{artistInfo.Id}| & " );
                }
            }

            string result = resultBuilder.ToString( ).TrimEnd( ' ', '&' );

            // Find the matching artist in the included output
            foreach (TidalResource includedItem in included) {
                if (includedItem.Type == "artists"
                    && includedItem.Attributes != null
                    && !string.IsNullOrWhiteSpace( includedItem.Id )
                    && !string.IsNullOrWhiteSpace( includedItem.Attributes.Name )
                ) {
                    result = result.Replace( $"|{includedItem.Id}|", includedItem.Attributes.Name );
                }
            }

            return result;
        }

        private async Task<string> GetAlbumArtUrl(
            List<TidalResource> included,
            bool isAlbum
        ) {
            if (!isAlbum) {
                // Lookup album from album details and then return album art
                foreach (TidalResource item in included) {
                    if (item.Type == "albums" && !string.IsNullOrWhiteSpace( item.Id )) {
                        MusicLookupResult? album = await NewAlbumIdLookup( item.Id, false );
                        if (!string.IsNullOrWhiteSpace( album?.ArtUrl )) {
                            return album.ArtUrl;
                        }
                    }
                }
            } else {
                // Parse Album art directly from included output
                foreach (TidalResource item in included) {
                    if (item.Type == "artworks"
                        && item.Attributes != null
                        && item.Attributes.MediaType == "IMAGE"
                        && item.Attributes.Files != null
                        && item.Attributes.Files.Count > 0
                    ) {
                        return item.Attributes.Files[0]?.Href ?? string.Empty;
                    }
                }
            }
            return string.Empty;
        }

        private List<(string id, string artistName)>? ParseTidalArtistList( string? body ) {
            if (body == null) { return null; }

            List<(string id, string artistName)> results = [];
            try {
                TidalResponse<TidalResource>? response = JsonSerializer.Deserialize<TidalResponse<TidalResource>>( body, SerializerOptions );
                if (response?.Included != null && response.Included.Count > 0) {
                    foreach (TidalResource item in response.Included) {
                        if (item.Type == "artists"
                            && item.Attributes != null
                            && !string.IsNullOrWhiteSpace( item.Attributes.Name )
                            && !string.IsNullOrWhiteSpace( item.Id )
                        ) {
                            results.Add( (item.Id, item.Attributes.Name) );
                        }
                    }
                    return results;
                }
                return null;
            } catch (Exception ex) {
                Logger.LogError( ex, "An error occurred while parsing the artist list json response from tidal." );
                Logger.LogTrace( "{ResponseBody}", JsonSerializer.Serialize( body, SerializerOptions ) );
                return null;
            }
        }

        /// <summary>
        /// Caches genre data from the included array for a track/album.
        /// Looks for genre resources in the included array and matches them to the data resource via relationships.
        /// </summary>
        /// <param name="data">The main track/album resource.</param>
        /// <param name="included">The included resources array from the JSON:API response.</param>
        private void CacheGenresFromIncluded( TidalResource data, List<TidalResource> included ) {
            if (genreCache == null || string.IsNullOrWhiteSpace( data.Id )) {
                return;
            }

            // Get genre IDs from relationships
            HashSet<string> genreIds = [];
            if (data.Relationships?.Genres?.Data != null) {
                foreach (TidalResourceIdentifier genreRef in data.Relationships.Genres.Data) {
                    if (genreRef.Type == "genres" && !string.IsNullOrWhiteSpace( genreRef.Id )) {
                        _ = genreIds.Add( genreRef.Id );
                    }
                }
            }

            if (genreIds.Count == 0) { return; }

            // Find matching genre names in included array
            List<string> genreNames = [];
            foreach (TidalResource item in included) {
                if (item.Type == "genres"
                    && !string.IsNullOrWhiteSpace( item.Id )
                    && genreIds.Contains( item.Id )
                    && item.Attributes != null
                    && !string.IsNullOrWhiteSpace( item.Attributes.Name )
                ) {
                    genreNames.Add( item.Attributes.Name );
                }
            }

            if (genreNames.Count == 0) { return; }

            // Fire and forget - don't block the response
            _ = Task.Run( async ( ) => {
                try {
                    await genreCache.SetGenresAsync( SupportedProviders.Tidal, data.Id, genreNames );
                } catch (Exception ex) {
                    Logger.LogWarning( ex, "Failed to cache genres for Tidal resource {ResourceId}", data.Id );
                }
            } );
        }

    }
}
