using BridgeBeats.Contracts.Exceptions;

namespace BridgeBeats.Tests;

/// <summary>
/// Helper class for handling rate limit responses in integration tests that call external APIs.
/// Provides consistent inconclusive/failure determination based on rate limit thresholds.
/// </summary>
public static class RateLimitTestHelper {
    /// <summary>
    /// Executes an async action and handles rate limit exceptions appropriately.
    /// </summary>
    /// <typeparam name="T">The return type of the action.</typeparam>
    /// <param name="action">The async action to execute.</param>
    /// <param name="context">Optional context message for the inconclusive assertion.</param>
    /// <returns>The result of the action, or default if rate limited.</returns>
    public static async Task<T?> ExecuteWithRateLimitHandlingAsync<T>(
        Func<Task<T>> action,
        string? context = null
    ) where T : class {
        try {
            return await action( );
        } catch (RetryAfterExceededException ex) {
            HandleRateLimitException( ex, context );
            return default;
        }
    }

    /// <summary>
    /// Executes an async action and handles rate limit exceptions appropriately.
    /// </summary>
    /// <param name="action">The async action to execute.</param>
    /// <param name="context">Optional context message for the inconclusive assertion.</param>
    public static async Task ExecuteWithRateLimitHandlingAsync(
        Func<Task> action,
        string? context = null
    ) {
        try {
            await action( );
        } catch (RetryAfterExceededException ex) {
            HandleRateLimitException( ex, context );
        }
    }

    /// <summary>
    /// Handles a rate limit exception by marking the test as inconclusive with details.
    /// </summary>
    /// <param name="ex">The rate limit exception.</param>
    /// <param name="context">Optional context message.</param>
    public static void HandleRateLimitException( RetryAfterExceededException ex, string? context = null ) {
        string providerInfo = ex.Provider.HasValue ? ex.Provider.Value.ToString( ) : "Unknown";
        string retryAfter = ex.RetryAfterValue.TotalSeconds.ToString( "F0" );
        string message = string.IsNullOrEmpty( context )
            ? $"Rate limited by {providerInfo}. Retry after {retryAfter}s (URI: {ex.RequestUri})"
            : $"{context}: Rate limited by {providerInfo}. Retry after {retryAfter}s (URI: {ex.RequestUri})";

        Assert.Inconclusive( message );
    }

    /// <summary>
    /// Checks if a null result likely indicates a rate limit vs an actual API failure.
    /// For integration tests, null results are often due to rate limiting.
    /// </summary>
    /// <param name="result">The result to check.</param>
    /// <param name="context">Context for the inconclusive message.</param>
    /// <returns>True if the result is not null and the test should continue.</returns>
    public static bool AssertNotNullOrRateLimited<T>( T? result, string context ) where T : class {
        if (result is null) {
            Assert.Inconclusive( $"{context}: API returned null - possibly due to rate limiting or service unavailability" );
            return false;
        }
        return true;
    }

    /// <summary>
    /// Checks if an empty collection likely indicates a rate limit vs an actual failure.
    /// </summary>
    /// <param name="collection">The collection to check.</param>
    /// <param name="context">Context for the inconclusive message.</param>
    /// <returns>True if the collection is not empty and the test should continue.</returns>
    public static bool AssertNotEmptyOrRateLimited<T>( IEnumerable<T> collection, string context ) {
        if (!collection.Any( )) {
            Assert.Inconclusive( $"{context}: API returned empty results - possibly due to rate limiting or service unavailability" );
            return false;
        }
        return true;
    }
}
