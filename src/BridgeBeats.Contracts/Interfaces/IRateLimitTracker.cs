using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Tracks per-provider, per-endpoint rate-limit windows: read the current state, mark an
/// endpoint rate-limited until an instant, clear a limit, and list a provider's active limits.
/// </summary>
/// <remarks>
/// Implemented in <c>BridgeBeats.Core</c> by <c>RedisRateLimitTracker</c>
/// (<c>Infrastructure/Queue/RedisRateLimitTracker.cs</c>), so state is shared across all service
/// instances. Limits are tracked independently per provider-and-endpoint combination, so one
/// endpoint can be blocked while others on the same provider stay available. The
/// <c>retryAfter</c> values are absolute instants ("retry no earlier than"), not durations.
/// </remarks>
public interface IRateLimitTracker {

    /// <summary>
    /// Reads the current rate-limit state for a provider endpoint.
    /// </summary>
    /// <param name="provider">The provider to query.</param>
    /// <param name="endpoint">The API endpoint path within the provider to query (for example <c>/v1/tracks</c>).</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task whose result is the current <see cref="RateLimitState"/> for the endpoint.</returns>
    Task<RateLimitState> GetStateAsync( SupportedProviders provider, string endpoint, CancellationToken cancellationToken = default );

    /// <summary>
    /// Marks a provider endpoint as rate-limited until the given absolute instant.
    /// </summary>
    /// <param name="provider">The provider that returned the rate limit.</param>
    /// <param name="endpoint">The API endpoint that was rate-limited.</param>
    /// <param name="retryAfter">The absolute instant before which the endpoint should not be retried.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the limit has been recorded.</returns>
    Task SetRateLimitedAsync( SupportedProviders provider, string endpoint, DateTimeOffset retryAfter, CancellationToken cancellationToken = default );

    /// <summary>
    /// Clears any recorded rate limit for a provider endpoint, for example after a successful
    /// request.
    /// </summary>
    /// <param name="provider">The provider whose limit is cleared.</param>
    /// <param name="endpoint">The API endpoint whose limit is cleared.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the limit has been cleared.</returns>
    Task ClearAsync( SupportedProviders provider, string endpoint, CancellationToken cancellationToken = default );

    /// <summary>
    /// Lists every endpoint currently rate-limited for a provider, with each endpoint's
    /// retry-after instant.
    /// </summary>
    /// <param name="provider">The provider to list active limits for.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// A task whose result is the currently rate-limited endpoints; an empty list when none are
    /// limited.
    /// </returns>
    Task<IReadOnlyList<RateLimitedEndpoint>> GetAllRateLimitedAsync( SupportedProviders provider, CancellationToken cancellationToken = default );
}
