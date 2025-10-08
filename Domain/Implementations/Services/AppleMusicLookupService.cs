using System.Text.Json;
using TuneBridge.Configuration;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Implementations.Auth;
using TuneBridge.Domain.Implementations.Extensions;
using TuneBridge.Domain.Implementations.LinkParsers;
using TuneBridge.Domain.Interfaces;
using TuneBridge.Domain.Types.Bases;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Implementations.Services {

    /// <summary>
    /// An <see cref="IMusicLookupService"/> implementation for <see cref="SupportedProviders.AppleMusic"/>
    /// </summary>
    /// <param name="jwtHandler">The <see cref="AppleJwtHandler"/> used to authenticate the API calls performed by the service. Added via dependency injection in <see cref="StartupExtensions.AddTuneBridgeServices"/></param>
    /// <param name="factory">The pre-configured HttpClientFactory used to perform the API calls for the service. Added via dependency injection in <see cref="StartupExtensions.AddTuneBridgeServices"/></param>
    /// <param name="logger">The logger used to record errors. Added via dependency injection in <see cref="StartupExtensions.AddTuneBridgeServices"/></param>
    /// <param name="serializerOptions">The Json Serializer Options used to record the body of the API results on error when using trace logging. Added via dependency injection in <see cref="StartupExtensions.AddTuneBridgeServices"/></param>
    public partial class AppleMusicLookupService(
        AppleJwtHandler jwtHandler,
        IHttpClientFactory factory,
        ILogger<AppleMusicLookupService> logger,
        JsonSerializerOptions serializerOptions
    ) : MusicLookupServiceBase( logger, serializerOptions ), IMusicLookupService {

        public const string DefaultStorefront = "us";

        public override SupportedProviders Provider => SupportedProviders.AppleMusic;

        #region IMusicLookupService Public

        public override async Task<MusicLookupResultDto?> GetInfoByISRCAsync( string isrc ) =>
            ParseAppleMusicResponse(
                await NewMusicApiRequest( AppleMusicLinkParser.GetSongsIsrcURI( DefaultStorefront, isrc ), IsrcLookupKey ),
                IsrcLookupKey,
                DefaultStorefront,
                false,
                null
            );

        public override async Task<MusicLookupResultDto?> GetInfoByUPCAsync( string upc )
            => ParseAppleMusicResponse(
                await NewMusicApiRequest( AppleMusicLinkParser.GetAlbumUpcURI( DefaultStorefront, upc ), UpcLookupKey ),
                UpcLookupKey,
                DefaultStorefront,
                true,
                null
            );

        public override async Task<MusicLookupResultDto?> GetInfoAsync( string title, string artist ) {
            List<(string id, string artistName)>? artistResults = ParseAppleMusicArtistList(
                await NewMusicApiRequest(
                    AppleMusicLinkParser.GetArtistSearchUri(DefaultStorefront, artist),
                    ArtistLookupKey
                )
            );

            // Bail out early if we have no results
            if (artistResults == null || artistResults.Count == 0) { return null; }

            string sanitizedAlbumTitle = SanitizeAlbumTitle(title);
            string sanitizedSongTitle = SanitizeSongTitle(title);
            foreach ((string id, string artistName) in artistResults) {
                string lookupKey = $"albums for artist {artistName} {id} ";
                MusicLookupResultDto? result = ParseArtistElementLists(
                    lookupKey,
                    await NewMusicApiRequest(AppleMusicLinkParser.GetArtistAlbumsURI(DefaultStorefront, id), lookupKey),
                    sanitizedAlbumTitle,
                    DefaultStorefront,
                    true
                );

                if (result != null) { return result; }

                // if no album match, try songs
                lookupKey = $"songs for artist {artistName} {id} ";
                result = ParseArtistElementLists(
                    lookupKey,
                    await NewMusicApiRequest( AppleMusicLinkParser.GetArtistSongsURI( DefaultStorefront, id ), lookupKey ),
                    sanitizedSongTitle,
                    DefaultStorefront,
                    false
                );

                if (result != null) { return result; }
            }

            return null;
        }

        public override async Task<MusicLookupResultDto?> GetInfoAsync( string uri )
            => AppleMusicLinkParser.TryParseUri( uri, out string requestUri, out string storefront, out bool isAlbum )
                ? ParseAppleMusicResponse(
                    await NewMusicApiRequest( requestUri, UriLookupKey ),
                    UriLookupKey,
                    storefront,
                    isAlbum,
                    true
                )
                : null;

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
                using JsonDocument jsonDoc = JsonDocument.Parse(body);
                JsonElement root = jsonDoc.RootElement;
                if (root.TryGetProperty( "results", out JsonElement resultProps ) &&
                    resultProps.TryGetProperty( "artists", out JsonElement artistsProps ) &&
                    artistsProps.TryGetProperty( "data", out JsonElement dataProps )
                ) {
                    if (dataProps.GetArrayLength( ) == 0) { return null; }

                    foreach (JsonElement artist in dataProps.EnumerateArray( )) {
                        results.Add(
                            (artist
                                .GetProperty( "id" )
                                .GetString( )!,
                            artist
                                .GetProperty( "attributes" )
                                .GetProperty( "name" )
                                .GetString( )!)
                        );
                    }
                    return results;
                }
                return null;
            } catch (Exception ex) {
                Logger.LogError( ex, $"An error occurred while parsing the artist list json response from apple." );
                Logger.LogTrace( JsonSerializer.Serialize( body, SerializerOptions ) );
                return null;
            }
        }

        private MusicLookupResultDto? ParseArtistElementLists(
            string lookupKey,
            string? body,
            string title,
            string storefront,
            bool isAlbum
        ) {
            if (body == null) { return null; }

            try {
                using JsonDocument jsonDoc = JsonDocument.Parse(body);
                JsonElement root = jsonDoc.RootElement;
                if (root.TryGetProperty( "data", out JsonElement dataProps )) {
                    if (dataProps.GetArrayLength( ) == 0) { return null; }

                    foreach (JsonElement data in dataProps.EnumerateArray( )) {
                        JsonElement attributes = data.GetProperty("attributes");
                        string name = (attributes.GetProperty("name").GetString() ?? string.Empty).Trim();

                        if (
                            isAlbum
                            ? SanitizeAlbumTitle( name ).Equals( title, StringComparison.InvariantCultureIgnoreCase )
                            : SanitizeSongTitle( name ).Equals( title, StringComparison.InvariantCultureIgnoreCase )
                        ) {
                            return ParseAppleMusicResponse(
                                data,
                                lookupKey,
                                storefront,
                                isAlbum,
                                null
                            );
                        }
                    }
                }
            } catch (Exception ex) {
                Logger.LogError( ex, $"An error occurred while parsing the {lookupKey}json response from apple." );
                Logger.LogTrace( JsonSerializer.Serialize( body, SerializerOptions ) );
            }
            return null;
        }

        private MusicLookupResultDto? ParseAppleMusicResponse(
            string? body,
            string lookupKey,
            string storeFront,
            bool? isAlbum,
            bool? isPrimary
        ) {
            if (body == null) { return null; }
            try {
                using JsonDocument jsonDoc = JsonDocument.Parse(body);
                return ParseAppleMusicResponse( jsonDoc.RootElement, lookupKey, storeFront, isAlbum, isPrimary );
            } catch (Exception ex) {
                Logger.LogError( ex, $"An error occurred while parsing the {lookupKey}json response from apple." );
                Logger.LogTrace( JsonSerializer.Serialize( body, SerializerOptions ) );
            }
            return null;
        }

        private MusicLookupResultDto? ParseAppleMusicResponse(
            JsonElement root,
            string lookupKey,
            string storeFront,
            bool? isAlbum,
            bool? isPrimary
        ) {
            MusicLookupResultDto result = new() {
                MarketRegion = storeFront,
                IsAlbum = isAlbum,
                IsPrimary = isPrimary ?? false
            };
            try {
                JsonElement attributes;
                if (root.TryGetProperty( "data", out JsonElement dataProps )) {
                    if (dataProps.GetArrayLength( ) == 0) {
                        return null;
                    }
                    attributes = dataProps[0].GetProperty( "attributes" );
                } else if (root.TryGetProperty( "attributes", out JsonElement attrProps )) {
                    attributes = attrProps;
                } else {
                    return null;
                }

                result.Artist = attributes.GetProperty( "artistName" ).GetString( ) ?? string.Empty;
                result.Title = attributes.GetProperty( "name" ).GetString( ) ?? string.Empty;
                result.ExternalId = GetExternalIdFromJson( attributes, isAlbum ?? false );
                result.URL = attributes.GetProperty( "url" ).GetString( ) ?? string.Empty;

                if (attributes.TryGetProperty( "artwork", out JsonElement artwork )
                    && artwork.TryGetProperty( "url", out JsonElement urlProps )
                ) {
                    string artUrl = urlProps.GetString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace( artUrl ) == false) {
                        result.ArtUrl = artUrl
                                        .Replace( "{w}", artwork
                                                            .GetProperty( "width" )
                                                            .GetInt32( )
                                                            .ToString( )
                                        ).Replace( "{h}", artwork
                                                            .GetProperty( "height" )
                                                            .GetInt32( ).ToString( )
                                        );
                    }
                }

                return result;
            } catch (Exception ex) {
                Logger.LogError( ex, $"An error occurred while parsing the {lookupKey}json response from apple." );
                Logger.LogTrace( JsonSerializer.Serialize( root, SerializerOptions ) );
                return null;
            }
        }

        #endregion IMusicLookupService Private Methods

    }
}
