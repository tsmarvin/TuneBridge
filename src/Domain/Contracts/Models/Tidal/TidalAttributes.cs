using System.Text.Json.Serialization;

namespace BridgeBeats.Domain.Contracts.Models.Tidal {

    /// <summary>
    /// Attributes object containing the actual data for a Tidal resource.
    /// The structure varies by resource type but shares common fields.
    /// </summary>
    /// <remarks>
    /// JSON:API specification allows attributes to contain any valid JSON.
    /// This class includes all possible attributes across different Tidal resource types.
    /// </remarks>
    public sealed class TidalAttributes {

        /// <summary>
        /// The title of the resource (track name, album name, artist name).
        /// </summary>
        [JsonPropertyName( "title" )]
        public string? Title { get; set; }

        /// <summary>
        /// The name of the resource (used for artists and artworks).
        /// </summary>
        [JsonPropertyName( "name" )]
        public string? Name { get; set; }

        /// <summary>
        /// The ISRC (International Standard Recording Code) for tracks.
        /// Used for cross-platform track matching.
        /// </summary>
        [JsonPropertyName( "isrc" )]
        public string? Isrc { get; set; }

        /// <summary>
        /// The barcode ID (UPC/EAN) for albums.
        /// Used for cross-platform album matching.
        /// </summary>
        [JsonPropertyName( "barcodeId" )]
        public string? BarcodeId { get; set; }

        /// <summary>
        /// External links to other platforms or services.
        /// </summary>
        [JsonPropertyName( "externalLinks" )]
        public List<TidalExternalLink>? ExternalLinks { get; set; }

        /// <summary>
        /// Media type for artwork resources.
        /// </summary>
        [JsonPropertyName( "mediaType" )]
        public string? MediaType { get; set; }

        /// <summary>
        /// Files array for artwork resources containing image URLs.
        /// </summary>
        [JsonPropertyName( "files" )]
        public List<TidalFile>? Files { get; set; }
    }

}
