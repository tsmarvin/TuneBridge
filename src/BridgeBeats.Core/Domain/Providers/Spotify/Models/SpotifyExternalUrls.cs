using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// Deserialization target mirroring the Spotify Web API <c>external_urls</c> object. For the entity
    /// types this codebase consumes the only populated member is the Spotify web link. The authoritative
    /// meaning of each field is the Spotify Web API object model; <c>[JsonPropertyName]</c> attributes
    /// map each property to its wire field.
    /// </summary>
    public sealed class SpotifyExternalUrls {

        /// <summary>Open-in-Spotify web URL for the entity, for example <c>https://open.spotify.com/track/...</c>. Maps to <c>spotify</c>.</summary>
        [JsonPropertyName( "spotify" )]
        public string Spotify { get; set; } = string.Empty;
    }

}
