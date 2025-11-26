using System.Text.Json.Serialization;

namespace TuneBridge.Domain.Contracts.DTOs {

    /// <summary>
    /// Represents an oEmbed response according to the oEmbed specification.
    /// See: https://oembed.com/
    /// </summary>
    public sealed class OEmbedResponse {

        /// <summary>
        /// The resource type. For TuneBridge, this is always "rich" as we provide embeddable HTML content.
        /// </summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = "rich";

        /// <summary>
        /// The oEmbed version number. Must be "1.0".
        /// </summary>
        [JsonPropertyName( "version" )]
        public string Version { get; set; } = "1.0";

        /// <summary>
        /// A text title describing the resource.
        /// </summary>
        [JsonPropertyName( "title" )]
        public string? Title { get; set; }

        /// <summary>
        /// The name of the author/owner of the resource.
        /// </summary>
        [JsonPropertyName( "author_name" )]
        public string? AuthorName { get; set; }

        /// <summary>
        /// A URL for the author/owner of the resource.
        /// </summary>
        [JsonPropertyName( "author_url" )]
        public string? AuthorUrl { get; set; }

        /// <summary>
        /// The name of the resource provider.
        /// </summary>
        [JsonPropertyName( "provider_name" )]
        public string ProviderName { get; set; } = "TuneBridge";

        /// <summary>
        /// The URL of the resource provider.
        /// </summary>
        [JsonPropertyName( "provider_url" )]
        public string? ProviderUrl { get; set; }

        /// <summary>
        /// The suggested cache lifetime for this resource, in seconds.
        /// </summary>
        [JsonPropertyName( "cache_age" )]
        public int? CacheAge { get; set; }

        /// <summary>
        /// A URL to a thumbnail image representing the resource.
        /// </summary>
        [JsonPropertyName( "thumbnail_url" )]
        public string? ThumbnailUrl { get; set; }

        /// <summary>
        /// The width of the optional thumbnail in pixels.
        /// </summary>
        [JsonPropertyName( "thumbnail_width" )]
        public int? ThumbnailWidth { get; set; }

        /// <summary>
        /// The height of the optional thumbnail in pixels.
        /// </summary>
        [JsonPropertyName( "thumbnail_height" )]
        public int? ThumbnailHeight { get; set; }

        /// <summary>
        /// The HTML to embed. Required for type "rich".
        /// </summary>
        [JsonPropertyName( "html" )]
        public string? Html { get; set; }

        /// <summary>
        /// The width in pixels required to display the HTML. Required for type "rich".
        /// </summary>
        [JsonPropertyName( "width" )]
        public int? Width { get; set; }

        /// <summary>
        /// The height in pixels required to display the HTML. Required for type "rich".
        /// </summary>
        [JsonPropertyName( "height" )]
        public int? Height { get; set; }
    }
}
