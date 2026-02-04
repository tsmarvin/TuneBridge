using System.Text.Json;
using System.Text.RegularExpressions;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Infrastructure.Logging;

namespace BridgeBeats.Core.Domain.Providers.Common {
    /// <summary>
    /// Base class for music lookup services that query a specific music provider's API.
    /// </summary>
    /// <param name="logger">Logger for recording errors and diagnostic information.</param>
    /// <param name="serializerOptions">JSON serialization options for logging API responses.</param>
    public abstract partial class MusicLookupServiceBase(
        ILogger<MusicLookupServiceBase> logger,
        JsonSerializerOptions serializerOptions
    ) : IMusicLookupService {

        #region IMusicLookupService Implementation

        /// <inheritdoc/>
        public abstract SupportedProviders Provider { get; }

        /// <inheritdoc/>
        public abstract Task<MusicLookupResult?> GetInfoByISRCAsync( string isrc );

        /// <inheritdoc/>
        public abstract Task<MusicLookupResult?> GetInfoByUPCAsync( string upc );

        /// <inheritdoc/>
        public abstract Task<MusicLookupResult?> GetInfoAsync( string title, string artist );

        /// <inheritdoc/>
        public abstract Task<MusicLookupResult?> GetInfoAsync( string uri );

        /// <inheritdoc/>
        public async Task<MusicLookupResult?> GetInfoAsync( MusicLookupResult lookup )
            => string.IsNullOrWhiteSpace( lookup.ExternalId ) || lookup.IsAlbum == null
                        ? await GetInfoAsync( lookup.Title, lookup.Artist )
                        : ((bool)lookup.IsAlbum
                            ? await GetInfoByUPCAsync( lookup.ExternalId )
                            : await GetInfoByISRCAsync( lookup.ExternalId ))
                        ?? await GetInfoAsync( lookup.Title, lookup.Artist ); // Fallback to title/artist search if lookup by id fails

        /// <inheritdoc/>
        public abstract Task<MusicLookupResult?> GetInfoByIDAsync( string providerId, bool isAlbum );

        #endregion IMusicLookupService Implementation

        /// <summary>
        /// Creates an authenticated HTTP client for the music provider's API.
        /// </summary>
        /// <returns>An HTTP client with authentication headers configured.</returns>
        private protected abstract Task<HttpClient> CreateAuthenticatedClientAsync( );


        #region Base Class Defaults

        /// <summary>Logger for recording errors and diagnostic information.</summary>
        protected readonly ILogger<MusicLookupServiceBase> Logger = logger;
        /// <summary>JSON serialization options for logging API responses.</summary>
        protected readonly JsonSerializerOptions SerializerOptions = serializerOptions;

        private protected async Task<string?> NewMusicApiRequest(
            string requestUri,
            LookupRequestType lookupKey
        ) {
            using HttpClient http = await CreateAuthenticatedClientAsync();

            HttpResponseMessage resp = await http.GetAsync( requestUri );
            if (!resp.IsSuccessStatusCode) {
                LogApiRequestError( Logger, lookupKey, Provider.ToString( ), (int)resp.StatusCode, resp.ReasonPhrase );
                return null;
            }
            return await resp.Content.ReadAsStringAsync( );
        }

        /// <summary>
        /// Extracts the external ID (ISRC or UPC) from a JSON element.
        /// </summary>
        /// <param name="element">The JSON element to extract from.</param>
        /// <param name="isAlbum">True to extract UPC (album), false to extract ISRC (track).</param>
        /// <returns>The external ID if found, otherwise an empty string.</returns>
        protected static string GetExternalIdFromJson( JsonElement element, bool isAlbum ) {
            if (element.TryGetProperty( "external_ids", out JsonElement idProps )) {
                element = idProps;
            }

            return isAlbum
                    ? (element.TryGetProperty( "upc", out JsonElement upcProp )
                            ? upcProp.GetString( ) ?? string.Empty
                            : element.TryGetProperty( "barcodeId", out JsonElement barcodeProp )
                                ? barcodeProp.GetString( ) ?? string.Empty
                                : string.Empty)
                    : (element.TryGetProperty( "isrc", out JsonElement isrcProp )
                            ? isrcProp.GetString( ) ?? string.Empty
                            : string.Empty);
        }

        /// <summary>
        /// Validates if an album's title matches the expected title after sanitization.
        /// </summary>
        /// <param name="album">The album to validate.</param>
        /// <param name="title">The expected title.</param>
        /// <returns>True if the titles match after sanitization.</returns>
        protected static bool ValidateAlbumTitle( MusicLookupResult? album, string title )
            => album != null &&
                SanitizeAlbumTitle( title )
                .Equals( SanitizeAlbumTitle( album.Title ), StringComparison.InvariantCultureIgnoreCase );

        /// <summary>
        /// Validates if an album's title matches a pre-sanitized title.
        /// </summary>
        /// <param name="albumTitle">The album title to validate.</param>
        /// <param name="sanitizedTitle">The pre-sanitized expected title.</param>
        /// <returns>True if the titles match.</returns>
        protected static bool ValidateSanitizedAlbumTitle( string albumTitle, string sanitizedTitle )
            => sanitizedTitle
                .Equals( SanitizeAlbumTitle( albumTitle ), StringComparison.InvariantCultureIgnoreCase );

        /// <summary>
        /// Validates if a song's title matches the expected title after sanitization.
        /// </summary>
        /// <param name="song">The song to validate.</param>
        /// <param name="title">The expected title.</param>
        /// <returns>True if the titles match after sanitization.</returns>
        protected static bool ValidateSongTitle( MusicLookupResult? song, string title )
            => song != null &&
                SanitizeSongTitle( title )
                .Equals( SanitizeSongTitle( song.Title ), StringComparison.InvariantCultureIgnoreCase );

        /// <summary>
        /// Validates if a song's title matches a pre-sanitized title.
        /// </summary>
        /// <param name="song">The song to validate.</param>
        /// <param name="sanitizedTitle">The pre-sanitized expected title.</param>
        /// <returns>True if the titles match.</returns>
        protected static bool ValidateSanitizedSongTitle( MusicLookupResult? song, string sanitizedTitle )
            => song != null &&
                sanitizedTitle
                .Equals( SanitizeSongTitle( song.Title ), StringComparison.InvariantCultureIgnoreCase );

        /// <summary>
        /// Sanitizes a title string by removing ignored characters for matching.
        /// </summary>
        /// <param name="input">The title to sanitize.</param>
        /// <returns>The sanitized title.</returns>
        protected static string SanitizeTitleString( string input ) =>
            s_ignoredChars.Replace( input, string.Empty );

        /// <summary>
        /// Sanitizes a song title by removing addendums and ignored characters.
        /// </summary>
        /// <param name="title">The song title to sanitize.</param>
        /// <returns>The sanitized song title.</returns>
        protected static string SanitizeSongTitle( string title )
            => SanitizeTitleString(
                s_songTitleAddendum.IsMatch( title ) ?
                    title.Replace( s_songTitleAddendum.GetGroupValues( title, "Addendum" ).First( ), string.Empty ).Trim( ) + " "
                    + s_songTitleAddendum.GetGroupValues( title, "EditType" ).First( ).Trim( )
                : title
            );

        /// <summary>
        /// Sanitizes an album title by removing addendums and ignored characters.
        /// </summary>
        /// <param name="title">The album title to sanitize.</param>
        /// <returns>The sanitized album title.</returns>
        protected static string SanitizeAlbumTitle( string title )
            => SanitizeTitleString(
                s_albumTitleAddendum.IsMatch( title )
                    ? title.Replace( s_albumTitleAddendum.GetGroupValues( title, "Addendum" ).First( ), string.Empty ).Trim( )
                    : title
            );

        private static readonly Regex s_ignoredChars = InvalidSearchCharsRegex();
        [GeneratedRegex( @"[''""'\""]", RegexOptions.Compiled )]
        private static partial Regex InvalidSearchCharsRegex( );

        private static readonly Regex s_albumTitleAddendum = AlbumAddendumRegex();
        [GeneratedRegex( @"(?<Addendum> (?:- )?\(?(?:Single|EP)\)?)$", RegexOptions.Compiled )]
        private static partial Regex AlbumAddendumRegex( );

        private static readonly Regex s_songTitleAddendum = SongAddendumRegex();
        [GeneratedRegex( @"(?<Addendum> (?:- )?\(?(?<EditType>Radio Edit)\)?)$", RegexOptions.Compiled )]
        private static partial Regex SongAddendumRegex( );

        #endregion Base Class Defaults

        #region LoggerMessage Methods

        /// <summary>
        /// Logs an error when an API request fails.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Providers.Common.ApiRequestError,
            Level = LogLevel.Error,
            Message = "An error occurred while fetching {LookupKey}data from {Provider}: HTTP {StatusCode} {ReasonPhrase}" )]
        internal static partial void LogApiRequestError( ILogger logger, LookupRequestType lookupKey, string provider, int statusCode, string? reasonPhrase );

        #endregion LoggerMessage Methods

    }

}
