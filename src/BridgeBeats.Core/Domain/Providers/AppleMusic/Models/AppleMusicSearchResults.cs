using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.AppleMusic.Models {

    /// <summary>
    /// DTO mirroring the Apple Music API search <c>results</c> object, grouping hits by resource type.
    /// Deserialization target only.
    /// </summary>
    public sealed class AppleMusicSearchResults {

        /// <summary>The matched artists; <see langword="null"/> when none were requested or returned.</summary>
        [JsonPropertyName( "artists" )]
        public AppleMusicDataResponse<AppleMusicArtist>? Artists { get; set; }

        /// <summary>The matched albums; <see langword="null"/> when none were requested or returned.</summary>
        [JsonPropertyName( "albums" )]
        public AppleMusicDataResponse<AppleMusicAlbum>? Albums { get; set; }

        /// <summary>The matched songs; <see langword="null"/> when none were requested or returned.</summary>
        [JsonPropertyName( "songs" )]
        public AppleMusicDataResponse<AppleMusicSong>? Songs { get; set; }

    }

}
