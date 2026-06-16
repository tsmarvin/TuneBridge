using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// Deserialization target mirroring the Spotify Web API <c>followers</c> object carried on artists.
    /// The authoritative meaning of each field is the Spotify Web API object model;
    /// <c>[JsonPropertyName]</c> attributes map each property to its wire field.
    /// </summary>
    public sealed class SpotifyFollowers {

        /// <summary>Link to the full follower list; Spotify currently always returns null for this field. Maps to <c>href</c>.</summary>
        [JsonPropertyName( "href" )]
        public string? Href { get; set; }

        /// <summary>Total number of followers. Maps to <c>total</c>.</summary>
        [JsonPropertyName( "total" )]
        public int Total { get; set; }
    }

}
