using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BridgeBeats.Core.Domain.Providers.Common;

/// <summary>
/// HTTP message handler that records OpenTelemetry metrics for all provider API requests.
/// </summary>
/// <remarks>
/// <para>
/// This handler intercepts all HTTP requests to provider APIs and records:
/// <list type="bullet">
///   <item>Request count with provider, endpoint, method, status code tags</item>
///   <item>Request duration histogram</item>
///   <item>Error count for failed requests</item>
/// </list>
/// </para>
/// <para>
/// Endpoint paths are normalized to replace dynamic segments (IDs, ISRCs, etc.)
/// with placeholders to reduce cardinality.
/// </para>
/// </remarks>
public sealed partial class ProviderMetricsHandler : DelegatingHandler {

    private readonly string _providerName;

    // Known Spotify ID patterns (22-character base62)
    [GeneratedRegex( @"/([0-9A-Za-z]{22})", RegexOptions.Compiled )]
    private static partial Regex SpotifyIdRegex( );

    // ISRC pattern: 2 letter country + 3 letter registrant + 2 digit year + 5 digit designation
    [GeneratedRegex( @"[A-Z]{2}[A-Z0-9]{3}\d{2}\d{5}", RegexOptions.Compiled | RegexOptions.IgnoreCase )]
    private static partial Regex IsrcRegex( );

    // UPC/EAN patterns: 12-14 digit barcodes
    [GeneratedRegex( @"\b\d{12,14}\b", RegexOptions.Compiled )]
    private static partial Regex UpcRegex( );

    // Apple Music numeric IDs
    [GeneratedRegex( @"/(\d{6,12})(?:/|$|\?)", RegexOptions.Compiled )]
    private static partial Regex AppleMusicIdRegex( );

    // Tidal numeric IDs
    [GeneratedRegex( @"/(\d+)(?:/|$|\?)", RegexOptions.Compiled )]
    private static partial Regex TidalIdRegex( );

    // Query parameter values that look like IDs
    [GeneratedRegex( @"(=)([A-Za-z0-9]{15,})", RegexOptions.Compiled )]
    private static partial Regex QueryIdRegex( );

    // Storefront in Apple Music catalog paths
    [GeneratedRegex( @"catalog/[a-z]{2}/", RegexOptions.Compiled )]
    private static partial Regex StorefrontRegex( );

    // Multiple consecutive forward slashes
    [GeneratedRegex( @"/+", RegexOptions.Compiled )]
    private static partial Regex MultipleSlashRegex( );

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderMetricsHandler"/> class.
    /// </summary>
    /// <param name="providerName">The name of the provider (e.g., "spotify", "applemusic", "tidal").</param>
    public ProviderMetricsHandler( string providerName ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( providerName );
        _providerName = providerName.ToLowerInvariant( );
    }

    /// <inheritdoc/>
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
    /// Normalizes an endpoint URI by replacing dynamic segments with placeholders.
    /// </summary>
    /// <param name="uri">The request URI.</param>
    /// <returns>A normalized endpoint template string.</returns>
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

    private static string NormalizeSpotifyEndpoint( string path ) {
        // Replace Spotify IDs (22-character base62 strings)
        path = SpotifyIdRegex( ).Replace( path, "/{id}" );

        // Replace ISRCs in query params
        path = IsrcRegex( ).Replace( path, "{isrc}" );

        // Replace UPCs
        path = UpcRegex( ).Replace( path, "{upc}" );

        return path;
    }

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

    private static string NormalizeTidalEndpoint( string path ) {
        // Replace numeric IDs
        path = TidalIdRegex( ).Replace( path, "/{id}$2" );

        // Replace ISRCs
        path = IsrcRegex( ).Replace( path, "{isrc}" );

        // Replace UPCs/barcodes
        path = UpcRegex( ).Replace( path, "{upc}" );

        return path;
    }

    private static string NormalizeGenericEndpoint( string path ) {
        // Replace ISRCs
        path = IsrcRegex( ).Replace( path, "{isrc}" );

        // Replace UPCs
        path = UpcRegex( ).Replace( path, "{upc}" );

        // Replace long alphanumeric IDs in query params
        path = QueryIdRegex( ).Replace( path, "$1{id}" );

        return path;
    }

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
