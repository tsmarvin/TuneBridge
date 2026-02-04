using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// A simplified track object returned in album track lists.
    /// Contains basic identification but NOT external_ids (ISRC) or album info.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /albums/{id}/tracks, embedded in album objects
    /// Documentation: https://developer.spotify.com/documentation/web-api/reference/get-an-albums-tracks
    ///
    /// Important: This simplified object does NOT include:
    /// - ISRC (external_ids)
    /// - Album information
    /// - Popularity
    ///
    /// To get the ISRC, you must fetch the full track via GET /tracks/{id}.
    /// </remarks>
    public sealed class SpotifyTrackSimplified {

        /// <summary>
        /// The artists who performed the track.
        /// </summary>
        [JsonPropertyName( "artists" )]
        public List<SpotifyArtistSimplified> Artists { get; set; } = [];

        /// <summary>
        /// A list of the countries in which the track can be played (ISO 3166-1 alpha-2).
        /// NOTE: Only present when market is not supplied in the request.
        /// </summary>
        [JsonPropertyName( "available_markets" )]
        public List<string>? AvailableMarkets { get; set; }

        /// <summary>
        /// The disc number (usually 1 unless the album consists of more than one disc).
        /// </summary>
        [JsonPropertyName( "disc_number" )]
        public int DiscNumber { get; set; }

        /// <summary>
        /// The track length in milliseconds.
        /// </summary>
        [JsonPropertyName( "duration_ms" )]
        public int DurationMs { get; set; }

        /// <summary>
        /// Whether or not the track has explicit lyrics.
        /// </summary>
        [JsonPropertyName( "explicit" )]
        public bool Explicit { get; set; }

        /// <summary>
        /// Known external URLs for this track.
        /// </summary>
        [JsonPropertyName( "external_urls" )]
        public SpotifyExternalUrls? ExternalUrls { get; set; }

        /// <summary>
        /// A link to the Web API endpoint providing full details of the track.
        /// </summary>
        [JsonPropertyName( "href" )]
        public string Href { get; set; } = string.Empty;

        /// <summary>
        /// The Spotify ID for the track.
        /// </summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Whether the track is playable in the given market.
        /// </summary>
        [JsonPropertyName( "is_playable" )]
        public bool? IsPlayable { get; set; }

        /// <summary>
        /// Track link information if track relinking is applied.
        /// </summary>
        [JsonPropertyName( "linked_from" )]
        public SpotifyLinkedTrack? LinkedFrom { get; set; }

        /// <summary>
        /// Included if a content restriction is applied.
        /// </summary>
        [JsonPropertyName( "restrictions" )]
        public SpotifyRestrictions? Restrictions { get; set; }

        /// <summary>
        /// The name of the track.
        /// </summary>
        [JsonPropertyName( "name" )]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// A URL to a 30 second preview (MP3 format) of the track.
        /// </summary>
        [JsonPropertyName( "preview_url" )]
        public string? PreviewUrl { get; set; }

        /// <summary>
        /// The number of the track on its album. If the album has multiple discs,
        /// the track number is the number on the specified disc.
        /// </summary>
        [JsonPropertyName( "track_number" )]
        public int TrackNumber { get; set; }

        /// <summary>
        /// The object type. Always "track".
        /// </summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = "track";

        /// <summary>
        /// The Spotify URI for the track.
        /// </summary>
        [JsonPropertyName( "uri" )]
        public string Uri { get; set; } = string.Empty;

        /// <summary>
        /// Whether or not the track is from a local file.
        /// </summary>
        [JsonPropertyName( "is_local" )]
        public bool IsLocal { get; set; }
    }

}
