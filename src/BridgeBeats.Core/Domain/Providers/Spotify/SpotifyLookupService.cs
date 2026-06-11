using System.Text.Json;
using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Domain.Providers.Common;
using BridgeBeats.Core.Domain.Providers.Spotify.Models;
using BridgeBeats.Core.Infrastructure.Logging;
using BridgeBeats.Providers.Spotify;

namespace BridgeBeats.Core.Domain.Providers.Spotify {

    /// <summary>
    /// An <see cref="IMusicLookupService"/> implementation for <see cref="SupportedProviders.Spotify"/>
    /// </summary>
    /// <param name="handler">The <see cref="SpotifyTokenHandler"/> used to authenticate the API calls performed by the service.</param>
    /// <param name="factory">The pre-configured HttpClientFactory used to perform the API calls for the service.</param>
    /// <param name="logger">The logger used to record errors.</param>
    /// <param name="serializerOptions">The Json Serializer Options used to record the body of the API results on error when using trace logging.</param>
    /// <param name="genreCache">Optional genre cache service for caching artist-track mappings.</param>
    public sealed partial class SpotifyLookupService(
        SpotifyTokenHandler handler,
        IHttpClientFactory factory,
        ILogger<SpotifyLookupService> logger,
        JsonSerializerOptions serializerOptions,
        IGenreCacheService? genreCache = null
    ) : MusicLookupServiceBase( logger, serializerOptions ), IMusicLookupService, ISpotifyBulkLookupService {

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

                        SpotifyAlbum? fullAlbum = JsonSerializer.Deserialize<SpotifyAlbum>( body, SerializerOptions );
                        if (fullAlbum == null) {
                            LogDeserializeAlbumFailed( Logger, LookupRequestType.AlbumIdLookup );
                        }
                        album.ExternalId = fullAlbum?.ExternalIds?.Upc ?? string.Empty;

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

        /// <summary>
        /// Performs a bulk lookup of tracks by their Spotify IDs.
        /// </summary>
        /// <param name="trackIds">The collection of Spotify track IDs to look up (max 50).</param>
        /// <returns>
        /// A dictionary mapping track ID to lookup result.
        /// Null values indicate the track was not found.
        /// </returns>
        /// <remarks>
        /// Uses a separate rate limit endpoint key (<see cref="SpotifyConstants.BulkTracksEndpoint"/>)
        /// from single-track lookups to allow independent rate limit tracking.
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown when more than 50 track IDs are provided.</exception>
        public async Task<Dictionary<string, MusicLookupResult?>> GetTracksByIdsAsync( IEnumerable<string> trackIds ) {
            List<string> idList = [.. trackIds];

            if (idList.Count == 0) {
                return [];
            }

            if (idList.Count > SpotifyConstants.MaxTracksPerBatchLookup) {
                throw new ArgumentException(
                    $"Cannot request more than {SpotifyConstants.MaxTracksPerBatchLookup} tracks at once. Received {idList.Count}.",
                    nameof( trackIds )
                );
            }

            string requestUri = SpotifyLinkParser.GetBulkTracksUri( idList );
            string? body = await NewBulkMusicApiRequest( requestUri, SpotifyConstants.BulkTracksEndpoint );

            Dictionary<string, MusicLookupResult?> results = [];

            if (string.IsNullOrWhiteSpace( body )) {
                // Request failed - return empty dictionary (caller should requeue)
                return results;
            }

            try {
                SpotifyTracksResponse? response = JsonSerializer.Deserialize<SpotifyTracksResponse>( body, SerializerOptions );

                if (response?.Tracks == null) {
                    LogDeserializeBulkTracksFailed( Logger );
                    return results;
                }

                // Map results back to IDs - position in response matches position in request
                for (int i = 0; i < idList.Count && i < response.Tracks.Count; i++) {
                    SpotifyTrack? track = response.Tracks[i];
                    if (track == null) {
                        // Track not found - null indicates not found
                        results[idList[i]] = null;
                    } else {
                        results[idList[i]] = new MusicLookupResult {
                            Artist = track.Artists is { Count: > 0 } ? track.Artists[0].Name : string.Empty,
                            Title = track.Name,
                            ExternalId = track.ExternalIds?.Isrc ?? string.Empty,
                            URL = track.ExternalUrls?.Spotify ?? string.Empty,
                            ArtUrl = track.Album?.Images is { Count: > 0 } ? track.Album.Images[0].Url : string.Empty,
                            IsAlbum = false,
                            IsPrimary = true
                        };

                        // Cache track→artist mapping for genre resolution
                        CacheTrackArtistMapping( track.Id, track.Artists );
                    }
                }
            } catch (Exception ex) {
                LogParseBulkTracksError( Logger, ex );
                LogResponseBody( Logger, body );
            }

            return results;
        }

        /// <summary>
        /// Performs a bulk lookup of albums by their Spotify IDs.
        /// </summary>
        /// <param name="albumIds">The collection of Spotify album IDs to look up (max 20).</param>
        /// <returns>
        /// A dictionary mapping album ID to lookup result.
        /// Null values indicate the album was not found.
        /// </returns>
        /// <remarks>
        /// Uses a separate rate limit endpoint key (<see cref="SpotifyConstants.BulkAlbumsEndpoint"/>)
        /// from single-album lookups to allow independent rate limit tracking.
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown when more than 20 album IDs are provided.</exception>
        public async Task<Dictionary<string, MusicLookupResult?>> GetAlbumsByIdsAsync( IEnumerable<string> albumIds ) {
            List<string> idList = [.. albumIds];

            if (idList.Count == 0) {
                return [];
            }

            if (idList.Count > SpotifyConstants.MaxAlbumsPerBatchLookup) {
                throw new ArgumentException(
                    $"Cannot request more than {SpotifyConstants.MaxAlbumsPerBatchLookup} albums at once. Received {idList.Count}.",
                    nameof( albumIds )
                );
            }

            string requestUri = SpotifyLinkParser.GetBulkAlbumsUri( idList );
            string? body = await NewBulkMusicApiRequest( requestUri, SpotifyConstants.BulkAlbumsEndpoint );

            Dictionary<string, MusicLookupResult?> results = [];

            if (string.IsNullOrWhiteSpace( body )) {
                // Request failed - return empty dictionary (caller should requeue)
                return results;
            }

            try {
                SpotifyAlbumsResponse? response = JsonSerializer.Deserialize<SpotifyAlbumsResponse>( body, SerializerOptions );

                if (response?.Albums == null) {
                    LogDeserializeBulkAlbumsFailed( Logger );
                    return results;
                }

                // Map results back to IDs - position in response matches position in request
                for (int i = 0; i < idList.Count && i < response.Albums.Count; i++) {
                    SpotifyAlbum? album = response.Albums[i];
                    if (album == null) {
                        // Album not found - null indicates not found
                        results[idList[i]] = null;
                    } else {
                        results[idList[i]] = new MusicLookupResult {
                            Artist = album.Artists is { Count: > 0 } ? album.Artists[0].Name : string.Empty,
                            Title = album.Name,
                            ExternalId = album.ExternalIds?.Upc ?? string.Empty,
                            URL = album.ExternalUrls?.Spotify ?? string.Empty,
                            ArtUrl = album.Images is { Count: > 0 } ? album.Images[0].Url : string.Empty,
                            IsAlbum = true,
                            IsPrimary = true
                        };
                    }
                }
            } catch (Exception ex) {
                LogParseBulkAlbumsError( Logger, ex );
                LogResponseBody( Logger, body );
            }

            return results;
        }

        /// <summary>
        /// Performs a bulk lookup of artists by their Spotify IDs to retrieve genre information.
        /// </summary>
        /// <param name="artistIds">The collection of Spotify artist IDs to look up (max 50).</param>
        /// <returns>
        /// A dictionary mapping artist ID to their genres.
        /// Null values indicate the artist was not found.
        /// Empty list indicates artist has no genres classified.
        /// </returns>
        /// <remarks>
        /// Uses a separate rate limit endpoint key (<see cref="SpotifyConstants.BulkArtistsEndpoint"/>)
        /// from other lookups to allow independent rate limit tracking.
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown when more than 50 artist IDs are provided.</exception>
        public async Task<Dictionary<string, List<string>?>> GetArtistsByIdsAsync( IEnumerable<string> artistIds ) {
            List<string> idList = [.. artistIds];

            if (idList.Count == 0) {
                return [];
            }

            if (idList.Count > SpotifyConstants.MaxArtistsPerBatchLookup) {
                throw new ArgumentException(
                    $"Cannot request more than {SpotifyConstants.MaxArtistsPerBatchLookup} artists at once. Received {idList.Count}.",
                    nameof( artistIds )
                );
            }

            string requestUri = SpotifyLinkParser.GetBulkArtistsUri( idList );
            string? body = await NewBulkMusicApiRequest( requestUri, SpotifyConstants.BulkArtistsEndpoint );

            Dictionary<string, List<string>?> results = [];

            if (string.IsNullOrWhiteSpace( body )) {
                // Request failed - return empty dictionary (caller should requeue)
                return results;
            }

            try {
                SpotifyArtistsResponse? response = JsonSerializer.Deserialize<SpotifyArtistsResponse>( body, SerializerOptions );

                if (response?.Artists == null) {
                    LogDeserializeBulkArtistsFailed( Logger );
                    return results;
                }

                // Map results back to IDs - position in response matches position in request
                for (int i = 0; i < idList.Count && i < response.Artists.Count; i++) {
                    SpotifyArtist? artist = response.Artists[i];
                    // Artist not found - null indicates not found
                    results[idList[i]] = artist?.Genres;
                }
            } catch (Exception ex) {
                LogParseBulkArtistsError( Logger, ex );
                LogResponseBody( Logger, body );
            }

            return results;
        }

        /// <summary>
        /// Performs an API request for bulk lookup operations with a custom endpoint key for rate limiting.
        /// </summary>
        /// <param name="requestUri">The API endpoint URI.</param>
        /// <param name="endpointKey">The endpoint key for rate limit tracking.</param>
        /// <returns>The response body, or null if the request failed.</returns>
        private async Task<string?> NewBulkMusicApiRequest( string requestUri, string endpointKey ) {
            using HttpClient client = await CreateAuthenticatedClientAsync( );
            HttpResponseMessage response = await client.GetAsync( requestUri );

            if (response.IsSuccessStatusCode) {
                return await response.Content.ReadAsStringAsync( );
            }

            // Handle rate limiting with custom endpoint key
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests) {
                TimeSpan retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds( 30 );
                LogBulkRateLimited( Logger, endpointKey, retryAfter );
                // Use the standard threshold - bulk requests are always thrown for requeue
                throw new Contracts.Exceptions.RetryAfterExceededException(
                    retryAfter,
                    TimeSpan.Zero, // Threshold is 0 since bulk requests are always queued
                    new Uri( client.BaseAddress!, requestUri ),
                    SupportedProviders.Spotify
                );
            }

            LogBulkRequestFailed( Logger, endpointKey, (int)response.StatusCode );
            return null;
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
                LogParseResponseError( Logger, ex, lookupKey );
                LogResponseBodySerialized( Logger, body, SerializerOptions );
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
                        SpotifyAlbum? albumData = element.Deserialize<SpotifyAlbum>( SerializerOptions );
                        if (albumData == null) {
                            LogDeserializeAlbumFailed( Logger, lookupKey );
                            return null;
                        }
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
                        SpotifyTrack? trackData = element.Deserialize<SpotifyTrack>( SerializerOptions );
                        if (trackData == null) {
                            LogDeserializeTrackFailed( Logger, lookupKey );
                            return null;
                        }
                        result.Artist = trackData.Artists != null && trackData.Artists.Count > 0
                                        ? trackData.Artists[0].Name
                                        : string.Empty;
                        result.Title = trackData.Name;
                        result.ExternalId = trackData.ExternalIds?.Isrc ?? string.Empty;
                        result.URL = trackData.ExternalUrls != null ? trackData.ExternalUrls.Spotify : string.Empty;
                        result.ArtUrl = trackData.Album?.Images != null && trackData.Album.Images.Count > 0
                                        ? trackData.Album.Images[0].Url
                                        : string.Empty;

                        // Cache track→artist mapping for genre resolution (fire and forget)
                        if (trackData.Artists != null) {
                            CacheTrackArtistMapping( trackData.Id, trackData.Artists );
                        }
                        break;
                }

                return result;
            } catch (Exception ex) {
                LogParseResponseError( Logger, ex, lookupKey );
                LogJsonElementSerialized( Logger, element, SerializerOptions );
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
                LogParseArtistListError( Logger, ex );
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
                        ArtUrl = album.Images != null && album.Images.Count > 0 ? album.Images[0].Url : string.Empty,
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

                        // This shouldn't happen, but if we cant fetch full track details, return simplified track data
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
                        if (fullTrack == null) {
                            LogDeserializeTrackFailed( Logger, LookupRequestType.SongIdLookup );
                            return null;
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

        /// <summary>
        /// Caches the track→artist mapping for later genre resolution via batch worker.
        /// Fires and forgets to avoid blocking the main response.
        /// </summary>
        /// <param name="trackId">The Spotify track ID.</param>
        /// <param name="artists">The list of artists on the track.</param>
        private void CacheTrackArtistMapping( string trackId, List<SpotifyArtistSimplified> artists ) {
            if (genreCache == null || string.IsNullOrWhiteSpace( trackId ) || artists.Count == 0) {
                return;
            }

            List<string> artistIds = [.. artists
                .Where( a => !string.IsNullOrWhiteSpace( a.Id ) )
                .Select( a => a.Id )];

            if (artistIds.Count == 0) { return; }

            // Fire and forget - don't block the response
            _ = Task.Run( async ( ) => {
                try {
                    await genreCache.SetTrackArtistMappingAsync( SupportedProviders.Spotify, trackId, artistIds );
                    await genreCache.EnqueueArtistsForRefreshAsync( SupportedProviders.Spotify, artistIds );
                } catch (Exception ex) {
                    LogCacheTrackMappingFailed( Logger, ex, trackId );
                }
            } );
        }

        #region LoggerMessage Methods

        /// <summary>
        /// Logs that album deserialization failed.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Providers.Spotify.DeserializeAlbumFailed,
            Level = LogLevel.Error,
            Message = "Failed to deserialize album data from Spotify response for {LookupKey}." )]
        internal static partial void LogDeserializeAlbumFailed( ILogger logger, LookupRequestType lookupKey );

        /// <summary>
        /// Logs that track deserialization failed.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Providers.Spotify.DeserializeTrackFailed,
            Level = LogLevel.Error,
            Message = "Failed to deserialize track data from Spotify response for {LookupKey}." )]
        internal static partial void LogDeserializeTrackFailed( ILogger logger, LookupRequestType lookupKey );

        /// <summary>
        /// Logs that bulk tracks deserialization failed.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Providers.Spotify.DeserializeBulkTracksFailed,
            Level = LogLevel.Error,
            Message = "Failed to deserialize bulk tracks response from Spotify." )]
        internal static partial void LogDeserializeBulkTracksFailed( ILogger logger );

        /// <summary>
        /// Logs that bulk albums deserialization failed.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Providers.Spotify.DeserializeBulkAlbumsFailed,
            Level = LogLevel.Error,
            Message = "Failed to deserialize bulk albums response from Spotify." )]
        internal static partial void LogDeserializeBulkAlbumsFailed( ILogger logger );

        /// <summary>
        /// Logs that bulk artists deserialization failed.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Providers.Spotify.DeserializeBulkArtistsFailed,
            Level = LogLevel.Error,
            Message = "Failed to deserialize bulk artists response from Spotify." )]
        internal static partial void LogDeserializeBulkArtistsFailed( ILogger logger );

        /// <summary>
        /// Logs an error parsing bulk tracks response.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Providers.Spotify.ParseBulkTracksError,
            Level = LogLevel.Error,
            Message = "An error occurred while parsing bulk tracks response from Spotify." )]
        internal static partial void LogParseBulkTracksError( ILogger logger, Exception ex );

        /// <summary>
        /// Logs an error parsing bulk albums response.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Providers.Spotify.ParseBulkAlbumsError,
            Level = LogLevel.Error,
            Message = "An error occurred while parsing bulk albums response from Spotify." )]
        internal static partial void LogParseBulkAlbumsError( ILogger logger, Exception ex );

        /// <summary>
        /// Logs an error parsing bulk artists response.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Providers.Spotify.ParseBulkArtistsError,
            Level = LogLevel.Error,
            Message = "An error occurred while parsing bulk artists response from Spotify." )]
        internal static partial void LogParseBulkArtistsError( ILogger logger, Exception ex );

        /// <summary>
        /// Logs rate limiting on bulk endpoint.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Providers.Spotify.BulkRateLimited,
            Level = LogLevel.Warning,
            Message = "Rate limited on bulk endpoint {Endpoint}, retry after {RetryAfter}" )]
        internal static partial void LogBulkRateLimited( ILogger logger, string endpoint, TimeSpan retryAfter );

        /// <summary>
        /// Logs bulk API request failure.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Providers.Spotify.BulkRequestFailed,
            Level = LogLevel.Warning,
            Message = "Bulk API request to {Endpoint} failed with status {StatusCode}" )]
        internal static partial void LogBulkRequestFailed( ILogger logger, string endpoint, int statusCode );

        /// <summary>
        /// Logs an error parsing the JSON response.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Providers.Spotify.ParseResponseError,
            Level = LogLevel.Error,
            Message = "An error occurred while parsing the {LookupKey} json response from spotify." )]
        internal static partial void LogParseResponseError( ILogger logger, Exception ex, LookupRequestType lookupKey );

        /// <summary>
        /// Logs an error parsing the artist list response.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Providers.Spotify.ParseArtistListError,
            Level = LogLevel.Error,
            Message = "An error occurred while parsing the artist list json response from spotify." )]
        internal static partial void LogParseArtistListError( ILogger logger, Exception ex );

        /// <summary>
        /// Logs failure to cache track artist mapping.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Providers.Spotify.CacheTrackMappingFailed,
            Level = LogLevel.Warning,
            Message = "Failed to cache track→artist mapping for Spotify track {TrackId}" )]
        internal static partial void LogCacheTrackMappingFailed( ILogger logger, Exception ex, string trackId );

        /// <summary>
        /// Logs the response body for trace level debugging.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Providers.Spotify.ResponseBodyTrace,
            Level = LogLevel.Trace,
            Message = "{ResponseBody}" )]
        internal static partial void LogResponseBody( ILogger logger, string? responseBody );

        /// <summary>
        /// Logs the serialized response body for trace level debugging.
        /// </summary>
        private static void LogResponseBodySerialized( ILogger logger, string? body, JsonSerializerOptions options ) {
            if (logger.IsEnabled( LogLevel.Trace )) {
                string serializedBody = JsonSerializer.Serialize( body, options );
                LogResponseBody( logger, serializedBody );
            }
        }

        /// <summary>
        /// Logs the serialized JSON element for trace level debugging.
        /// </summary>
        private static void LogJsonElementSerialized( ILogger logger, JsonElement element, JsonSerializerOptions options ) {
            if (logger.IsEnabled( LogLevel.Trace )) {
                string serializedElement = JsonSerializer.Serialize( element, options );
                LogResponseBody( logger, serializedElement );
            }
        }

        #endregion LoggerMessage Methods

    }
}
