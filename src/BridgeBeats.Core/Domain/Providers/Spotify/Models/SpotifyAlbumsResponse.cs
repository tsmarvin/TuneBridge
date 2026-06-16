using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// Deserialization target for the Spotify Web API "get several albums" response
    /// (GET /albums?ids={ids}), which wraps the returned albums in a top-level <c>albums</c> array.
    /// Entries may be null when a requested id was not found. The endpoint accepts up to 20 album ids per
    /// request. Mirrors the Spotify Web API response shape.
    /// </summary>
    public sealed class SpotifyAlbumsResponse {

        /// <summary>Full album objects in the order requested; an entry is null when the corresponding id was not found. Maps to <c>albums</c>.</summary>
        [JsonPropertyName( "albums" )]
        public List<SpotifyAlbum?> Albums { get; set; } = [];
    }

}
