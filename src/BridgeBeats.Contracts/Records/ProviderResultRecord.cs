using System.Text.Json.Serialization;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// Music metadata from a specific provider's API query: one provider's persisted result row
/// within a <see cref="MediaLinkResultRecord"/>.
/// </summary>
/// <remarks>
/// <see cref="Provider"/> is stored as a string here, unlike the in-memory result aggregate
/// which keys provider results on the <c>SupportedProviders</c> enum.
/// </remarks>
public sealed record ProviderResultRecord {

    /// <summary>Initializes a result row, null-checking the required fields. Used by JSON deserialization.</summary>
    /// <param name="provider">The streaming platform provider this row belongs to.</param>
    /// <param name="artist">The primary artist name for the track or album artist.</param>
    /// <param name="title">The official title of the track or album as listed in the provider's catalog.</param>
    /// <param name="url">The direct web link to the track or album on the provider's platform.</param>
    /// <param name="marketRegion">The ISO 3166-1 alpha-2 country code for the market or storefront.</param>
    /// <param name="externalId">The ISRC (for tracks) or UPC (for albums) identifier for cross-platform matching, when known; otherwise <see langword="null"/>.</param>
    /// <param name="artUrl">The cover-artwork image URL, when available; otherwise <see langword="null"/>.</param>
    /// <param name="isAlbum"><see langword="true"/> for an album or EP, <see langword="false"/> for an individual track, or <see langword="null"/> when unspecified.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="provider"/>, <paramref name="artist"/>, <paramref name="title"/>, <paramref name="url"/>, or <paramref name="marketRegion"/> is <see langword="null"/>.</exception>
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

    /// <summary>The streaming platform provider (stored as a string).</summary>
    [JsonPropertyName( "provider" )]
    [JsonRequired]
    public string Provider { get; init; }

    /// <summary>The primary artist name for the track or album artist.</summary>
    [JsonPropertyName( "artist" )]
    [JsonRequired]
    public string Artist { get; init; }

    /// <summary>The official title of the track or album as listed in the provider's catalog.</summary>
    [JsonPropertyName( "title" )]
    [JsonRequired]
    public string Title { get; init; }

    /// <summary>The ISRC (for tracks) or UPC (for albums) identifier for cross-platform matching, when known; otherwise <see langword="null"/>.</summary>
    [JsonPropertyName( "externalId" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? ExternalId { get; init; }

    /// <summary>The direct web link to the track or album on the provider's platform.</summary>
    [JsonPropertyName( "url" )]
    [JsonRequired]
    public string Url { get; init; }

    /// <summary>The URL to the cover artwork image, when available; otherwise <see langword="null"/>.</summary>
    [JsonPropertyName( "artUrl" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? ArtUrl { get; init; }

    /// <summary>The ISO 3166-1 alpha-2 country code for the market or storefront.</summary>
    [JsonPropertyName( "marketRegion" )]
    [JsonRequired]
    public string MarketRegion { get; init; }

    /// <summary><see langword="true"/> for albums and EPs, <see langword="false"/> for individual tracks, or <see langword="null"/> when unspecified.</summary>
    [JsonPropertyName( "isAlbum" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public bool? IsAlbum { get; init; }
}
