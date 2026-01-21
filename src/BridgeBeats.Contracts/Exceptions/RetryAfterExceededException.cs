using BridgeBeats.Contracts.Enums;

namespace BridgeBeats.Contracts.Exceptions {
    /// <summary>
    /// Exception thrown when a music provider returns HTTP 429 with a Retry-After header
    /// that exceeds the configured maximum threshold. This allows fail-fast behavior
    /// instead of waiting for extended periods.
    /// </summary>
    /// <remarks>
    /// This exception is designed to capture request context for future re-queue functionality.
    /// The exception is excluded from the resilience handler's retry logic to ensure immediate propagation.
    /// </remarks>
    public class RetryAfterExceededException : Exception {
        /// <summary>
        /// The Retry-After value from the response header, in seconds.
        /// </summary>
        public TimeSpan RetryAfterValue { get; }

        /// <summary>
        /// The configured maximum threshold that was exceeded, in seconds.
        /// </summary>
        public TimeSpan Threshold { get; }

        /// <summary>
        /// The request URI that triggered the rate limit response.
        /// </summary>
        public Uri? RequestUri { get; }

        /// <summary>
        /// The music provider that returned the rate limit response.
        /// Extracted from the request URI host when available.
        /// </summary>
        public SupportedProviders? Provider { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="RetryAfterExceededException"/> class.
        /// </summary>
        /// <param name="retryAfterValue">The Retry-After value from the response header.</param>
        /// <param name="threshold">The configured maximum threshold.</param>
        /// <param name="requestUri">The request URI that triggered the rate limit.</param>
        /// <param name="provider">The music provider that returned the rate limit.</param>
        public RetryAfterExceededException(
            TimeSpan retryAfterValue,
            TimeSpan threshold,
            Uri? requestUri,
            SupportedProviders? provider
        ) : base( BuildMessage( retryAfterValue, threshold, requestUri, provider ) ) {
            RetryAfterValue = retryAfterValue;
            Threshold = threshold;
            RequestUri = requestUri;
            Provider = provider;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RetryAfterExceededException"/> class with an inner exception.
        /// </summary>
        /// <param name="retryAfterValue">The Retry-After value from the response header.</param>
        /// <param name="threshold">The configured maximum threshold.</param>
        /// <param name="requestUri">The request URI that triggered the rate limit.</param>
        /// <param name="provider">The music provider that returned the rate limit.</param>
        /// <param name="innerException">The inner exception.</param>
        public RetryAfterExceededException(
            TimeSpan retryAfterValue,
            TimeSpan threshold,
            Uri? requestUri,
            SupportedProviders? provider,
            Exception innerException
        ) : base( BuildMessage( retryAfterValue, threshold, requestUri, provider ), innerException ) {
            RetryAfterValue = retryAfterValue;
            Threshold = threshold;
            RequestUri = requestUri;
            Provider = provider;
        }

        private static string BuildMessage(
            TimeSpan retryAfterValue,
            TimeSpan threshold,
            Uri? requestUri,
            SupportedProviders? provider
        ) {
            string providerName = provider?.ToString( ) ?? "Unknown";
            string uri = requestUri?.ToString( ) ?? "Unknown";
            return $"Rate limit exceeded for {providerName}. Retry-After of {retryAfterValue.TotalSeconds:F0} seconds " +
                   $"exceeds maximum threshold of {threshold.TotalSeconds:F0} seconds. Request URI: {uri}";
        }

        /// <summary>
        /// Determines the music provider from a request URI based on known API host patterns.
        /// </summary>
        /// <param name="requestUri">The request URI to analyze.</param>
        /// <returns>The identified provider, or null if not recognized.</returns>
        public static SupportedProviders? DetermineProviderFromUri( Uri? requestUri ) {
            if (requestUri == null) {
                return null;
            }

            string host = requestUri.Host.ToLowerInvariant( );
            return host switch {
                _ when host.Contains( "apple" ) => SupportedProviders.AppleMusic,
                _ when host.Contains( "spotify" ) => SupportedProviders.Spotify,
                _ when host.Contains( "tidal" ) => SupportedProviders.Tidal,
                _ => null
            };
        }
    }
}
