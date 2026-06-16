using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.AppleMusic.Models {

    /// <summary>
    /// DTO mirroring the Apple Music API <c>Songs</c> resource object. Deserialization target only;
    /// the authoritative field meanings are defined by the Apple Music API.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /v1/catalog/{storefront}/songs/{id}
    /// Documentation: https://developer.apple.com/documentation/applemusicapi/songs
    /// </remarks>
    public sealed class AppleMusicSong {

        /// <summary>The song's Apple Music catalog identifier.</summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;

        /// <summary>The resource type; always <c>songs</c> for this object.</summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = "songs";

        /// <summary>Relative API path to the song resource; <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "href" )]
        public string? Href { get; set; }

        /// <summary>The song's attribute payload; <see langword="null"/> when not requested or returned.</summary>
        [JsonPropertyName( "attributes" )]
        public AppleMusicSongAttributes? Attributes { get; set; }

    }

}
