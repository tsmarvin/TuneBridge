using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Exceptions;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Contracts.Records.WorkerApi;

namespace BridgeBeats.Core.Domain.Providers.Common;

/// <summary>
/// Converts worker-proxy HTTP 429 envelopes into the typed provider signal excluded by the shared
/// resilience pipeline.
/// </summary>
internal sealed class WorkerRateLimitHandler(
    SupportedProviders provider,
    QueueSettings queueSettings
) : DelegatingHandler {
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) {
        HttpResponseMessage response = await base.SendAsync( request, cancellationToken );
        if (response.StatusCode != HttpStatusCode.TooManyRequests) {
            return response;
        }

        try {
            ProviderLookupResponse? envelope = null;
            try {
                envelope = await response.Content.ReadFromJsonAsync<ProviderLookupResponse>( cancellationToken );
            } catch (JsonException) {
                // Retry-After remains a valid fallback for workers that cannot serialize an envelope.
            } catch (NotSupportedException) {
                // Treat an unsupported error content type the same as an absent envelope.
            }

            double? retrySeconds = envelope?.RetryAfterSeconds;
            TimeSpan retryAfter = retrySeconds.HasValue && IsValidSeconds( retrySeconds.Value )
                ? TimeSpan.FromSeconds( retrySeconds.Value )
                : RetryAfterLimitHandler.GetRetryAfterValue( response )
                    ?? queueSettings.RateLimitDefaultRetryAfter;
            if (retryAfter < queueSettings.RateLimitMinimumRetryAfter) {
                retryAfter = queueSettings.RateLimitMinimumRetryAfter;
            }

            string? endpoint = envelope?.RateLimitedEndpoint;
            double? thresholdSeconds = envelope?.RetryThresholdSeconds;
            if (thresholdSeconds.HasValue
                && IsValidSeconds( thresholdSeconds.Value )
                && retryAfter > TimeSpan.FromSeconds( thresholdSeconds.Value )) {
                throw new RetryAfterExceededException(
                    retryAfter,
                    TimeSpan.FromSeconds( thresholdSeconds.Value ),
                    request.RequestUri,
                    provider,
                    endpoint );
            }

            throw new ProviderRateLimitException(
                retryAfter,
                request.RequestUri,
                provider,
                endpoint );
        } finally {
            response.Dispose( );
        }
    }

    private static bool IsValidSeconds( double seconds ) =>
        double.IsFinite( seconds )
        && seconds >= 0
        && seconds <= TimeSpan.MaxValue.TotalSeconds;
}
