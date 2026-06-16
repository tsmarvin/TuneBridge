using System.Diagnostics.Metrics;

namespace BridgeBeats.Core.Domain.Providers.Common;

/// <summary>
/// Holds the <see cref="System.Diagnostics.Metrics.Meter"/> and instruments that record provider
/// API request volume, errors, and latency.
/// </summary>
/// <remarks>
/// All instruments are tagged by provider, endpoint, method, status code, and success so dashboards
/// can break metrics down per dimension, and all instrument names are prefixed with
/// <c>bridgebeats.provider.</c>. Register the meter with <c>.AddMeter("BridgeBeats.Providers")</c>.
/// <see cref="ProviderMetricsHandler"/> feeds these instruments from the HTTP pipeline.
/// </remarks>
public static class ProviderMetrics {

    /// <summary>The meter name (<c>BridgeBeats.Providers</c>) used to register provider instruments.</summary>
    public const string MeterName = "BridgeBeats.Providers";

    /// <summary>The shared meter that owns the provider instruments.</summary>
    public static readonly Meter Meter = new( MeterName, "1.0.0" );

    /// <summary>Tag key for the music provider (spotify, applemusic, tidal).</summary>
    public const string ProviderTag = "provider";

    /// <summary>Tag key for the normalized endpoint path template.</summary>
    public const string EndpointTag = "endpoint";

    /// <summary>Tag key for the HTTP method.</summary>
    public const string MethodTag = "method";

    /// <summary>Tag key for the HTTP status code.</summary>
    public const string StatusCodeTag = "status_code";

    /// <summary>Tag key for the success flag (<c>"true"</c>/<c>"false"</c>).</summary>
    public const string SuccessTag = "success";

    #region Counters

    /// <summary>
    /// Counts every provider API request, tagged by provider, endpoint, method, status code, and success.
    /// </summary>
    public static readonly Counter<long> RequestsTotal = Meter.CreateCounter<long>(
        "bridgebeats.provider.requests.total",
        unit: "{requests}",
        description: "Total number of provider API requests made"
    );

    /// <summary>
    /// Counts provider API request errors (exceptions thrown before a response), tagged by provider,
    /// endpoint, and method.
    /// </summary>
    public static readonly Counter<long> ErrorsTotal = Meter.CreateCounter<long>(
        "bridgebeats.provider.errors.total",
        unit: "{errors}",
        description: "Total number of provider API request errors"
    );

    #endregion

    #region Histograms

    /// <summary>
    /// Records provider API request duration in seconds, tagged by provider, endpoint, method, status
    /// code, and success.
    /// </summary>
    public static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "bridgebeats.provider.request.duration",
        unit: "s",
        description: "Duration of provider API requests"
    );

    #endregion

    #region Helper Methods

    /// <summary>
    /// Records a completed provider request, incrementing <see cref="RequestsTotal"/> and recording its
    /// duration on <see cref="RequestDuration"/>.
    /// </summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="endpoint">The normalized endpoint the request targeted.</param>
    /// <param name="method">The HTTP method used.</param>
    /// <param name="statusCode">The HTTP status code returned; a 2xx code is recorded as success.</param>
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
    /// Records a failed provider request, incrementing <see cref="ErrorsTotal"/> and recording its
    /// duration on <see cref="RequestDuration"/> with an <c>error</c> status tag.
    /// </summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="endpoint">The normalized endpoint the request targeted.</param>
    /// <param name="method">The HTTP method used.</param>
    /// <param name="durationSeconds">The elapsed time before the failure, in seconds.</param>
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
