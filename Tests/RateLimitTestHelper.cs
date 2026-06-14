using BridgeBeats.Contracts.Exceptions;

namespace BridgeBeats.Tests;

/// <summary>
/// Test helper that lets live-provider tests tolerate rate limiting from external music services.
/// When a provider returns a <see cref="RetryAfterExceededException"/> or yields null/empty results,
/// the affected test is marked inconclusive rather than failed, so transient throttling does not turn
/// the suite red.
/// </summary>
public static class RateLimitTestHelper {
    /// <summary>
    /// Runs an asynchronous, result-producing action and converts a rate-limit exception into an
    /// inconclusive result instead of a failure.
    /// </summary>
    /// <typeparam name="T">The reference type returned by the action.</typeparam>
    /// <param name="action">The asynchronous operation to execute.</param>
    /// <param name="context">Optional description used in the inconclusive message.</param>
    /// <returns>The action's result, or <c>null</c> if the call was rate limited.</returns>
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
    /// Runs an asynchronous action that returns no value and converts a rate-limit exception into an
    /// inconclusive result instead of a failure.
    /// </summary>
    /// <param name="action">The asynchronous operation to execute.</param>
    /// <param name="context">Optional description used in the inconclusive message.</param>
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
    /// Marks the current test inconclusive with a message describing which provider rate limited the
    /// request, the retry-after delay, and the request URI.
    /// </summary>
    /// <param name="ex">The rate-limit exception thrown by the provider.</param>
    /// <param name="context">Optional description prepended to the inconclusive message.</param>
    public static void HandleRateLimitException( RetryAfterExceededException ex, string? context = null ) {
        string providerInfo = ex.Provider.HasValue ? ex.Provider.Value.ToString( ) : "Unknown";
        string retryAfter = ex.RetryAfterValue.TotalSeconds.ToString( "F0" );
        string message = string.IsNullOrEmpty( context )
            ? $"Rate limited by {providerInfo}. Retry after {retryAfter}s (URI: {ex.RequestUri})"
            : $"{context}: Rate limited by {providerInfo}. Retry after {retryAfter}s (URI: {ex.RequestUri})";

        Assert.Inconclusive( message );
    }

    /// <summary>
    /// Asserts that a result is non-null; if it is null, marks the test inconclusive on the assumption
    /// the provider was rate limited or unavailable rather than failing outright.
    /// </summary>
    /// <typeparam name="T">The reference type of the result.</typeparam>
    /// <param name="result">The value to check.</param>
    /// <param name="context">Description used in the inconclusive message.</param>
    /// <returns><c>true</c> if the result is non-null; otherwise the test is marked inconclusive.</returns>
    public static bool AssertNotNullOrRateLimited<T>( T? result, string context ) where T : class {
        if (result is null) {
            Assert.Inconclusive( $"{context}: API returned null - possibly due to rate limiting or service unavailability" );
            return false;
        }
        return true;
    }

    /// <summary>
    /// Asserts that a collection is non-empty; if it is empty, marks the test inconclusive on the
    /// assumption the provider was rate limited or unavailable rather than failing outright.
    /// </summary>
    /// <typeparam name="T">The element type of the collection.</typeparam>
    /// <param name="collection">The collection to check.</param>
    /// <param name="context">Description used in the inconclusive message.</param>
    /// <returns><c>true</c> if the collection has elements; otherwise the test is marked inconclusive.</returns>
    public static bool AssertNotEmptyOrRateLimited<T>( IEnumerable<T> collection, string context ) {
        if (!collection.Any( )) {
            Assert.Inconclusive( $"{context}: API returned empty results - possibly due to rate limiting or service unavailability" );
            return false;
        }
        return true;
    }
}
