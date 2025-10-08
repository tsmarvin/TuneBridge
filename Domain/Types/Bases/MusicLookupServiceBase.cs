using System.Text.Json;
using System.Text.RegularExpressions;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Implementations.Extensions;
using TuneBridge.Domain.Interfaces;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Types.Bases {
    public abstract partial class MusicLookupServiceBase(
        ILogger<MusicLookupServiceBase> logger,
        JsonSerializerOptions serializerOptions
    ) : IMusicLookupService {

        #region IMusicLookupService Implementation

        public abstract SupportedProviders Provider { get; }
        public abstract Task<MusicLookupResultDto?> GetInfoByISRCAsync( string isrc );
        public abstract Task<MusicLookupResultDto?> GetInfoByUPCAsync( string upc );
        public abstract Task<MusicLookupResultDto?> GetInfoAsync( string title, string artist );
        public abstract Task<MusicLookupResultDto?> GetInfoAsync( string uri );
        public async Task<MusicLookupResultDto?> GetInfoAsync( MusicLookupResultDto lookup )
            => string.IsNullOrWhiteSpace( lookup.ExternalId ) || lookup.IsAlbum == null
                        ? await GetInfoAsync( lookup.Title, lookup.Artist )
                        : ((bool)lookup.IsAlbum
                            ? await GetInfoByUPCAsync( lookup.ExternalId )
                            : await GetInfoByISRCAsync( lookup.ExternalId ))
                        ?? await GetInfoAsync( lookup.Title, lookup.Artist ); // Fallback to title/artist search if lookup by id fails

        #endregion IMusicLookupService Implementation

        private protected abstract Task<HttpClient> CreateAuthenticatedClientAsync( );


        #region Base Class Defaults

        protected const string IsrcLookupKey = "isrc song ";
        protected const string UpcLookupKey = "upc album ";
        protected const string ArtistLookupKey = "artist search ";
        protected const string UriLookupKey = "by uri ";
        protected const string AlbumLookupKey = "album ";
        protected const string SongLookupKey = "song ";

        protected readonly ILogger<MusicLookupServiceBase> Logger = logger;
        protected readonly JsonSerializerOptions SerializerOptions = serializerOptions;

        private protected async Task<string?> NewMusicApiRequest(
            string requestUri,
            string lookupKey
        ) {
            using HttpClient http = await CreateAuthenticatedClientAsync();

            HttpResponseMessage resp = await http.GetAsync(requestUri);
            if (!resp.IsSuccessStatusCode) {
                Logger.LogError( "An error occurred while fetching {lookupKey}data from apple: HTTP {statusCode} {reasonPhrase}", lookupKey, (int)resp.StatusCode, resp.ReasonPhrase );
                return null;
            }
            return await resp.Content.ReadAsStringAsync( );
        }

        protected static string GetExternalIdFromJson( JsonElement element, bool isAlbum ) {
            if (element.TryGetProperty( "external_ids", out JsonElement idProps )) {
                element = idProps;
            }

            return isAlbum
                    ? (element.TryGetProperty( "upc", out JsonElement upcProp )
                            ? upcProp.GetString( ) ?? string.Empty
                            : string.Empty)
                    : (element.TryGetProperty( "isrc", out JsonElement isrcProp )
                            ? isrcProp.GetString( ) ?? string.Empty
                            : string.Empty);
        }

        protected static bool ValidateAlbumTitle( MusicLookupResultDto? album, string title )
            => album != null &&
                SanitizeAlbumTitle( title )
                .Equals( SanitizeAlbumTitle( album.Title ), StringComparison.InvariantCultureIgnoreCase );

        protected static bool ValidateSanitizedAlbumTitle( MusicLookupResultDto? album, string sanitizedTitle )
            => album != null &&
                sanitizedTitle
                .Equals( SanitizeAlbumTitle( album.Title ), StringComparison.InvariantCultureIgnoreCase );

        protected static bool ValidateSongTitle( MusicLookupResultDto? song, string title )
            => song != null &&
                SanitizeSongTitle( title )
                .Equals( SanitizeSongTitle( song.Title ), StringComparison.InvariantCultureIgnoreCase );

        protected static bool ValidateSanitizedSongTitle( MusicLookupResultDto? song, string sanitizedTitle )
            => song != null &&
                sanitizedTitle
                .Equals( SanitizeSongTitle( song.Title ), StringComparison.InvariantCultureIgnoreCase );

        protected static string SanitizeTitleString( string input ) =>
            s_ignoredChars.Replace( input, string.Empty );

        protected static string SanitizeSongTitle( string title )
            => SanitizeTitleString(
                s_songTitleAddendum.IsMatch( title ) ?
                    (title.Replace( s_songTitleAddendum.GetGroupValues( title, "Addendum" ).First( ), string.Empty ).Trim( )) + " "
                    + s_songTitleAddendum.GetGroupValues( title, "EditType" ).First( ).Trim( )
                : title
            );

        protected static string SanitizeAlbumTitle( string title )
            => SanitizeTitleString(
                s_albumTitleAddendum.IsMatch( title )
                    ? title.Replace( s_albumTitleAddendum.GetGroupValues( title, "Addendum" ).First( ), string.Empty ).Trim( )
                    : title
            );

        private static readonly Regex s_ignoredChars = InvalidSearchCharsRegex();
        [GeneratedRegex( @"[‘’“”'\""]", RegexOptions.Compiled )]
        private static partial Regex InvalidSearchCharsRegex( );

        private static readonly Regex s_albumTitleAddendum = AlbumAddendumRegex();
        [GeneratedRegex( @"(?<Addendum> (?:- )?\(?(?:Single|EP)\)?)$", RegexOptions.Compiled )]
        private static partial Regex AlbumAddendumRegex( );

        private static readonly Regex s_songTitleAddendum = SongAddendumRegex();
        [GeneratedRegex( @"(?<Addendum> (?:- )?\(?(?<EditType>Radio Edit)\)?)$", RegexOptions.Compiled )]
        private static partial Regex SongAddendumRegex( );

        #endregion Base Class Defaults

    }

}
