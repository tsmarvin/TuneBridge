using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Tracks per-endpoint rate limit state across all service instances.
/// </summary>
/// <remarks>
/// Rate limits are tracked independently per provider and endpoint combination.
/// This allows fine-grained control over which endpoints are blocked while others
/// remain available for the same provider.
/// </remarks>
public interface IRateLimitTracker {
    /// <summary>
    /// Check if an endpoint is currently rate-limited.
    /// </summary>
    /// <param name="provider">The music provider (Spotify, Apple Music, Tidal).</param>
    /// <param name="endpoint">The API endpoint path (e.g., "/v1/tracks").</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The current rate limit state for the endpoint.</returns>
    Task<RateLimitState> GetStateAsync( SupportedProviders provider, string endpoint, CancellationToken cancellationToken = default );

    /// <summary>
    /// Record a rate limit event for an endpoint.
    /// </summary>
    /// <param name="provider">The music provider that returned the rate limit.</param>
    /// <param name="endpoint">The API endpoint that was rate-limited.</param>
    /// <param name="retryAfter">The time when the rate limit expires.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetRateLimitedAsync( SupportedProviders provider, string endpoint, DateTimeOffset retryAfter, CancellationToken cancellationToken = default );

    /// <summary>
    /// Clear rate limit state (e.g., after successful request).
    /// </summary>
    /// <param name="provider">The music provider.</param>
    /// <param name="endpoint">The API endpoint to clear.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ClearAsync( SupportedProviders provider, string endpoint, CancellationToken cancellationToken = default );

    /// <summary>
    /// Get all currently rate-limited endpoints for a provider.
    /// </summary>
    /// <param name="provider">The music provider to check.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of all rate-limited endpoints with their retry-after times.</returns>
    Task<IReadOnlyList<RateLimitedEndpoint>> GetAllRateLimitedAsync( SupportedProviders provider, CancellationToken cancellationToken = default );
}
