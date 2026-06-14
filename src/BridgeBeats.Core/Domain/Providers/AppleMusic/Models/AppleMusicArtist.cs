using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.AppleMusic.Models {

    /// <summary>
    /// DTO mirroring the Apple Music API <c>Artists</c> resource object. Deserialization target only;
    /// the authoritative field meanings are defined by the Apple Music API.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /v1/catalog/{storefront}/artists/{id}
    /// Documentation: https://developer.apple.com/documentation/applemusicapi/artists
    /// </remarks>
    public sealed class AppleMusicArtist {

        /// <summary>The artist's Apple Music catalog identifier.</summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;

        /// <summary>The resource type; always <c>artists</c> for this object.</summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = "artists";

        /// <summary>Relative API path to the artist resource; <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "href" )]
        public string? Href { get; set; }

        /// <summary>The artist's attribute payload; <see langword="null"/> when not requested or returned.</summary>
        [JsonPropertyName( "attributes" )]
        public AppleMusicArtistAttributes? Attributes { get; set; }

    }

}
