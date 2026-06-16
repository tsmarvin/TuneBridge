using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// Deserialization target for the Spotify Web API "get several tracks" response
    /// (GET /tracks?ids={ids}), which wraps the returned tracks in a top-level <c>tracks</c> array.
    /// Entries may be null when a requested id was not found. The endpoint accepts up to 50 track ids per
    /// request. Mirrors the Spotify Web API response shape.
    /// </summary>
    public sealed class SpotifyTracksResponse {

        /// <summary>Full track objects in the order requested; an entry is null when the corresponding id was not found. Maps to <c>tracks</c>.</summary>
        [JsonPropertyName( "tracks" )]
        public List<SpotifyTrack?> Tracks { get; set; } = [];
    }

}
