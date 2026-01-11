using System.Text.Json.Serialization;

namespace BridgeBeats.Domain.Contracts.Models.Spotify {

    /// <summary>
    /// Response from GET /albums?ids={ids} - batch album lookup.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /albums?ids={comma-separated-ids}
    /// Maximum: 20 album IDs per request
    /// Documentation: https://developer.spotify.com/documentation/web-api/reference/get-multiple-albums
    /// </remarks>
    public sealed class SpotifyAlbumsResponse {

        /// <summary>
        /// A list of album objects. Position corresponds to the position of the ID in the request.
        /// If an album is not found, null is returned at that position.
        /// </summary>
        [JsonPropertyName( "albums" )]
        public List<SpotifyAlbum?> Albums { get; set; } = [];
    }

}
