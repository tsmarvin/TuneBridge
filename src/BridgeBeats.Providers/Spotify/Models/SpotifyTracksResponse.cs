using System.Text.Json.Serialization;

namespace BridgeBeats.Providers.Spotify.Models {

    /// <summary>
    /// Response from GET /tracks?ids={ids} - batch track lookup.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /tracks?ids={comma-separated-ids}
    /// Maximum: 50 track IDs per request
    /// Documentation: https://developer.spotify.com/documentation/web-api/reference/get-several-tracks
    /// </remarks>
    public sealed class SpotifyTracksResponse {

        /// <summary>
        /// A list of track objects. Position corresponds to the position of the ID in the request.
        /// If a track is not found, null is returned at that position.
        /// </summary>
        [JsonPropertyName( "tracks" )]
        public List<SpotifyTrack?> Tracks { get; set; } = [];
    }

}
