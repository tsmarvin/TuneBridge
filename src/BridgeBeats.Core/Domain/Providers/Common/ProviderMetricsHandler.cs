using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BridgeBeats.Core.Domain.Providers.Common;

/// <summary>
/// <see cref="DelegatingHandler"/> that times each outbound provider request and records the outcome
/// to <see cref="ProviderMetrics"/>.
/// </summary>
/// <remarks>
/// Before recording, the request URI is normalized so high-cardinality values (entity ids, ISRCs,
/// UPCs, storefronts) collapse into placeholders, keeping metric cardinality bounded. Normalization
/// rules are chosen per provider based on the handler's provider name.
/// </remarks>
public sealed partial class ProviderMetricsHandler : DelegatingHandler {

    private readonly string _providerName;

    // Known Spotify ID patterns (22-character base62)
    /// <summary>Builds the compiled regex matching a 22-character Spotify base-62 id segment.</summary>
    /// <returns>The compiled regex.</returns>
    [GeneratedRegex( @"/([0-9A-Za-z]{22})", RegexOptions.Compiled )]
    private static partial Regex SpotifyIdRegex( );

    // ISRC pattern: 2 letter country + 3 letter registrant + 2 digit year + 5 digit designation
    /// <summary>Builds the compiled regex matching an ISRC.</summary>
    /// <returns>The compiled regex.</returns>
    [GeneratedRegex( @"[A-Z]{2}[A-Z0-9]{3}\d{2}\d{5}", RegexOptions.Compiled | RegexOptions.IgnoreCase )]
    private static partial Regex IsrcRegex( );

    // UPC/EAN patterns: 12-14 digit barcodes
    /// <summary>Builds the compiled regex matching a 12-14 digit UPC.</summary>
    /// <returns>The compiled regex.</returns>
    [GeneratedRegex( @"\b\d{12,14}\b", RegexOptions.Compiled )]
    private static partial Regex UpcRegex( );

    // Apple Music numeric IDs
    /// <summary>Builds the compiled regex matching an Apple Music numeric id segment.</summary>
    /// <returns>The compiled regex.</returns>
    [GeneratedRegex( @"/(\d{6,12})(?:/|$|\?)", RegexOptions.Compiled )]
    private static partial Regex AppleMusicIdRegex( );

    // Tidal numeric IDs
    /// <summary>Builds the compiled regex matching a Tidal numeric id segment.</summary>
    /// <returns>The compiled regex.</returns>
    [GeneratedRegex( @"/(\d+)(?:/|$|\?)", RegexOptions.Compiled )]
    private static partial Regex TidalIdRegex( );

    // Query parameter values that look like IDs
    /// <summary>Builds the compiled regex matching a long id carried in a query-string value.</summary>
    /// <returns>The compiled regex.</returns>
    [GeneratedRegex( @"(=)([A-Za-z0-9]{15,})", RegexOptions.Compiled )]
    private static partial Regex QueryIdRegex( );

    // Storefront in Apple Music catalog paths
    /// <summary>Builds the compiled regex matching an Apple Music <c>catalog/{storefront}/</c> segment.</summary>
    /// <returns>The compiled regex.</returns>
    [GeneratedRegex( @"catalog/[a-z]{2}/", RegexOptions.Compiled )]
    private static partial Regex StorefrontRegex( );

    // Multiple consecutive forward slashes
    /// <summary>Builds the compiled regex matching runs of consecutive slashes.</summary>
    /// <returns>The compiled regex.</returns>
    [GeneratedRegex( @"/+", RegexOptions.Compiled )]
    private static partial Regex MultipleSlashRegex( );

    // Normalizes Spotify bulk ids= query parameters to a stable placeholder to prevent unbounded tag cardinality.
    /// <summary>Builds the compiled regex matching a Spotify bulk <c>ids=</c> query parameter.</summary>
    /// <returns>The compiled regex.</returns>
    [GeneratedRegex( @"(\?|&)ids=[^&]+", RegexOptions.Compiled )]
    private static partial Regex SpotifyBulkIdsRegex( );

    /// <summary>
    /// Initializes the handler for a given provider.
    /// </summary>
    /// <param name="providerName">The provider name; lowercased, used to select normalization rules and tag metrics.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="providerName"/> is <see langword="null"/>, empty, or whitespace.</exception>
    public ProviderMetricsHandler( string providerName ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( providerName );
        _providerName = providerName.ToLowerInvariant( );
    }

    /// <summary>
    /// Times the inner request and records it to <see cref="ProviderMetrics"/> on completion or failure.
    /// </summary>
    /// <param name="request">The outbound request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The response from the inner handler.</returns>
    /// <exception cref="Exception">Any exception from the inner handler is recorded as an error and re-thrown.</exception>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) {
        string endpoint = NormalizeEndpoint( request.RequestUri );
        string method = request.Method.Method;
        Stopwatch stopwatch = Stopwatch.StartNew( );

        try {
            HttpResponseMessage response = await base.SendAsync( request, cancellationToken );

            stopwatch.Stop( );
            ProviderMetrics.RecordRequest(
                _providerName,
                endpoint,
                method,
                (int)response.StatusCode,
                stopwatch.Elapsed.TotalSeconds
            );

            return response;
        } catch (Exception) {
            stopwatch.Stop( );
            ProviderMetrics.RecordError(
                _providerName,
                endpoint,
                method,
                stopwatch.Elapsed.TotalSeconds
            );
            throw;
        }
    }

    /// <summary>
    /// Reduces a request URI to a low-cardinality endpoint label by replacing ids, ISRCs, UPCs, and
    /// storefronts with placeholders, choosing the rule set from the handler's provider.
    /// </summary>
    /// <param name="uri">The request URI to normalize.</param>
    /// <returns>
    /// A normalized endpoint label; <c>"unknown"</c> when <paramref name="uri"/> is <see langword="null"/>,
    /// or <c>"root"</c> when nothing remains after normalization.
    /// </returns>
    internal string NormalizeEndpoint( Uri? uri ) {
        if (uri is null) {
            return "unknown";
        }

        // Get path and query
        string pathAndQuery = uri.PathAndQuery;

        // Remove base API path prefixes to keep endpoint names concise
        pathAndQuery = RemoveBaseApiPrefix( pathAndQuery );

        // Apply provider-specific ID normalization
        string normalized = _providerName switch {
            "spotify" => NormalizeSpotifyEndpoint( pathAndQuery ),
            "applemusic" => NormalizeAppleMusicEndpoint( pathAndQuery ),
            "tidal" => NormalizeTidalEndpoint( pathAndQuery ),
            _ => NormalizeGenericEndpoint( pathAndQuery )
        };

        // Clean up multiple slashes and trailing slashes
        normalized = CleanupPath( normalized );

        return normalized;
    }

    /// <summary>Strips a leading API version or <c>api</c> prefix from a path.</summary>
    /// <param name="path">The path-and-query to trim.</param>
    /// <returns>The path with a recognized prefix and any leading slash removed.</returns>
    private static string RemoveBaseApiPrefix( string path ) {
        // Remove common API version prefixes
        string[] prefixes = ["/v1/", "/v2/", "/api/"];
        foreach (string prefix in prefixes) {
            if (path.StartsWith( prefix, StringComparison.OrdinalIgnoreCase )) {
                path = path[prefix.Length..];
                break;
            }
        }
        return path.TrimStart( '/' );
    }

    /// <summary>Replaces Spotify bulk id lists, single ids, ISRCs, and UPCs with placeholders.</summary>
    /// <param name="path">The path-and-query to normalize.</param>
    /// <returns>The normalized path.</returns>
    private static string NormalizeSpotifyEndpoint( string path ) {
        // Normalize the bulk-endpoint ids= parameter first so the stable placeholder
        // (e.g. tracks?ids={ids}) is in place before the per-ID regex runs.
        // Without this step each unique comma-separated ID list produces a distinct
        // endpoint tag value, causing unbounded metric series growth (CR-M-1).
        path = SpotifyBulkIdsRegex( ).Replace( path, "$1ids={ids}" );

        // Replace Spotify IDs (22-character base62 strings) in path segments
        path = SpotifyIdRegex( ).Replace( path, "/{id}" );

        // Replace ISRCs in query params
        path = IsrcRegex( ).Replace( path, "{isrc}" );

        // Replace UPCs
        path = UpcRegex( ).Replace( path, "{upc}" );

        return path;
    }

    /// <summary>Replaces Apple Music storefronts, ids, ISRCs, and UPCs with placeholders.</summary>
    /// <param name="path">The path-and-query to normalize.</param>
    /// <returns>The normalized path.</returns>
    private static string NormalizeAppleMusicEndpoint( string path ) {
        // Remove storefront from path (e.g., /catalog/us/ -> /catalog/)
        path = StorefrontRegex( ).Replace( path, "catalog/{storefront}/" );

        // Replace numeric IDs
        path = AppleMusicIdRegex( ).Replace( path, "/{id}$2" );

        // Replace ISRCs
        path = IsrcRegex( ).Replace( path, "{isrc}" );

        // Replace UPCs
        path = UpcRegex( ).Replace( path, "{upc}" );

        return path;
    }

    /// <summary>Replaces Tidal ids, ISRCs, and UPCs with placeholders.</summary>
    /// <param name="path">The path-and-query to normalize.</param>
    /// <returns>The normalized path.</returns>
    private static string NormalizeTidalEndpoint( string path ) {
        // Replace numeric IDs
        path = TidalIdRegex( ).Replace( path, "/{id}$2" );

        // Replace ISRCs
        path = IsrcRegex( ).Replace( path, "{isrc}" );

        // Replace UPCs/barcodes
        path = UpcRegex( ).Replace( path, "{upc}" );

        return path;
    }

    /// <summary>Replaces ISRCs, UPCs, and query-string ids with placeholders for unknown providers.</summary>
    /// <param name="path">The path-and-query to normalize.</param>
    /// <returns>The normalized path.</returns>
    private static string NormalizeGenericEndpoint( string path ) {
        // Replace ISRCs
        path = IsrcRegex( ).Replace( path, "{isrc}" );

        // Replace UPCs
        path = UpcRegex( ).Replace( path, "{upc}" );

        // Replace long alphanumeric IDs in query params
        path = QueryIdRegex( ).Replace( path, "$1{id}" );

        return path;
    }

    /// <summary>
    /// Collapses duplicate slashes, trims leading and trailing slashes, and caps length to keep the
    /// endpoint label compact.
    /// </summary>
    /// <param name="path">The normalized path to tidy.</param>
    /// <returns>The cleaned path, or <c>"root"</c> when nothing remains.</returns>
    private static string CleanupPath( string path ) {
        // Remove multiple consecutive slashes
        path = MultipleSlashRegex( ).Replace( path, "/" );

        // Remove trailing slash
        path = path.TrimEnd( '/' );

        // Remove leading slash
        path = path.TrimStart( '/' );

        // Limit length to prevent cardinality explosion
        if (path.Length > 100) {
            path = path[..100] + "...";
        }

        return string.IsNullOrEmpty( path ) ? "root" : path;
    }
}
