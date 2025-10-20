using System.Text.Json.Serialization;
using idunno.AtProto.Repo;

namespace TuneBridge.Domain.Contracts.Records {

    /// <summary>
    /// AT Protocol record for TuneBridge MediaLinkResult.
    /// Corresponds to the media.tunebridge.lookup.result lexicon.
    /// Note: Input links are tracked only in SQLite for privacy - not stored on PDS.
    /// </summary>
    public sealed record MediaLinkResultRecord : AtProtoRecord {

        /// <summary>
        /// Creates a new instance of <see cref="MediaLinkResultRecord"/>.
        /// </summary>
        public MediaLinkResultRecord( ) : base( ) {
        }

        /// <summary>
        /// Creates a new instance of <see cref="MediaLinkResultRecord"/>.
        /// </summary>
        /// <param name="results">Collection of lookup results from each provider.</param>
        /// <param name="lookedUpAt">ISO 8601 timestamp of when this lookup was performed.</param>
        [JsonConstructor]
        public MediaLinkResultRecord(
            ICollection<ProviderResultRecord> results,
            DateTimeOffset lookedUpAt
        ) : base( ) {
            Results = results ?? throw new ArgumentNullException( nameof( results ) );
            LookedUpAt = lookedUpAt;
        }

        /// <summary>
        /// Collection of lookup results from each provider that returned a match.
        /// </summary>
        [JsonPropertyName( "results" )]
        [JsonRequired]
        public ICollection<ProviderResultRecord> Results { get; init; } = [];

        /// <summary>
        /// ISO 8601 timestamp of when this lookup was performed.
        /// </summary>
        [JsonPropertyName( "lookedUpAt" )]
        [JsonRequired]
        public DateTimeOffset LookedUpAt { get; init; }
    }

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
