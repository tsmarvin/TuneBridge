namespace BridgeBeats.Core.Infrastructure.Queue;

/// <summary>
/// Standard tag keys for queue-related OpenTelemetry metrics.
/// </summary>
/// <remarks>
/// Use these constants when recording metrics to ensure consistent tag naming
/// across all queue instrumentation points.
/// </remarks>
public static class QueueMetricTags {
    /// <summary>
    /// The music provider (spotify, applemusic, tidal).
    /// </summary>
    public const string Provider = "provider";

    /// <summary>
    /// The queue priority level (interactive, background, bulk).
    /// </summary>
    public const string Priority = "priority";

    /// <summary>
    /// The API endpoint or lookup type being processed.
    /// </summary>
    public const string Endpoint = "endpoint";

    /// <summary>
    /// The type of lookup operation (IsrcLookup, UpcLookup, etc.).
    /// </summary>
    public const string LookupType = "lookup_type";

    /// <summary>
    /// The processing status (success, failure, rate_limited).
    /// </summary>
    public const string Status = "status";

    /// <summary>
    /// HTTP status code for provider API requests.
    /// </summary>
    public const string StatusCode = "status_code";

    /// <summary>
    /// The HTTP method used for provider API requests.
    /// </summary>
    public const string Method = "method";
}
