using System.Text.RegularExpressions;
using TuneBridge.Domain.Implementations.Extensions;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Implementations.LinkParsers {

    /// <summary>
    /// Utility class for parsing SoundCloud URLs and constructing SoundCloud API request URIs.
    /// Extracts entity types (track, playlist) and URLs from soundcloud.com links,
    /// then maps them to the corresponding API v2 endpoints for metadata retrieval.
    /// </summary>
    /// <remarks>
    /// SoundCloud uses URL structure: soundcloud.com/{user}/{track-or-set} where {user} is
    /// the artist/user name and {track-or-set} is the track or playlist slug.
    /// This parser validates the URL structure and extracts components for API calls.
    /// </remarks>
    internal static partial class SoundCloudLinkParser {

        /// <summary>
        /// Parses a SoundCloud web URL to extract the entity type and permalink URL.
        /// Validates URL structure and determines whether the link points to a track or playlist.
        /// </summary>
        /// <param name="link">
        /// SoundCloud URL in the format "https://soundcloud.com/{user}/{track}".
        /// Must follow the soundcloud.com pattern.
        /// </param>
        /// <param name="kind">
        /// Output: The entity type extracted from the URL, mapped to <see cref="SoundCloudEntity"/> enum.
        /// Set to <see cref="SoundCloudEntity.Unknown"/> if the URL doesn't match known patterns.
        /// </param>
        /// <param name="url">
        /// Output: The full SoundCloud URL that can be used with the resolve API endpoint.
        /// Empty string if parsing fails.
        /// </param>
        /// <returns>
        /// True if the URL was successfully parsed and recognized as a supported SoundCloud entity type.
        /// False if the URL is malformed or doesn't match SoundCloud patterns.
        /// </returns>
        public static bool TryParseUri(
            string link,
            out SoundCloudEntity kind,
            out string url
        ) {
            kind = SoundCloudEntity.Unknown;
            url = string.Empty;

            if (SoundCloudLink.IsMatch( link )) {
                Match match = SoundCloudLink.Match(link);
                url = match.Value;
                
                // Check if it's a sets (playlist) URL
                if (url.Contains( "/sets/" )) {
                    kind = SoundCloudEntity.Playlist;
                } else {
                    // Default to track for standard user/track pattern
                    kind = SoundCloudEntity.Track;
                }
            }

            return kind != SoundCloudEntity.Unknown && !string.IsNullOrEmpty( url );
        }

        private static readonly Regex SoundCloudLink = SoundCloudMusicLink();
        [GeneratedRegex( @"(?:https?://)?(?:www\.)?soundcloud\.com/[\w\-]+/[\w\-]+", RegexOptions.IgnoreCase | RegexOptions.Compiled )]
        private static partial Regex SoundCloudMusicLink( );

        /// <summary>
        /// Constructs an API URI for resolving a SoundCloud URL to get resource details.
        /// </summary>
        /// <param name="url">The SoundCloud URL to resolve.</param>
        /// <returns>The API URI for URL resolution.</returns>
        public static string GetResolveURI( string url )
            => ResolveURI.Replace( "{url}", Uri.EscapeDataString( url ) );

        /// <summary>
        /// Constructs an API URI for searching tracks by query.
        /// </summary>
        /// <param name="query">The search query (title and artist).</param>
        /// <returns>The API URI for track search.</returns>
        public static string GetTrackSearchURI( string query )
            => TracksSearchURI.Replace( "{query}", Uri.EscapeDataString( query ) );

        /// <summary>
        /// Constructs an API URI for getting track details by ID.
        /// </summary>
        /// <param name="trackId">The track's SoundCloud ID.</param>
        /// <returns>The API URI for the track.</returns>
        public static string GetTrackIdURI( string trackId )
            => TracksURI.Replace( "{id}", trackId );

        private const string ResolveURI = "resolve?url={url}";
        private const string TracksSearchURI = "tracks?q={query}";
        private const string TracksURI = "tracks/{id}";

    }
}
