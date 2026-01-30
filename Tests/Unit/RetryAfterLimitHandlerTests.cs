using System.Net;
using System.Net.Http.Headers;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Exceptions;
using BridgeBeats.Core.Domain.Providers.Common;
using BridgeBeats.Core.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using Moq;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for RetryAfterLimitHandler to verify fail-fast behavior on 429 responses
/// with Retry-After headers exceeding the configured threshold.
/// </summary>
[TestClass]
public class RetryAfterLimitHandlerTests {
    private Mock<ILogger<RetryAfterLimitHandler>> _loggerMock = null!;
    private const int DefaultMaxRetryAfterSeconds = 120;

    /// <summary>
    /// Initializes test dependencies before each test method.
    /// </summary>
    [TestInitialize]
    public void Initialize( ) {
        _loggerMock = new Mock<ILogger<RetryAfterLimitHandler>>( );
        // Enable logging for all log levels so LoggerMessage source-generated methods invoke Log
        _ = _loggerMock.Setup( x => x.IsEnabled( It.IsAny<LogLevel>( ) ) ).Returns( true );
    }

    #region Constructor Tests

    /// <summary>
    /// Verifies that the constructor creates a valid instance when provided with valid parameters.
    /// </summary>
    [TestMethod]
    public void Constructor_WithValidParameters_ShouldCreateInstance( ) {
        // Act
        RetryAfterLimitHandler handler = new( DefaultMaxRetryAfterSeconds, _loggerMock.Object );

        // Assert
        Assert.IsNotNull( handler );
    }

    /// <summary>
    /// Verifies that the constructor accepts a zero threshold value, which means any Retry-After
    /// header will exceed the threshold.
    /// </summary>
    [TestMethod]
    public void Constructor_WithZeroThreshold_ShouldCreateInstance( ) {
        // Act - Zero means any Retry-After will exceed threshold
        RetryAfterLimitHandler handler = new( 0, _loggerMock.Object );

        // Assert
        Assert.IsNotNull( handler );
    }

    #endregion

    #region SendAsync Tests - Non-429 Responses

    /// <summary>
    /// Verifies that successful responses pass through the handler without modification.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_WithSuccessResponse_ShouldPassThrough( ) {
        // Arrange
        RetryAfterLimitHandler handler = CreateHandler( DefaultMaxRetryAfterSeconds );
        HttpResponseMessage successResponse = new( HttpStatusCode.OK );
        SetupInnerHandler( handler, successResponse );

        // Act
        HttpResponseMessage result = await SendRequestAsync( handler );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, result.StatusCode );
    }

    /// <summary>
    /// Verifies that 500 Internal Server Error responses pass through without triggering rate limit logic.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_With500Error_ShouldPassThrough( ) {
        // Arrange
        RetryAfterLimitHandler handler = CreateHandler( DefaultMaxRetryAfterSeconds );
        HttpResponseMessage errorResponse = new( HttpStatusCode.InternalServerError );
        SetupInnerHandler( handler, errorResponse );

        // Act
        HttpResponseMessage result = await SendRequestAsync( handler );

        // Assert
        Assert.AreEqual( HttpStatusCode.InternalServerError, result.StatusCode );
    }

    /// <summary>
    /// Verifies that 404 Not Found responses pass through without triggering rate limit logic.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_With404Error_ShouldPassThrough( ) {
        // Arrange
        RetryAfterLimitHandler handler = CreateHandler( DefaultMaxRetryAfterSeconds );
        HttpResponseMessage errorResponse = new( HttpStatusCode.NotFound );
        SetupInnerHandler( handler, errorResponse );

        // Act
        HttpResponseMessage result = await SendRequestAsync( handler );

        // Assert
        Assert.AreEqual( HttpStatusCode.NotFound, result.StatusCode );
    }

    #endregion

    #region SendAsync Tests - 429 Without Retry-After

    /// <summary>
    /// Verifies that 429 responses without a Retry-After header pass through without throwing,
    /// as there is no delay value to evaluate against the threshold.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_With429_NoRetryAfterHeader_ShouldPassThrough( ) {
        // Arrange
        RetryAfterLimitHandler handler = CreateHandler( DefaultMaxRetryAfterSeconds );
        HttpResponseMessage rateLimitResponse = new( HttpStatusCode.TooManyRequests );
        // No Retry-After header set
        SetupInnerHandler( handler, rateLimitResponse );

        // Act
        HttpResponseMessage result = await SendRequestAsync( handler );

        // Assert - Should pass through without throwing
        Assert.AreEqual( HttpStatusCode.TooManyRequests, result.StatusCode );
    }

    #endregion

    #region SendAsync Tests - 429 With Retry-After Below Threshold

    /// <summary>
    /// Verifies that 429 responses with a Retry-After value below the threshold pass through
    /// to allow the standard resilience pipeline to handle the retry.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_With429_RetryAfterBelowThreshold_ShouldPassThrough( ) {
        // Arrange
        RetryAfterLimitHandler handler = CreateHandler( DefaultMaxRetryAfterSeconds );
        HttpResponseMessage rateLimitResponse = new( HttpStatusCode.TooManyRequests );
        rateLimitResponse.Headers.RetryAfter = new RetryConditionHeaderValue( TimeSpan.FromSeconds( 60 ) );
        SetupInnerHandler( handler, rateLimitResponse );

        // Act
        HttpResponseMessage result = await SendRequestAsync( handler );

        // Assert - 60 seconds is below 120 threshold, should pass through
        Assert.AreEqual( HttpStatusCode.TooManyRequests, result.StatusCode );
    }

    /// <summary>
    /// Verifies that 429 responses with a Retry-After value exactly at the threshold pass through,
    /// as only values exceeding the threshold should trigger the fail-fast behavior.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_With429_RetryAfterExactlyAtThreshold_ShouldPassThrough( ) {
        // Arrange
        RetryAfterLimitHandler handler = CreateHandler( DefaultMaxRetryAfterSeconds );
        HttpResponseMessage rateLimitResponse = new( HttpStatusCode.TooManyRequests );
        rateLimitResponse.Headers.RetryAfter = new RetryConditionHeaderValue( TimeSpan.FromSeconds( 120 ) );
        SetupInnerHandler( handler, rateLimitResponse );

        // Act
        HttpResponseMessage result = await SendRequestAsync( handler );

        // Assert - Exactly at threshold should pass through (not exceeded)
        Assert.AreEqual( HttpStatusCode.TooManyRequests, result.StatusCode );
    }

    #endregion

    #region SendAsync Tests - 429 With Retry-After Exceeding Threshold

    /// <summary>
    /// Verifies that 429 responses with a Retry-After value exceeding the threshold throw
    /// <see cref="RetryAfterExceededException"/> with the correct retry value and threshold.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_With429_RetryAfterExceedsThreshold_ShouldThrowException( ) {
        // Arrange
        RetryAfterLimitHandler handler = CreateHandler( DefaultMaxRetryAfterSeconds );
        HttpResponseMessage rateLimitResponse = new( HttpStatusCode.TooManyRequests );
        rateLimitResponse.Headers.RetryAfter = new RetryConditionHeaderValue( TimeSpan.FromSeconds( 300 ) ); // 5 minutes
        SetupInnerHandler( handler, rateLimitResponse );

        // Act & Assert
        RetryAfterExceededException exception = await Assert.ThrowsExactlyAsync<RetryAfterExceededException>(
            ( ) => SendRequestAsync( handler )
        );

        Assert.AreEqual( TimeSpan.FromSeconds( 300 ), exception.RetryAfterValue );
        Assert.AreEqual( TimeSpan.FromSeconds( DefaultMaxRetryAfterSeconds ), exception.Threshold );
    }

    /// <summary>
    /// Verifies that 429 responses with a Retry-After value just slightly over the threshold
    /// still trigger the fail-fast behavior.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_With429_RetryAfterSlightlyExceedsThreshold_ShouldThrowException( ) {
        // Arrange
        RetryAfterLimitHandler handler = CreateHandler( DefaultMaxRetryAfterSeconds );
        HttpResponseMessage rateLimitResponse = new( HttpStatusCode.TooManyRequests );
        rateLimitResponse.Headers.RetryAfter = new RetryConditionHeaderValue( TimeSpan.FromSeconds( 121 ) ); // Just over threshold
        SetupInnerHandler( handler, rateLimitResponse );

        // Act & Assert
        RetryAfterExceededException exception = await Assert.ThrowsExactlyAsync<RetryAfterExceededException>(
            ( ) => SendRequestAsync( handler )
        );

        Assert.AreEqual( TimeSpan.FromSeconds( 121 ), exception.RetryAfterValue );
    }

    /// <summary>
    /// Verifies that very long Retry-After values (e.g., 1 hour) correctly trigger the fail-fast behavior,
    /// as commonly seen during severe rate limiting scenarios.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_With429_VeryLongRetryAfter_ShouldThrowException( ) {
        // Arrange - Simulate 1 hour Retry-After (common for severe rate limiting)
        RetryAfterLimitHandler handler = CreateHandler( DefaultMaxRetryAfterSeconds );
        HttpResponseMessage rateLimitResponse = new( HttpStatusCode.TooManyRequests );
        rateLimitResponse.Headers.RetryAfter = new RetryConditionHeaderValue( TimeSpan.FromHours( 1 ) );
        SetupInnerHandler( handler, rateLimitResponse );

        // Act & Assert
        RetryAfterExceededException exception = await Assert.ThrowsExactlyAsync<RetryAfterExceededException>(
            ( ) => SendRequestAsync( handler )
        );

        Assert.AreEqual( TimeSpan.FromHours( 1 ), exception.RetryAfterValue );
    }

    #endregion

    #region SendAsync Tests - HTTP-Date Format Retry-After

    /// <summary>
    /// Verifies that Retry-After headers using HTTP-date format (future date) are correctly
    /// converted to a duration and evaluated against the threshold.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_With429_HttpDateRetryAfterExceedsThreshold_ShouldThrowException( ) {
        // Arrange - Use HTTP-date format (future date)
        RetryAfterLimitHandler handler = CreateHandler( DefaultMaxRetryAfterSeconds );
        HttpResponseMessage rateLimitResponse = new( HttpStatusCode.TooManyRequests );
        DateTimeOffset futureDate = DateTimeOffset.UtcNow.AddMinutes( 10 ); // 10 minutes in future
        rateLimitResponse.Headers.RetryAfter = new RetryConditionHeaderValue( futureDate );
        SetupInnerHandler( handler, rateLimitResponse );

        // Act & Assert
        RetryAfterExceededException exception = await Assert.ThrowsExactlyAsync<RetryAfterExceededException>(
            ( ) => SendRequestAsync( handler )
        );

        // Should be approximately 10 minutes (600 seconds), allow some tolerance
        Assert.IsGreaterThan( 500, exception.RetryAfterValue.TotalSeconds );
        Assert.IsLessThan( 700, exception.RetryAfterValue.TotalSeconds );
    }

    /// <summary>
    /// Verifies that Retry-After headers using HTTP-date format with a near-future date
    /// that is below the threshold pass through without throwing.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_With429_HttpDateRetryAfterBelowThreshold_ShouldPassThrough( ) {
        // Arrange - Use HTTP-date format (near future)
        RetryAfterLimitHandler handler = CreateHandler( DefaultMaxRetryAfterSeconds );
        HttpResponseMessage rateLimitResponse = new( HttpStatusCode.TooManyRequests );
        DateTimeOffset futureDate = DateTimeOffset.UtcNow.AddSeconds( 30 ); // 30 seconds in future
        rateLimitResponse.Headers.RetryAfter = new RetryConditionHeaderValue( futureDate );
        SetupInnerHandler( handler, rateLimitResponse );

        // Act
        HttpResponseMessage result = await SendRequestAsync( handler );

        // Assert - 30 seconds is below threshold
        Assert.AreEqual( HttpStatusCode.TooManyRequests, result.StatusCode );
    }

    #endregion

    #region Provider Detection Tests

    /// <summary>
    /// Verifies that requests to Spotify API endpoints correctly identify the provider
    /// in the thrown exception.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_WithSpotifyUri_ShouldIncludeProviderInException( ) {
        // Arrange
        RetryAfterLimitHandler handler = CreateHandler( DefaultMaxRetryAfterSeconds );
        HttpResponseMessage rateLimitResponse = new( HttpStatusCode.TooManyRequests );
        rateLimitResponse.Headers.RetryAfter = new RetryConditionHeaderValue( TimeSpan.FromSeconds( 300 ) );
        SetupInnerHandler( handler, rateLimitResponse );

        // Act & Assert
        RetryAfterExceededException exception = await Assert.ThrowsExactlyAsync<RetryAfterExceededException>(
            ( ) => SendRequestAsync( handler, new Uri( "https://api.spotify.com/v1/tracks/123" ) )
        );

        Assert.AreEqual( SupportedProviders.Spotify, exception.Provider );
        Assert.IsNotNull( exception.RequestUri );
        Assert.Contains( "spotify", exception.RequestUri.Host );
    }

    /// <summary>
    /// Verifies that requests to Apple Music API endpoints correctly identify the provider
    /// in the thrown exception.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_WithAppleMusicUri_ShouldIncludeProviderInException( ) {
        // Arrange
        RetryAfterLimitHandler handler = CreateHandler( DefaultMaxRetryAfterSeconds );
        HttpResponseMessage rateLimitResponse = new( HttpStatusCode.TooManyRequests );
        rateLimitResponse.Headers.RetryAfter = new RetryConditionHeaderValue( TimeSpan.FromSeconds( 300 ) );
        SetupInnerHandler( handler, rateLimitResponse );

        // Act & Assert
        RetryAfterExceededException exception = await Assert.ThrowsExactlyAsync<RetryAfterExceededException>(
            ( ) => SendRequestAsync( handler, new Uri( "https://api.music.apple.com/v1/catalog/us/songs/123" ) )
        );

        Assert.AreEqual( SupportedProviders.AppleMusic, exception.Provider );
    }

    /// <summary>
    /// Verifies that requests to Tidal API endpoints correctly identify the provider
    /// in the thrown exception.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_WithTidalUri_ShouldIncludeProviderInException( ) {
        // Arrange
        RetryAfterLimitHandler handler = CreateHandler( DefaultMaxRetryAfterSeconds );
        HttpResponseMessage rateLimitResponse = new( HttpStatusCode.TooManyRequests );
        rateLimitResponse.Headers.RetryAfter = new RetryConditionHeaderValue( TimeSpan.FromSeconds( 300 ) );
        SetupInnerHandler( handler, rateLimitResponse );

        // Act & Assert
        RetryAfterExceededException exception = await Assert.ThrowsExactlyAsync<RetryAfterExceededException>(
            ( ) => SendRequestAsync( handler, new Uri( "https://openapi.tidal.com/v2/tracks/123" ) )
        );

        Assert.AreEqual( SupportedProviders.Tidal, exception.Provider );
    }

    /// <summary>
    /// Verifies that requests to unrecognized API endpoints result in a null provider
    /// in the thrown exception.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_WithUnknownUri_ShouldHaveNullProvider( ) {
        // Arrange
        RetryAfterLimitHandler handler = CreateHandler( DefaultMaxRetryAfterSeconds );
        HttpResponseMessage rateLimitResponse = new( HttpStatusCode.TooManyRequests );
        rateLimitResponse.Headers.RetryAfter = new RetryConditionHeaderValue( TimeSpan.FromSeconds( 300 ) );
        SetupInnerHandler( handler, rateLimitResponse );

        // Act & Assert
        RetryAfterExceededException exception = await Assert.ThrowsExactlyAsync<RetryAfterExceededException>(
            ( ) => SendRequestAsync( handler, new Uri( "https://unknown-api.example.com/endpoint" ) )
        );

        Assert.IsNull( exception.Provider );
    }

    #endregion

    #region Logging Tests

    /// <summary>
    /// Verifies that a warning is logged when the handler throws <see cref="RetryAfterExceededException"/>
    /// due to a rate limit threshold being exceeded.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_WhenThrowingException_ShouldLogWarning( ) {
        // Arrange
        RetryAfterLimitHandler handler = CreateHandler( DefaultMaxRetryAfterSeconds );
        HttpResponseMessage rateLimitResponse = new( HttpStatusCode.TooManyRequests );
        rateLimitResponse.Headers.RetryAfter = new RetryConditionHeaderValue( TimeSpan.FromSeconds( 300 ) );
        SetupInnerHandler( handler, rateLimitResponse );

        // Act
        try {
            _ = await SendRequestAsync( handler, new Uri( "https://api.spotify.com/v1/tracks/123" ) );
        } catch (RetryAfterExceededException) {
            // Expected
        }

        // Assert - Verify warning was logged with the correct EventId
        // LoggerMessage source-generated methods pass a LoggerMessageState struct to Log,
        // so we verify by EventId instead of checking message content directly
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.Is<EventId>( e => e.Id == LogEventIds.Providers.Common.RateLimitExceeded ),
                It.IsAny<It.IsAnyType>( ),
                It.IsAny<Exception?>( ),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>( )
            ),
            Times.Once
        );
    }

    #endregion

    #region Edge Cases

    /// <summary>
    /// Verifies that with a zero threshold, any non-zero Retry-After value triggers the fail-fast behavior.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_WithZeroThreshold_AnyRetryAfterShouldThrow( ) {
        // Arrange - Zero threshold means any Retry-After exceeds it
        RetryAfterLimitHandler handler = CreateHandler( 0 );
        HttpResponseMessage rateLimitResponse = new( HttpStatusCode.TooManyRequests );
        rateLimitResponse.Headers.RetryAfter = new RetryConditionHeaderValue( TimeSpan.FromSeconds( 1 ) );
        SetupInnerHandler( handler, rateLimitResponse );

        // Act & Assert
        _ = await Assert.ThrowsExactlyAsync<RetryAfterExceededException>(
            ( ) => SendRequestAsync( handler )
        );
    }

    /// <summary>
    /// Verifies that with a very large threshold (1 hour), typical Retry-After values pass through
    /// without triggering the fail-fast behavior.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_WithLargeThreshold_ShouldNotThrow( ) {
        // Arrange - Very large threshold (1 hour)
        RetryAfterLimitHandler handler = CreateHandler( 3600 );
        HttpResponseMessage rateLimitResponse = new( HttpStatusCode.TooManyRequests );
        rateLimitResponse.Headers.RetryAfter = new RetryConditionHeaderValue( TimeSpan.FromMinutes( 30 ) );
        SetupInnerHandler( handler, rateLimitResponse );

        // Act
        HttpResponseMessage result = await SendRequestAsync( handler );

        // Assert - 30 minutes is below 1 hour threshold
        Assert.AreEqual( HttpStatusCode.TooManyRequests, result.StatusCode );
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a <see cref="RetryAfterLimitHandler"/> instance with the specified threshold.
    /// </summary>
    /// <param name="maxRetryAfterSeconds">The maximum Retry-After value in seconds before failing fast.</param>
    /// <returns>A configured <see cref="RetryAfterLimitHandler"/> instance.</returns>
    private RetryAfterLimitHandler CreateHandler( int maxRetryAfterSeconds ) {
        return new RetryAfterLimitHandler( maxRetryAfterSeconds, _loggerMock.Object );
    }

    /// <summary>
    /// Configures the handler's inner handler to return the specified response.
    /// </summary>
    /// <param name="handler">The handler to configure.</param>
    /// <param name="response">The response the inner handler should return.</param>
    private static void SetupInnerHandler( RetryAfterLimitHandler handler, HttpResponseMessage response ) {
        TestDelegatingHandler innerHandler = new( response );
        handler.InnerHandler = innerHandler;
    }

    /// <summary>
    /// Sends a test HTTP request through the handler.
    /// </summary>
    /// <param name="handler">The handler to send the request through.</param>
    /// <param name="requestUri">Optional URI for the request. Defaults to a test URL.</param>
    /// <returns>The HTTP response from the handler.</returns>
    private static async Task<HttpResponseMessage> SendRequestAsync( RetryAfterLimitHandler handler, Uri? requestUri = null ) {
        using HttpMessageInvoker invoker = new( handler );
        HttpRequestMessage request = new( HttpMethod.Get, requestUri ?? new Uri( "https://test.example.com/api" ) );
        return await invoker.SendAsync( request, CancellationToken.None );
    }

    /// <summary>
    /// Test handler that returns a predefined response for testing purposes.
    /// </summary>
    /// <param name="response">The response to return when <see cref="SendAsync"/> is called.</param>
    private class TestDelegatingHandler( HttpResponseMessage response ) : HttpMessageHandler {
        /// <summary>
        /// Returns the predefined response configured in the constructor.
        /// </summary>
        /// <param name="request">The HTTP request message (unused).</param>
        /// <param name="cancellationToken">The cancellation token (unused).</param>
        /// <returns>The predefined response.</returns>
        protected override Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken ) {
            return Task.FromResult( response );
        }
    }

    #endregion
}
