using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// Deserialization target mirroring the Spotify Web API <c>restrictions</c> object carried on albums
    /// and tracks. The authoritative meaning of each field is the Spotify Web API object model;
    /// <c>[JsonPropertyName]</c> attributes map each property to its wire field.
    /// </summary>
    public sealed class SpotifyRestrictions {

        /// <summary>Reason the content is restricted: <c>market</c> (not available in the user's market), <c>product</c> (not available with the user's subscription), or <c>explicit</c> (explicit content blocked by user settings). Maps to <c>reason</c>.</summary>
        [JsonPropertyName( "reason" )]
        public string Reason { get; set; } = string.Empty;
    }

}
