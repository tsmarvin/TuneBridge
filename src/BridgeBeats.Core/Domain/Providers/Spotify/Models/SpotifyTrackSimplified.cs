using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// Deserialization target mirroring the Spotify Web API simplified <c>track</c> object — the track
    /// shape embedded in an album's track listing (GET /albums/{id}/tracks). Compared with
    /// <see cref="SpotifyTrack"/> it omits the embedded album, external ids (ISRC), and popularity; fetch
    /// the full track via GET /tracks/{id} to obtain those. The authoritative meaning of each field is the
    /// Spotify Web API object model; <c>[JsonPropertyName]</c> attributes map each property to its wire field.
    /// </summary>
    public sealed class SpotifyTrackSimplified {

        /// <summary>Artists credited on the track, as simplified artist objects. Maps to <c>artists</c>.</summary>
        [JsonPropertyName( "artists" )]
        public List<SpotifyArtistSimplified> Artists { get; set; } = [];

        /// <summary>ISO 3166-1 alpha-2 market codes the track is available in; present only when no market is supplied in the request. Maps to <c>available_markets</c>.</summary>
        [JsonPropertyName( "available_markets" )]
        public List<string>? AvailableMarkets { get; set; }

        /// <summary>Disc number the track appears on, usually 1 unless the album spans more than one disc. Maps to <c>disc_number</c>.</summary>
        [JsonPropertyName( "disc_number" )]
        public int DiscNumber { get; set; }

        /// <summary>Track length in milliseconds. Maps to <c>duration_ms</c>.</summary>
        [JsonPropertyName( "duration_ms" )]
        public int DurationMs { get; set; }

        /// <summary>Whether the track has explicit lyrics. Maps to <c>explicit</c>.</summary>
        [JsonPropertyName( "explicit" )]
        public bool Explicit { get; set; }

        /// <summary>Known external URLs for the track, primarily the Spotify web link. Maps to <c>external_urls</c>.</summary>
        [JsonPropertyName( "external_urls" )]
        public SpotifyExternalUrls? ExternalUrls { get; set; }

        /// <summary>Spotify Web API endpoint URL providing full details for the track. Maps to <c>href</c>.</summary>
        [JsonPropertyName( "href" )]
        public string Href { get; set; } = string.Empty;

        /// <summary>Spotify id for the track. Maps to <c>id</c>.</summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;

        /// <summary>Whether the track is playable in the requested market; present only when a market is supplied. Maps to <c>is_playable</c>.</summary>
        [JsonPropertyName( "is_playable" )]
        public bool? IsPlayable { get; set; }

        /// <summary>When track relinking applied, the originally requested track. Maps to <c>linked_from</c>.</summary>
        [JsonPropertyName( "linked_from" )]
        public SpotifyLinkedTrack? LinkedFrom { get; set; }

        /// <summary>Content restrictions that apply to the track, when present. Maps to <c>restrictions</c>.</summary>
        [JsonPropertyName( "restrictions" )]
        public SpotifyRestrictions? Restrictions { get; set; }

        /// <summary>Track name. Maps to <c>name</c>.</summary>
        [JsonPropertyName( "name" )]
        public string Name { get; set; } = string.Empty;

        /// <summary>URL to a 30-second MP3 preview clip, or null when none is available. Maps to <c>preview_url</c>.</summary>
        [JsonPropertyName( "preview_url" )]
        public string? PreviewUrl { get; set; }

        /// <summary>Position of the track within its disc, starting at 1. Maps to <c>track_number</c>.</summary>
        [JsonPropertyName( "track_number" )]
        public int TrackNumber { get; set; }

        /// <summary>Object type, always <c>track</c> for this object. Maps to <c>type</c>.</summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = "track";

        /// <summary>Spotify URI for the track. Maps to <c>uri</c>.</summary>
        [JsonPropertyName( "uri" )]
        public string Uri { get; set; } = string.Empty;

        /// <summary>Whether the track is a local file rather than a Spotify catalog track. Maps to <c>is_local</c>.</summary>
        [JsonPropertyName( "is_local" )]
        public bool IsLocal { get; set; }
    }

}
