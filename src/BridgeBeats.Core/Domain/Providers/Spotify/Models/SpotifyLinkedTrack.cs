using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// Deserialization target mirroring the Spotify Web API <c>linked_from</c> object on a track. When
    /// track relinking substitutes a market-available track for the one requested, this records the
    /// originally requested track. The authoritative meaning of each field is the Spotify Web API object
    /// model; <c>[JsonPropertyName]</c> attributes map each property to its wire field.
    /// </summary>
    public sealed class SpotifyLinkedTrack {

        /// <summary>Known external URLs for the originally requested track. Maps to <c>external_urls</c>.</summary>
        [JsonPropertyName( "external_urls" )]
        public SpotifyExternalUrls? ExternalUrls { get; set; }

        /// <summary>Spotify Web API endpoint URL for the originally requested track. Maps to <c>href</c>.</summary>
        [JsonPropertyName( "href" )]
        public string Href { get; set; } = string.Empty;

        /// <summary>Spotify id of the originally requested track. Maps to <c>id</c>.</summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;

        /// <summary>Object type, always <c>track</c> for this object. Maps to <c>type</c>.</summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = "track";

        /// <summary>Spotify URI of the originally requested track. Maps to <c>uri</c>.</summary>
        [JsonPropertyName( "uri" )]
        public string Uri { get; set; } = string.Empty;
    }

}
