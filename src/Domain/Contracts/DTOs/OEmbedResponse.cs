namespace BridgeBeats.Domain.Contracts.DTOs {

    /// <summary>
    /// Represents an oEmbed response for embedding media content.
    /// Based on the oEmbed specification: https://oembed.com/
    /// </summary>
    public sealed class OEmbedResponse {

        /// <summary>
        /// The oEmbed version number. Must be "1.0".
        /// </summary>
        public string Version { get; init; } = "1.0";

        /// <summary>
        /// The resource type. Can be "photo", "video", "link", or "rich".
        /// </summary>
        public string Type { get; init; } = "rich";

        /// <summary>
        /// A text title, describing the resource.
        /// </summary>
        public string? Title { get; init; }

        /// <summary>
        /// The name of the author/owner of the resource.
        /// </summary>
        public string? AuthorName { get; init; }

        /// <summary>
        /// A URL for the author/owner of the resource.
        /// </summary>
        public string? AuthorUrl { get; init; }

        /// <summary>
        /// The name of the resource provider.
        /// </summary>
        public string? ProviderName { get; init; }

        /// <summary>
        /// The URL of the resource provider.
        /// </summary>
        public string? ProviderUrl { get; init; }

        /// <summary>
        /// The suggested cache lifetime for this resource, in seconds.
        /// </summary>
        public int? CacheAge { get; init; }

        /// <summary>
        /// A URL to a thumbnail image representing the resource.
        /// </summary>
        public string? ThumbnailUrl { get; init; }

        /// <summary>
        /// The width of the thumbnail image in pixels.
        /// </summary>
        public int? ThumbnailWidth { get; init; }

        /// <summary>
        /// The height of the thumbnail image in pixels.
        /// </summary>
        public int? ThumbnailHeight { get; init; }

        /// <summary>
        /// The HTML required to embed a rich resource.
        /// Required for type "rich".
        /// </summary>
        public string? Html { get; init; }

        /// <summary>
        /// The width in pixels required to display the HTML.
        /// Required for type "rich".
        /// </summary>
        public int? Width { get; init; }

        /// <summary>
        /// The height in pixels required to display the HTML.
        /// Required for type "rich".
        /// </summary>
        public int? Height { get; init; }
    }
}
