using System.Text.Json;
using BridgeBeats.Configuration;
using BridgeBeats.Domain.Contracts.DTOs;
using BridgeBeats.Domain.Contracts.Models.Spotify;
using BridgeBeats.Domain.Implementations.Auth;
using BridgeBeats.Domain.Implementations.LinkParsers;
using BridgeBeats.Domain.Interfaces;
using BridgeBeats.Domain.Types.Bases;
using BridgeBeats.Domain.Types.Enums;

namespace BridgeBeats.Domain.Implementations.Services {

    /// <summary>
    /// An <see cref="IMusicLookupService"/> implementation for <see cref="SupportedProviders.Spotify"/>
    /// </summary>
    /// <param name="handler">The <see cref="SpotifyTokenHandler"/> used to authenticate the API calls performed by the service. Added via dependency injection in <see cref="StartupExtensions.ConfigureBridgeBeatsServices{TBuilder}"/></param>
    /// <param name="factory">The pre-configured HttpClientFactory used to perform the API calls for the service. Added via dependency injection in <see cref="StartupExtensions.ConfigureBridgeBeatsServices{TBuilder}"/></param>
    /// <param name="logger">The logger used to record errors. Added via dependency injection in <see cref="StartupExtensions.ConfigureBridgeBeatsServices{TBuilder}"/></param>
    /// <param name="serializerOptions">The Json Serializer Options used to record the body of the API results on error when using trace logging. Added via dependency injection in <see cref="StartupExtensions.ConfigureBridgeBeatsServices{TBuilder}"/></param>
    public sealed partial class SpotifyLookupService(
        SpotifyTokenHandler handler,
        IHttpClientFactory factory,
        ILogger<SpotifyLookupService> logger,
        JsonSerializerOptions serializerOptions
    ) : MusicLookupServiceBase( logger, serializerOptions ), IMusicLookupService {

        /// <inheritdoc/>
        public override SupportedProviders Provider => SupportedProviders.Spotify;

        /// <inheritdoc/>
        public override async Task<MusicLookupResult?> GetInfoByISRCAsync( string isrc )
            => ParseSpotifyResponse(
                await NewMusicApiRequest( SpotifyLinkParser.GetTracksIsrcURI( isrc ), LookupRequestType.IsrcLookup ),
                LookupRequestType.IsrcLookup,
                SpotifyEntity.Track,
                null
            );

        /// <inheritdoc/>
        public override async Task<MusicLookupResult?> GetInfoByUPCAsync( string upc )
            => ParseSpotifyResponse(
                await NewMusicApiRequest( SpotifyLinkParser.GetAlbumUpcURI( upc ), LookupRequestType.UpcLookup ),
                LookupRequestType.UpcLookup,
                SpotifyEntity.Album,
                null
            );

        /// <inheritdoc/>
        public override async Task<MusicLookupResult?> GetInfoAsync( string title, string artist ) {
            List<(string id, string artistName)>? artistResults = await ParseSpotifyArtistList( artist );

            // Bail out early if we have no results
            if (artistResults == null || artistResults.Count == 0) { return null; }

            string sanitizedAlbumTitle = SanitizeAlbumTitle( title );
            foreach ((string id, string artistName) in artistResults) {
                List<string> artistAlbumIds = [];
                await foreach ((string albumId, MusicLookupResult album) in ParseArtistAlbumLists( id )) {
                    if (ValidateSanitizedAlbumTitle( album.Title, sanitizedAlbumTitle )) {
                        // The Artist Album endpoint doesn't return the albums with external Id's (UPC's) included.
                        // So we'll perform another direct lookup to get the UPC.
                        string? body = await NewMusicApiRequest(
                            SpotifyLinkParser.GetAlbumIdURI( albumId ),
                            LookupRequestType.AlbumIdLookup
                        );

                        // This really shouldnt happen, but if something goes wrong we can return what we've already matched.
                        if (string.IsNullOrWhiteSpace( body )) { return album; }

                        SpotifyAlbum fullAlbum = JsonSerializer.Deserialize<SpotifyAlbum>( body, SerializerOptions )!;
                        album.ExternalId = fullAlbum.ExternalIds?.Upc ?? string.Empty;

                        return album;
                    } else {
                        artistAlbumIds.Add( albumId );
                    }
                }

                // If we couldnt match the artist + album combination, try searching for the
                // individual tracks on those albums instead.
                string sanitizedSongTitle = SanitizeSongTitle( title );
                foreach (string albumId in artistAlbumIds) {
                    MusicLookupResult? result = await ParseAlbumTrackListsAsync( albumId, sanitizedSongTitle );
                    if (result != null) { return result; }
                }
            }

            return null;
        }

        /// <inheritdoc/>
        public override async Task<MusicLookupResult?> GetInfoAsync( string uri ) {
            (bool result, SpotifyEntity kind, string id) = await SpotifyLinkParser.TryParseUriAsync( uri );
            if (result) {
                if (kind == SpotifyEntity.Album) {
                    return await GetInfoByIDAsync( id, true );
                } else if (kind == SpotifyEntity.Track) {
                    return await GetInfoByIDAsync( id, false );
                }
            }
            return null;
        }

        /// <inheritdoc/>
        public override async Task<MusicLookupResult?> GetInfoByIDAsync( string providerId, bool isAlbum ) {
            SpotifyEntity kind = isAlbum ? SpotifyEntity.Album : SpotifyEntity.Track;
            string requestUri = isAlbum
                ? SpotifyLinkParser.GetAlbumIdURI( providerId )
                : SpotifyLinkParser.GetTrackIdURI( providerId );

            LookupRequestType requestKey = isAlbum ? LookupRequestType.AlbumIdLookup : LookupRequestType.SongIdLookup;
            string? body = await NewMusicApiRequest( requestUri, requestKey );
            if (body == null) { return null; }

            using JsonDocument jsonDoc = JsonDocument.Parse(body);
            return ParseSpotifyResponse( jsonDoc.RootElement, requestKey, kind, true );
        }

        private protected override async Task<HttpClient> CreateAuthenticatedClientAsync( ) {
            HttpClient client = factory.CreateClient("spotify-api");
            client.DefaultRequestHeaders.Authorization = await handler.NewBearerAuthenticationHeader( );
            return client;
        }

        private MusicLookupResult? ParseSpotifyResponse(
            string? body,
            LookupRequestType lookupKey,
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

        /// <summary>
        /// Parses out the first track or album JsonElement from the Spotify API response.
        /// </summary>
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

        private MusicLookupResult? ParseSpotifyResponse(
            JsonElement element,
            LookupRequestType lookupKey,
            SpotifyEntity kind,
            bool? isPrimary
        ) {
            bool isAlbum = kind == SpotifyEntity.Album;
            MusicLookupResult result = new() {
                IsAlbum = isAlbum,
                IsPrimary = isPrimary ?? false
            };

            try {
                switch (kind) {
                    case SpotifyEntity.Album:
                        SpotifyAlbum albumData = JsonSerializer.Deserialize<SpotifyAlbum>(
                            JsonSerializer.Serialize( element, SerializerOptions ),
                            SerializerOptions
                        )!;
                        result.Artist = albumData.Artists != null && albumData.Artists.Count > 0
                                        ? albumData.Artists[0].Name
                                        : string.Empty;
                        result.Title = albumData.Name;
                        result.ExternalId = albumData.ExternalIds?.Upc ?? string.Empty;
                        result.URL = albumData.ExternalUrls != null ? albumData.ExternalUrls.Spotify : string.Empty;
                        result.ArtUrl = albumData.Images.Count > 0
                                        ? albumData.Images[0].Url
                                        : string.Empty;
                        break;
                    case SpotifyEntity.Track:
                        SpotifyTrack trackData = JsonSerializer.Deserialize<SpotifyTrack>(
                            JsonSerializer.Serialize( element, SerializerOptions ),
                            SerializerOptions
                        )!;
                        result.Artist = trackData.Artists != null && trackData.Artists.Count > 0
                                        ? trackData.Artists[0].Name
                                        : string.Empty;
                        result.Title = trackData.Name;
                        result.ExternalId = trackData.ExternalIds?.Isrc ?? string.Empty;
                        result.URL = trackData.ExternalUrls != null ? trackData.ExternalUrls.Spotify : string.Empty;
                        result.ArtUrl = trackData.Album?.Images != null && trackData.Album.Images.Count > 0
                                        ? trackData.Album.Images[0].Url
                                        : string.Empty;
                        break;
                }

                return result;
            } catch (Exception ex) {
                Logger.LogError( ex, $"An error occurred while parsing the {lookupKey}json response from spotify." );
                Logger.LogTrace( JsonSerializer.Serialize( element, SerializerOptions ) );
                return null;
            }
        }

        private async Task<List<(string id, string artistName)>?> ParseSpotifyArtistList( string artist ) {
            try {
                List<(string id, string artistName)> results = [];
                string? body = await NewMusicApiRequest(
                                        SpotifyLinkParser.GetArtistSearchUri( artist ),
                                        LookupRequestType.ArtistLookup
                                    );
                if (body == null) { return null; }

                SpotifySearchResponse? response = JsonSerializer.Deserialize<SpotifySearchResponse>( body );
                do {
                    if (null != response?.Artists) {
                        foreach (SpotifyArtist sArtist in response.Artists.Items) {
                            results.Add( (sArtist.Id, sArtist.Name) );
                        }

                        if (response.Artists.Next != null) {
                            body = await NewMusicApiRequest( response.Artists.Next, LookupRequestType.ArtistLookup );
                            if (body == null) { return results; }
                            response = JsonSerializer.Deserialize<SpotifySearchResponse>( body );
                        } else {
                            return results;
                        }
                    }
                } while (response?.Artists?.Next != null);
            } catch (Exception ex) {
                Logger.LogError( ex, $"An error occurred while parsing the artist list json response from spotify." );
            }

            return null;
        }

        private async IAsyncEnumerable<(string id, MusicLookupResult album)> ParseArtistAlbumLists( string id ) {

            string? body = await NewMusicApiRequest( SpotifyLinkParser.GetArtistAlbumsURI( id ), LookupRequestType.ArtistAlbumLookup);
            if (body == null) { yield break; }

            do {
                SpotifyPaging<SpotifyAlbumSimplified>? paging = JsonSerializer.Deserialize<SpotifyPaging<SpotifyAlbumSimplified>>( body );
                if (paging == null || paging.Items.Count == 0) { yield break; }

                foreach (SpotifyAlbumSimplified album in paging.Items) {
                    yield return (album.Id, new MusicLookupResult {
                        Artist = album.Artists != null && album.Artists.Count > 0 ? album.Artists[0].Name : string.Empty,
                        Title = album.Name,
                        ExternalId = string.Empty, // Will be filled in later if matched
                        URL = album.ExternalUrls != null ? album.ExternalUrls.Spotify : string.Empty,
                        IsAlbum = true
                    });
                }

                body = string.IsNullOrWhiteSpace( paging.Next )
                    ? null
                    : await NewMusicApiRequest( paging.Next, LookupRequestType.ArtistAlbumLookup );
            } while (body != null);
        }

        private async Task<MusicLookupResult?> ParseAlbumTrackListsAsync(
            string albumId,
            string sanitizedSongTitle
        ) {
            string? body = await NewMusicApiRequest(SpotifyLinkParser.GetAlbumTracksURI(albumId), LookupRequestType.AlbumTrackLookup);
            if (body == null) { return null; }

            do {
                SpotifyPaging<SpotifyTrackSimplified>? paging = JsonSerializer.Deserialize<SpotifyPaging<SpotifyTrackSimplified>>( body );
                if (paging == null || paging.Items.Count == 0) { return null; }

                foreach (SpotifyTrackSimplified track in paging.Items) {
                    if (SanitizeSongTitle( track.Name ).Equals( sanitizedSongTitle, StringComparison.InvariantCultureIgnoreCase )) {

                        string? trackBody = await NewMusicApiRequest( SpotifyLinkParser.GetTrackIdURI( track.Id ), LookupRequestType.SongIdLookup );
                        
                        // If we can't fetch full track details, return simplified track data
                        if (trackBody == null) {
                            return new MusicLookupResult {
                                Artist = track.Artists != null && track.Artists.Count > 0 ? track.Artists[0].Name : string.Empty,
                                Title = track.Name,
                                ExternalId = string.Empty,
                                URL = track.ExternalUrls != null ? track.ExternalUrls.Spotify : string.Empty,
                                IsAlbum = false
                            };
                        }

                        SpotifyTrack? fullTrack = JsonSerializer.Deserialize<SpotifyTrack>( trackBody );
                        
                        // If deserialization fails, return simplified track data
                        if (fullTrack == null) {
                            return new MusicLookupResult {
                                Artist = track.Artists != null && track.Artists.Count > 0 ? track.Artists[0].Name : string.Empty,
                                Title = track.Name,
                                ExternalId = string.Empty,
                                URL = track.ExternalUrls != null ? track.ExternalUrls.Spotify : string.Empty,
                                IsAlbum = false
                            };
                        }

                        return new MusicLookupResult {
                            Artist = fullTrack.Artists != null && fullTrack.Artists.Count > 0 ? fullTrack.Artists[0].Name : string.Empty,
                            Title = fullTrack.Name,
                            ExternalId = fullTrack.ExternalIds?.Isrc ?? string.Empty,
                            URL = fullTrack.ExternalUrls != null ? fullTrack.ExternalUrls.Spotify : string.Empty,
                            IsAlbum = false
                        };
                    }
                }

                body = string.IsNullOrWhiteSpace( paging.Next )
                    ? null
                    : await NewMusicApiRequest( paging.Next, LookupRequestType.AlbumTrackLookup );
            } while (body != null);

            return null;
        }

    }
}
