using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// Deserialization target for the Spotify Web API "get several artists" response
    /// (GET /artists?ids={ids}), which wraps the returned artists in a top-level <c>artists</c> array.
    /// Entries may be null when a requested id was not found. The endpoint accepts up to 50 artist ids per
    /// request. Mirrors the Spotify Web API response shape.
    /// </summary>
    public sealed class SpotifyArtistsResponse {

        /// <summary>Full artist objects in the order requested; an entry is null when the corresponding id was not found. Maps to <c>artists</c>.</summary>
        [JsonPropertyName( "artists" )]
        public List<SpotifyArtist?> Artists { get; set; } = [];
    }

}
