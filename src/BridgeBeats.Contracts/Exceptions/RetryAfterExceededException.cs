using BridgeBeats.Contracts.Enums;

namespace BridgeBeats.Contracts.Exceptions {

    /// <summary>
    /// Thrown when a provider's <c>Retry-After</c> value exceeds the configured retry threshold,
    /// meaning the rate-limit wait is too long to be worth retrying. Carries the offending
    /// <c>Retry-After</c> value, the threshold it breached, and the request context.
    /// </summary>
    /// <remarks>
    /// The captured request context drives rate-limit handling and requeue of the affected work. The
    /// resilience handler is configured not to retry this exception, so it propagates immediately
    /// rather than waiting out the rate limit.
    /// </remarks>
    public class RetryAfterExceededException : Exception {

        /// <summary>
        /// The provider's requested <c>Retry-After</c> wait duration that triggered this exception.
        /// </summary>
        public TimeSpan RetryAfterValue { get; }

        /// <summary>
        /// The maximum retry wait the caller is willing to tolerate. When
        /// <see cref="RetryAfterValue"/> exceeds this, the exception is raised.
        /// </summary>
        public TimeSpan Threshold { get; }

        /// <summary>
        /// The request URI that was rate-limited, if known; otherwise <see langword="null"/>.
        /// </summary>
        public Uri? RequestUri { get; }

        /// <summary>
        /// The provider that issued the rate limit, if known; otherwise <see langword="null"/>.
        /// Typically inferred from the request URI host via <see cref="DetermineProviderFromUri"/>.
        /// </summary>
        public SupportedProviders? Provider { get; }

        /// <summary>
        /// Initializes a new instance with the rate-limit context, building a descriptive message.
        /// </summary>
        /// <param name="retryAfterValue">The provider's requested <c>Retry-After</c> wait.</param>
        /// <param name="threshold">The maximum retry wait the caller will tolerate.</param>
        /// <param name="requestUri">The rate-limited request URI, or <see langword="null"/> if unknown.</param>
        /// <param name="provider">The provider that issued the limit, or <see langword="null"/> if unknown.</param>
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
        /// Initializes a new instance with the rate-limit context and an inner exception that
        /// caused or accompanied the rate limit.
        /// </summary>
        /// <param name="retryAfterValue">The provider's requested <c>Retry-After</c> wait.</param>
        /// <param name="threshold">The maximum retry wait the caller will tolerate.</param>
        /// <param name="requestUri">The rate-limited request URI, or <see langword="null"/> if unknown.</param>
        /// <param name="provider">The provider that issued the limit, or <see langword="null"/> if unknown.</param>
        /// <param name="innerException">The exception that is the cause of this exception.</param>
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
        /// Infers the <see cref="SupportedProviders"/> from a request URI by matching a substring of
        /// its host (<c>apple</c>, <c>spotify</c>, or <c>tidal</c>).
        /// </summary>
        /// <param name="requestUri">The request URI to inspect.</param>
        /// <returns>
        /// The matching provider, or <see langword="null"/> if <paramref name="requestUri"/> is
        /// <see langword="null"/> or its host matches no known provider.
        /// </returns>
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
