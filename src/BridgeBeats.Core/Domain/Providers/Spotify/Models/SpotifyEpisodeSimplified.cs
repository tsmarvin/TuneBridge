using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// Deserialization target mirroring the Spotify Web API simplified <c>episode</c> object as it
    /// appears in search results (GET /search?type=episode). Only the identifying fields this codebase
    /// consumes are modeled. The authoritative meaning of each field is the Spotify Web API object model;
    /// <c>[JsonPropertyName]</c> attributes map each property to its wire field.
    /// </summary>
    public sealed class SpotifyEpisodeSimplified {

        /// <summary>Spotify id for the episode. Maps to <c>id</c>.</summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;

        /// <summary>Episode name. Maps to <c>name</c>.</summary>
        [JsonPropertyName( "name" )]
        public string Name { get; set; } = string.Empty;

        /// <summary>Known external URLs for the episode, primarily the Spotify web link. Maps to <c>external_urls</c>.</summary>
        [JsonPropertyName( "external_urls" )]
        public SpotifyExternalUrls? ExternalUrls { get; set; }

        /// <summary>Spotify Web API endpoint URL providing full details for the episode. Maps to <c>href</c>.</summary>
        [JsonPropertyName( "href" )]
        public string Href { get; set; } = string.Empty;

        /// <summary>Spotify URI for the episode. Maps to <c>uri</c>.</summary>
        [JsonPropertyName( "uri" )]
        public string Uri { get; set; } = string.Empty;
    }

}
