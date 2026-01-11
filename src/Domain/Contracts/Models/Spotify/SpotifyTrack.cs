using System.Text.Json.Serialization;

namespace BridgeBeats.Domain.Contracts.Models.Spotify {

    /// <summary>
    /// A full track object with complete details including external IDs (ISRC) and album info.
    /// Returned by GET /tracks/{id} and GET /tracks?ids=.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /tracks/{id}, GET /tracks?ids={ids}, GET /search?type=track
    /// Documentation: https://developer.spotify.com/documentation/web-api/reference/get-track
    /// </remarks>
    public sealed class SpotifyTrack {

        /// <summary>
        /// The album on which the track appears.
        /// Note: This is a simplified album object (no UPC).
        /// </summary>
        [JsonPropertyName( "album" )]
        public SpotifyAlbumSimplified? Album { get; set; }

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
        /// Known external IDs for the track.
        /// </summary>
        [JsonPropertyName( "external_ids" )]
        public SpotifyExternalIds? ExternalIds { get; set; }

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
        /// The popularity of the track. Value between 0 and 100, with 100 being the most popular.
        /// </summary>
        [JsonPropertyName( "popularity" )]
        public int Popularity { get; set; }

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
