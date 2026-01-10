using System.Text.Json.Serialization;

namespace BridgeBeats.Domain.Contracts.Models.AppleMusic {

    /// <summary>
    /// Contains the search results for each requested resource type.
    /// Each property represents a different resource type (artists, albums, songs, etc.).
    /// </summary>
    public sealed class AppleMusicSearchResults {

        /// <summary>
        /// Search results for artists.
        /// Only present if "artists" was in the types parameter.
        /// </summary>
        [JsonPropertyName( "artists" )]
        public AppleMusicDataResponse<AppleMusicArtist>? Artists { get; set; }

        /// <summary>
        /// Search results for albums.
        /// Only present if "albums" was in the types parameter.
        /// </summary>
        [JsonPropertyName( "albums" )]
        public AppleMusicDataResponse<AppleMusicAlbum>? Albums { get; set; }

        /// <summary>
        /// Search results for songs.
        /// Only present if "songs" was in the types parameter.
        /// </summary>
        [JsonPropertyName( "songs" )]
        public AppleMusicDataResponse<AppleMusicSong>? Songs { get; set; }

    }

}
