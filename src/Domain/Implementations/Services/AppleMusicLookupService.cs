using System.Text.Json;
using BridgeBeats.Configuration;
using BridgeBeats.Domain.Contracts.DTOs;
using BridgeBeats.Domain.Contracts.Models.AppleMusic;
using BridgeBeats.Domain.Implementations.Auth;
using BridgeBeats.Domain.Implementations.LinkParsers;
using BridgeBeats.Domain.Interfaces;
using BridgeBeats.Domain.Types.Bases;
using BridgeBeats.Domain.Types.Enums;

namespace BridgeBeats.Domain.Implementations.Services {

    /// <summary>
    /// An <see cref="IMusicLookupService"/> implementation for <see cref="SupportedProviders.AppleMusic"/>
    /// </summary>
    /// <param name="jwtHandler">The <see cref="AppleJwtHandler"/> used to authenticate the API calls performed by the service. Added via dependency injection in <see cref="StartupExtensions.ConfigureBridgeBeatsServices{TBuilder}"/></param>
    /// <param name="factory">The pre-configured HttpClientFactory used to perform the API calls for the service. Added via dependency injection in <see cref="StartupExtensions.ConfigureBridgeBeatsServices{TBuilder}"/></param>
    /// <param name="logger">The logger used to record errors. Added via dependency injection in <see cref="StartupExtensions.ConfigureBridgeBeatsServices{TBuilder}"/></param>
    /// <param name="serializerOptions">The Json Serializer Options used to record the body of the API results on error when using trace logging. Added via dependency injection in <see cref="StartupExtensions.ConfigureBridgeBeatsServices{TBuilder}"/></param>
    public partial class AppleMusicLookupService(
        AppleJwtHandler jwtHandler,
        IHttpClientFactory factory,
        ILogger<AppleMusicLookupService> logger,
        JsonSerializerOptions serializerOptions
    ) : MusicLookupServiceBase( logger, serializerOptions ), IMusicLookupService {

        /// <summary>
        /// The default market region/storefront used for Apple Music API requests.
        /// </summary>
        public const string DefaultStorefront = "us";

        /// <inheritdoc/>
        public override SupportedProviders Provider => SupportedProviders.AppleMusic;

        #region IMusicLookupService Public

        /// <inheritdoc/>
        public override async Task<MusicLookupResult?> GetInfoByISRCAsync( string isrc ) =>
            ParseAppleMusicResponse(
                await NewMusicApiRequest( AppleMusicLinkParser.GetSongsIsrcURI( DefaultStorefront, isrc ), LookupRequestType.IsrcLookup ),
                LookupRequestType.IsrcLookup,
                DefaultStorefront,
                false,
                null
            );

        /// <inheritdoc/>
        public override async Task<MusicLookupResult?> GetInfoByUPCAsync( string upc )
            => ParseAppleMusicResponse(
                await NewMusicApiRequest( AppleMusicLinkParser.GetAlbumUpcURI( DefaultStorefront, upc ), LookupRequestType.UpcLookup ),
                LookupRequestType.UpcLookup,
                DefaultStorefront,
                true,
                null
            );

        /// <inheritdoc/>
        public override async Task<MusicLookupResult?> GetInfoAsync( string title, string artist ) {
            List<(string id, string artistName)>? artistResults = ParseAppleMusicArtistList(
                await NewMusicApiRequest(
                    AppleMusicLinkParser.GetArtistSearchUri(DefaultStorefront, artist),
                    LookupRequestType.ArtistLookup
                )
            );

            // Bail out early if we have no results
            if (artistResults == null || artistResults.Count == 0) { return null; }

            string sanitizedAlbumTitle = SanitizeAlbumTitle(title);
            string sanitizedSongTitle = SanitizeSongTitle(title);
            foreach ((string id, string artistName) in artistResults) {
                MusicLookupResult? result = ParseArtistElementLists(
                    LookupRequestType.ArtistAlbumLookup,
                    await NewMusicApiRequest(AppleMusicLinkParser.GetArtistAlbumsURI(DefaultStorefront, id), LookupRequestType.ArtistAlbumLookup),
                    sanitizedAlbumTitle,
                    DefaultStorefront,
                    true
                );

                if (result != null) { return result; }

                // if no album match, try songs
                result = ParseArtistElementLists(
                    LookupRequestType.AlbumTrackLookup,
                    await NewMusicApiRequest( AppleMusicLinkParser.GetArtistSongsURI( DefaultStorefront, id ), LookupRequestType.AlbumTrackLookup ),
                    sanitizedSongTitle,
                    DefaultStorefront,
                    false
                );

                if (result != null) { return result; }
            }

            return null;
        }

        /// <inheritdoc/>
        public override async Task<MusicLookupResult?> GetInfoAsync( string uri )
            => AppleMusicLinkParser.TryParseUri( uri, out string requestUri, out string storefront, out bool isAlbum )
                ? ParseAppleMusicResponse(
                    await NewMusicApiRequest( requestUri, LookupRequestType.UriLookup ),
                    LookupRequestType.UriLookup,
                    storefront,
                    isAlbum,
                    true
                )
                : null;

        /// <inheritdoc/>
        public override async Task<MusicLookupResult?> GetInfoByIDAsync( string providerId, bool isAlbum ) {
            string requestUri = isAlbum
                ? AppleMusicLinkParser.GetAlbumIdUri( DefaultStorefront, providerId )
                : AppleMusicLinkParser.GetSongIdUri( DefaultStorefront, providerId );

            LookupRequestType requestKey = isAlbum ? LookupRequestType.AlbumIdLookup : LookupRequestType.SongIdLookup;
            return ParseAppleMusicResponse(
                await NewMusicApiRequest( requestUri, requestKey ),
                requestKey,
                DefaultStorefront,
                isAlbum,
                true
            );
        }

        #endregion IMusicLookupService Public

        #region IMusicLookupService Private Methods

        private protected override Task<HttpClient> CreateAuthenticatedClientAsync( ) {
            HttpClient client = factory.CreateClient("musickit-api");
            client.DefaultRequestHeaders.Authorization = jwtHandler.NewAuthenticationHeader( );
            return Task.FromResult( client );
        }

        private List<(string id, string artistName)>? ParseAppleMusicArtistList( string? body ) {
            if (body == null) { return null; }

            List<(string id, string artistName)> results = [];
            try {
                AppleMusicSearchResponse? response = JsonSerializer.Deserialize<AppleMusicSearchResponse>( body, SerializerOptions );
                if (response?.Results?.Artists?.Data != null && response.Results.Artists.Data.Count > 0) {
                    foreach (AppleMusicArtist artist in response.Results.Artists.Data) {
                        if (artist.Attributes != null) {
                            results.Add( (artist.Id, artist.Attributes.Name) );
                        }
                    }
                    return results;
                }
                return null;
            } catch (Exception ex) {
                Logger.LogError( ex, "An error occurred while parsing the artist list json response from apple." );
                Logger.LogTrace( "{ResponseBody}", JsonSerializer.Serialize( body, SerializerOptions ) );
                return null;
            }
        }

        private MusicLookupResult? ParseArtistElementLists(
            LookupRequestType lookupKey,
            string? body,
            string title,
            string storefront,
            bool isAlbum
        ) {
            if (body == null) { return null; }

            try {
                AppleMusicDataResponse<JsonElement>? response = JsonSerializer.Deserialize<AppleMusicDataResponse<JsonElement>>( body, SerializerOptions );
                if (response?.Data != null && response.Data.Count > 0) {
                    foreach (JsonElement item in response.Data) {
                        if (isAlbum) {
                            AppleMusicAlbum? album = item.Deserialize<AppleMusicAlbum>( SerializerOptions );
                            if (album?.Attributes != null) {
                                string name = album.Attributes.Name.Trim();
                                if (SanitizeAlbumTitle( name ).Equals( title, StringComparison.InvariantCultureIgnoreCase )) {
                                    return ParseAppleMusicAlbumResponse( album, lookupKey, storefront, null );
                                }
                            }
                        } else {
                            AppleMusicSong? song = item.Deserialize<AppleMusicSong>( SerializerOptions );
                            if (song?.Attributes != null) {
                                string name = song.Attributes.Name.Trim();
                                if (SanitizeSongTitle( name ).Equals( title, StringComparison.InvariantCultureIgnoreCase )) {
                                    return ParseAppleMusicSongResponse( song, lookupKey, storefront, null );
                                }
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                Logger.LogError( ex, "An error occurred while parsing the {LookupKey} json response from apple.", lookupKey );
                Logger.LogTrace( "{ResponseBody}", JsonSerializer.Serialize( body, SerializerOptions ) );
            }
            return null;
        }

        private MusicLookupResult? ParseAppleMusicResponse(
            string? body,
            LookupRequestType lookupKey,
            string storeFront,
            bool? isAlbum,
            bool? isPrimary
        ) {
            if (body == null) { return null; }
            try {
                AppleMusicDataResponse<JsonElement>? response = JsonSerializer.Deserialize<AppleMusicDataResponse<JsonElement>>( body, SerializerOptions );
                if (response?.Data == null || response.Data.Count == 0) {
                    return null;
                }

                JsonElement firstItem = response.Data[0];
                if (isAlbum == true) {
                    AppleMusicAlbum? album = firstItem.Deserialize<AppleMusicAlbum>( SerializerOptions );
                    return album != null ? ParseAppleMusicAlbumResponse( album, lookupKey, storeFront, isPrimary ) : null;
                } else {
                    AppleMusicSong? song = firstItem.Deserialize<AppleMusicSong>( SerializerOptions );
                    return song != null ? ParseAppleMusicSongResponse( song, lookupKey, storeFront, isPrimary ) : null;
                }
            } catch (Exception ex) {
                Logger.LogError( ex, "An error occurred while parsing the {LookupKey} json response from apple.", lookupKey );
                Logger.LogTrace( "{ResponseBody}", JsonSerializer.Serialize( body, SerializerOptions ) );
            }
            return null;
        }

        private MusicLookupResult? ParseAppleMusicSongResponse(
            AppleMusicSong song,
            LookupRequestType lookupKey,
            string storeFront,
            bool? isPrimary
        ) {
            try {
                if (song.Attributes == null) { return null; }

                MusicLookupResult result = new() {
                    MarketRegion = storeFront,
                    IsAlbum = false,
                    IsPrimary = isPrimary ?? false,
                    Artist = song.Attributes.ArtistName,
                    Title = song.Attributes.Name,
                    ExternalId = song.Attributes.Isrc ?? string.Empty,
                    URL = song.Attributes.Url
                };

                if (song.Attributes.Artwork != null && !string.IsNullOrWhiteSpace( song.Attributes.Artwork.Url )) {
                    result.ArtUrl = song.Attributes.Artwork.Url
                        .Replace( "{w}", song.Attributes.Artwork.Width.ToString( ) )
                        .Replace( "{h}", song.Attributes.Artwork.Height.ToString( ) );
                }

                return result;
            } catch (Exception ex) {
                Logger.LogError( ex, "An error occurred while parsing the {LookupKey} json response from apple.", lookupKey );
                Logger.LogTrace( "{ResponseBody}", JsonSerializer.Serialize( song, SerializerOptions ) );
                return null;
            }
        }

        private MusicLookupResult? ParseAppleMusicAlbumResponse(
            AppleMusicAlbum album,
            LookupRequestType lookupKey,
            string storeFront,
            bool? isPrimary
        ) {
            try {
                if (album.Attributes == null) { return null; }

                MusicLookupResult result = new() {
                    MarketRegion = storeFront,
                    IsAlbum = true,
                    IsPrimary = isPrimary ?? false,
                    Artist = album.Attributes.ArtistName,
                    Title = album.Attributes.Name,
                    ExternalId = album.Attributes.Upc ?? string.Empty,
                    URL = album.Attributes.Url
                };

                if (album.Attributes.Artwork != null && !string.IsNullOrWhiteSpace( album.Attributes.Artwork.Url )) {
                    result.ArtUrl = album.Attributes.Artwork.Url
                        .Replace( "{w}", album.Attributes.Artwork.Width.ToString( ) )
                        .Replace( "{h}", album.Attributes.Artwork.Height.ToString( ) );
                }

                return result;
            } catch (Exception ex) {
                Logger.LogError( ex, "An error occurred while parsing the {LookupKey} json response from apple.", lookupKey );
                Logger.LogTrace( "{ResponseBody}", JsonSerializer.Serialize( album, SerializerOptions ) );
                return null;
            }
        }

        #endregion IMusicLookupService Private Methods

    }
}
