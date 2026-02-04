using System.Diagnostics.Metrics;

namespace BridgeBeats.Core.Domain.Providers.Common;

/// <summary>
/// OpenTelemetry metrics for provider API requests.
/// </summary>
/// <remarks>
/// <para>
/// All metrics are prefixed with <c>bridgebeats.provider.</c> and include appropriate
/// tags for filtering by provider, endpoint, method, and status code.
/// </para>
/// <para>
/// Register this meter with OpenTelemetry using <c>.AddMeter("BridgeBeats.Providers")</c>
/// in your OpenTelemetry configuration.
/// </para>
/// </remarks>
public static class ProviderMetrics {

    /// <summary>
    /// The meter name for provider metrics.
    /// </summary>
    public const string MeterName = "BridgeBeats.Providers";

    /// <summary>
    /// The meter for provider API request metrics.
    /// </summary>
    public static readonly Meter Meter = new( MeterName, "1.0.0" );

    /// <summary>
    /// Tag key for the music provider (spotify, applemusic, tidal).
    /// </summary>
    public const string ProviderTag = "provider";

    /// <summary>
    /// Tag key for the normalized endpoint path template.
    /// </summary>
    public const string EndpointTag = "endpoint";

    /// <summary>
    /// Tag key for the HTTP method used.
    /// </summary>
    public const string MethodTag = "method";

    /// <summary>
    /// Tag key for the HTTP response status code.
    /// </summary>
    public const string StatusCodeTag = "status_code";

    /// <summary>
    /// Tag key for whether the request was successful.
    /// </summary>
    public const string SuccessTag = "success";

    #region Counters

    /// <summary>
    /// Total number of provider API requests made.
    /// </summary>
    /// <remarks>
    /// Tags: provider, endpoint, method, status_code, success
    /// </remarks>
    public static readonly Counter<long> RequestsTotal = Meter.CreateCounter<long>(
        "bridgebeats.provider.requests.total",
        unit: "{requests}",
        description: "Total number of provider API requests made"
    );

    /// <summary>
    /// Total number of provider API request errors (exceptions thrown).
    /// </summary>
    /// <remarks>
    /// Tags: provider, endpoint, method
    /// </remarks>
    public static readonly Counter<long> ErrorsTotal = Meter.CreateCounter<long>(
        "bridgebeats.provider.errors.total",
        unit: "{errors}",
        description: "Total number of provider API request errors"
    );

    #endregion

    #region Histograms

    /// <summary>
    /// Duration of provider API requests.
    /// </summary>
    /// <remarks>
    /// Tags: provider, endpoint, method, status_code, success
    /// </remarks>
    public static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "bridgebeats.provider.request.duration",
        unit: "s",
        description: "Duration of provider API requests"
    );

    #endregion

    #region Helper Methods

    /// <summary>
    /// Records a successful provider API request.
    /// </summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="endpoint">The normalized endpoint template.</param>
    /// <param name="method">The HTTP method used.</param>
    /// <param name="statusCode">The HTTP response status code.</param>
    /// <param name="durationSeconds">The request duration in seconds.</param>
    public static void RecordRequest(
        string provider,
        string endpoint,
        string method,
        int statusCode,
        double durationSeconds
    ) {
        bool success = statusCode is >= 200 and < 300;

        RequestsTotal.Add(
            1,
            new KeyValuePair<string, object?>( ProviderTag, provider ),
            new KeyValuePair<string, object?>( EndpointTag, endpoint ),
            new KeyValuePair<string, object?>( MethodTag, method ),
            new KeyValuePair<string, object?>( StatusCodeTag, statusCode.ToString( ) ),
            new KeyValuePair<string, object?>( SuccessTag, success.ToString( ).ToLowerInvariant( ) )
        );

        RequestDuration.Record(
            durationSeconds,
            new KeyValuePair<string, object?>( ProviderTag, provider ),
            new KeyValuePair<string, object?>( EndpointTag, endpoint ),
            new KeyValuePair<string, object?>( MethodTag, method ),
            new KeyValuePair<string, object?>( StatusCodeTag, statusCode.ToString( ) ),
            new KeyValuePair<string, object?>( SuccessTag, success.ToString( ).ToLowerInvariant( ) )
        );
    }

    /// <summary>
    /// Records a provider API request error (exception thrown before response received).
    /// </summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="endpoint">The normalized endpoint template.</param>
    /// <param name="method">The HTTP method used.</param>
    /// <param name="durationSeconds">The request duration in seconds.</param>
    public static void RecordError(
        string provider,
        string endpoint,
        string method,
        double durationSeconds
    ) {
        ErrorsTotal.Add(
            1,
            new KeyValuePair<string, object?>( ProviderTag, provider ),
            new KeyValuePair<string, object?>( EndpointTag, endpoint ),
            new KeyValuePair<string, object?>( MethodTag, method )
        );

        // Also record in the duration histogram with a special status code
        RequestDuration.Record(
            durationSeconds,
            new KeyValuePair<string, object?>( ProviderTag, provider ),
            new KeyValuePair<string, object?>( EndpointTag, endpoint ),
            new KeyValuePair<string, object?>( MethodTag, method ),
            new KeyValuePair<string, object?>( StatusCodeTag, "error" ),
            new KeyValuePair<string, object?>( SuccessTag, "false" )
        );
    }

    #endregion
}
