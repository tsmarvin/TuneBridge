using System.Text.RegularExpressions;
using TuneBridge.Domain.Implementations.Extensions;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Implementations.LinkParsers {

    /// <summary>
    /// Utility class for parsing YouTube Music URLs and constructing YouTube Data API request URIs.
    /// Extracts entity types (video/track, playlist/album) and IDs from music.youtube.com URLs,
    /// then maps them to the corresponding API v3 endpoints for metadata retrieval.
    /// </summary>
    /// <remarks>
    /// YouTube Music uses URLs like music.youtube.com/watch?v={videoId} for tracks and
    /// music.youtube.com/playlist?list={playlistId} for albums/playlists.
    /// This parser validates the URL structure and extracts IDs for API calls.
    /// </remarks>
    internal static partial class YouTubeMusicLinkParser {

        /// <summary>
        /// Represents the type of YouTube Music entity.
        /// </summary>
        public enum YouTubeMusicEntity {
            Unknown,
            Video,
            Playlist
        }

        /// <summary>
        /// Parses a YouTube Music URL to extract the entity type and YouTube ID.
        /// </summary>
        /// <param name="link">
        /// YouTube Music URL in the format "https://music.youtube.com/watch?v={id}" or 
        /// "https://music.youtube.com/playlist?list={id}".
        /// </param>
        /// <param name="kind">
        /// Output: The entity type extracted from the URL, mapped to <see cref="YouTubeMusicEntity"/> enum.
        /// </param>
        /// <param name="id">
        /// Output: The YouTube video ID or playlist ID. Empty string if parsing fails.
        /// </param>
        /// <returns>
        /// True if the URL was successfully parsed and recognized as a supported YouTube Music entity type.
        /// </returns>
        public static bool TryParseUri(
            string link,
            out YouTubeMusicEntity kind,
            out string id
        ) {
            kind = YouTubeMusicEntity.Unknown;
            id = string.Empty;

            if (YouTubeMusicVideoLink.IsMatch( link )) {
                Match match = YouTubeMusicVideoLink.Match(link);
                id = match.Groups["id"].Value;
                kind = YouTubeMusicEntity.Video;
            } else if (YouTubeMusicPlaylistLink.IsMatch( link )) {
                Match match = YouTubeMusicPlaylistLink.Match(link);
                id = match.Groups["id"].Value;
                kind = YouTubeMusicEntity.Playlist;
            }

            return kind != YouTubeMusicEntity.Unknown && !string.IsNullOrEmpty( id );
        }

        private static readonly Regex YouTubeMusicVideoLink = YouTubeMusicVideoRegex();
        [GeneratedRegex( @"(?:music\.youtube\.com/watch\?v=)(?<id>[A-Za-z0-9_-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled )]
        private static partial Regex YouTubeMusicVideoRegex( );

        private static readonly Regex YouTubeMusicPlaylistLink = YouTubeMusicPlaylistRegex();
        [GeneratedRegex( @"(?:music\.youtube\.com/playlist\?list=)(?<id>[A-Za-z0-9_-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled )]
        private static partial Regex YouTubeMusicPlaylistRegex( );

        /// <summary>
        /// Constructs an API URI for searching videos by query.
        /// </summary>
        /// <param name="query">The search query.</param>
        /// <returns>The API URI for video search.</returns>
        public static string GetSearchUri( string query )
            => SearchURI.Replace( "{query}", Uri.EscapeDataString( query ) );

        /// <summary>
        /// Constructs an API URI for getting video details by ID.
        /// </summary>
        /// <param name="videoId">The YouTube video ID.</param>
        /// <returns>The API URI for video details.</returns>
        public static string GetVideoDetailsUri( string videoId )
            => VideoDetailsURI.Replace( "{id}", videoId );

        /// <summary>
        /// Constructs an API URI for getting playlist details by ID.
        /// </summary>
        /// <param name="playlistId">The YouTube playlist ID.</param>
        /// <returns>The API URI for playlist details.</returns>
        public static string GetPlaylistDetailsUri( string playlistId )
            => PlaylistDetailsURI.Replace( "{id}", playlistId );

        /// <summary>
        /// Constructs an API URI for getting playlist items by playlist ID.
        /// </summary>
        /// <param name="playlistId">The YouTube playlist ID.</param>
        /// <returns>The API URI for playlist items.</returns>
        public static string GetPlaylistItemsUri( string playlistId )
            => PlaylistItemsURI.Replace( "{id}", playlistId );

        private const string SearchURI = "search?part=snippet&type=video&videoCategoryId=10&q={query}&maxResults=5";
        private const string VideoDetailsURI = "videos?part=snippet&id={id}";
        private const string PlaylistDetailsURI = "playlists?part=snippet&id={id}";
        private const string PlaylistItemsURI = "playlistItems?part=snippet&playlistId={id}&maxResults=50";

    }
}
