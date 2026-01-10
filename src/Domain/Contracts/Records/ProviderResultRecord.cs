using System.Text.Json.Serialization;

namespace BridgeBeats.Domain.Contracts.Records {

    /// <summary>
    /// Music metadata from a specific provider's API query.
    /// </summary>
    public sealed record ProviderResultRecord {

        /// <summary>
        /// Creates a new instance of <see cref="ProviderResultRecord"/>.
        /// </summary>
        [JsonConstructor]
        public ProviderResultRecord(
            string provider,
            string artist,
            string title,
            string url,
            string marketRegion,
            string? externalId = null,
            string? artUrl = null,
            bool? isAlbum = null
        ) {
            Provider = provider ?? throw new ArgumentNullException( nameof( provider ) );
            Artist = artist ?? throw new ArgumentNullException( nameof( artist ) );
            Title = title ?? throw new ArgumentNullException( nameof( title ) );
            Url = url ?? throw new ArgumentNullException( nameof( url ) );
            MarketRegion = marketRegion ?? throw new ArgumentNullException( nameof( marketRegion ) );
            ExternalId = externalId;
            ArtUrl = artUrl;
            IsAlbum = isAlbum;
        }

        /// <summary>
        /// The streaming platform provider.
        /// </summary>
        [JsonPropertyName( "provider" )]
        [JsonRequired]
        public string Provider { get; init; }

        /// <summary>
        /// Primary artist name for the track or album artist.
        /// </summary>
        [JsonPropertyName( "artist" )]
        [JsonRequired]
        public string Artist { get; init; }

        /// <summary>
        /// Official title of the track or album as listed in the provider's catalog.
        /// </summary>
        [JsonPropertyName( "title" )]
        [JsonRequired]
        public string Title { get; init; }

        /// <summary>
        /// ISRC (for tracks) or UPC (for albums) identifier for cross-platform matching.
        /// </summary>
        [JsonPropertyName( "externalId" )]
        [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
        public string? ExternalId { get; init; }

        /// <summary>
        /// Direct web link to the track or album on the provider's platform.
        /// </summary>
        [JsonPropertyName( "url" )]
        [JsonRequired]
        public string Url { get; init; }

        /// <summary>
        /// URL to the cover artwork image.
        /// </summary>
        [JsonPropertyName( "artUrl" )]
        [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
        public string? ArtUrl { get; init; }

        /// <summary>
        /// ISO 3166-1 alpha-2 country code for the market/storefront.
        /// </summary>
        [JsonPropertyName( "marketRegion" )]
        [JsonRequired]
        public string MarketRegion { get; init; }

        /// <summary>
        /// True for albums/EPs, false for individual tracks.
        /// </summary>
        [JsonPropertyName( "isAlbum" )]
        [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
        public bool? IsAlbum { get; init; }
    }
}
