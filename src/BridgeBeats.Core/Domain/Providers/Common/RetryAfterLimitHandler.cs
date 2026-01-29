using System.Net;
using BridgeBeats.Contracts.Exceptions;

namespace BridgeBeats.Core.Domain.Providers.Common {
    /// <summary>
    /// HTTP message handler that intercepts 429 (Too Many Requests) responses and throws
    /// <see cref="RetryAfterExceededException"/> when the Retry-After header exceeds the configured threshold.
    /// This enables fail-fast behavior instead of waiting for extended rate limit periods.
    /// </summary>
    /// <remarks>
    /// This handler should be registered before the resilience handler in the HTTP client pipeline
    /// to intercept 429 responses before retry logic is applied.
    /// </remarks>
    public class RetryAfterLimitHandler : DelegatingHandler {
        private readonly TimeSpan _maxRetryAfter;
        private readonly ILogger<RetryAfterLimitHandler> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="RetryAfterLimitHandler"/> class.
        /// </summary>
        /// <param name="maxRetryAfterSeconds">Maximum Retry-After value in seconds before failing fast.</param>
        /// <param name="logger">Logger for recording rate limit events.</param>
        public RetryAfterLimitHandler( int maxRetryAfterSeconds, ILogger<RetryAfterLimitHandler> logger ) {
            _maxRetryAfter = TimeSpan.FromSeconds( maxRetryAfterSeconds );
            _logger = logger;
        }

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) {
            HttpResponseMessage response = await base.SendAsync( request, cancellationToken );

            if (response.StatusCode == HttpStatusCode.TooManyRequests) {
                TimeSpan? retryAfter = GetRetryAfterValue( response );

                if (retryAfter.HasValue && retryAfter.Value > _maxRetryAfter) {
                    Uri? requestUri = request.RequestUri;
                    Contracts.Enums.SupportedProviders? provider = RetryAfterExceededException.DetermineProviderFromUri( requestUri );

                    _logger.LogWarning(
                        "Rate limit exceeded threshold for {Provider}. Retry-After: {RetryAfterSeconds}s, " +
                        "Threshold: {ThresholdSeconds}s. Request URI: {RequestUri}. Failing fast instead of waiting.",
                        provider?.ToString( ) ?? "Unknown",
                        retryAfter.Value.TotalSeconds,
                        _maxRetryAfter.TotalSeconds,
                        requestUri
                    );

                    throw new RetryAfterExceededException(
                        retryAfter.Value,
                        _maxRetryAfter,
                        requestUri,
                        provider
                    );
                }
            }

            return response;
        }

        /// <summary>
        /// Extracts the Retry-After value from the response headers.
        /// Handles both delta-seconds and HTTP-date formats.
        /// </summary>
        /// <param name="response">The HTTP response to extract from.</param>
        /// <returns>The Retry-After duration, or null if not present or invalid.</returns>
        private static TimeSpan? GetRetryAfterValue( HttpResponseMessage response ) {
            // Check for delta seconds (e.g., "Retry-After: 120")
            if (response.Headers.RetryAfter?.Delta.HasValue == true) {
                return response.Headers.RetryAfter.Delta;
            }

            // Check for HTTP-date format (e.g., "Retry-After: Wed, 21 Oct 2015 07:28:00 GMT")
            if (response.Headers.RetryAfter?.Date.HasValue == true) {
                DateTimeOffset retryDate = response.Headers.RetryAfter.Date.Value;
                TimeSpan delta = retryDate - DateTimeOffset.UtcNow;
                return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
            }

            return null;
        }
    }
}
