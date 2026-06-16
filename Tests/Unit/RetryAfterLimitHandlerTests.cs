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
/// Unit tests for <see cref="RetryAfterLimitHandler"/>, the delegating handler that fails fast on an
/// HTTP 429 whose <c>Retry-After</c> exceeds a configured threshold. Verify that non-429 responses and
/// 429s with no or below-threshold <c>Retry-After</c> pass through; that a <c>Retry-After</c> above the
/// threshold (in both delta-seconds and HTTP-date forms) throws <see cref="RetryAfterExceededException"/>
/// carrying the retry-after and threshold; that the exception's provider is derived from the request
/// host; that the fail-fast path logs a warning; and the zero- and large-threshold edge cases.
/// </summary>
[TestClass]
public class RetryAfterLimitHandlerTests {
    /// <summary>Mocked logger (enabled at all levels) used to assert the fail-fast warning.</summary>
    private Mock<ILogger<RetryAfterLimitHandler>> _loggerMock = null!;
    /// <summary>The default threshold (seconds) handlers are constructed with for these tests.</summary>
    private const int DefaultMaxRetryAfterSeconds = 120;

    /// <summary>Builds a fresh logger mock (enabled at all levels) before each test.</summary>
    [TestInitialize]
    public void Initialize( ) {
        _loggerMock = new Mock<ILogger<RetryAfterLimitHandler>>( );
        // Enable logging for all log levels so LoggerMessage source-generated methods invoke Log
        _ = _loggerMock.Setup( x => x.IsEnabled( It.IsAny<LogLevel>( ) ) ).Returns( true );
    }

    #region Constructor Tests

    /// <summary>The constructor builds an instance from a valid threshold and logger.</summary>
    [TestMethod]
    public void Constructor_WithValidParameters_ShouldCreateInstance( ) {
        // Act
        RetryAfterLimitHandler handler = new( DefaultMaxRetryAfterSeconds, _loggerMock.Object );

        // Assert
        Assert.IsNotNull( handler );
    }

    /// <summary>The constructor accepts a zero threshold and builds an instance.</summary>
    [TestMethod]
    public void Constructor_WithZeroThreshold_ShouldCreateInstance( ) {
        // Act - Zero means any Retry-After will exceed threshold
        RetryAfterLimitHandler handler = new( 0, _loggerMock.Object );

        // Assert
        Assert.IsNotNull( handler );
    }

    #endregion

    #region SendAsync Tests - Non-429 Responses

    /// <summary>A 200 response passes through unchanged.</summary>
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

    /// <summary>A 500 response passes through unchanged (non-429 errors are not intercepted).</summary>
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

    /// <summary>A 404 response passes through unchanged.</summary>
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
    /// A 429 with no <c>Retry-After</c> header passes through (nothing to compare against the threshold).
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
    /// A 429 whose <c>Retry-After</c> (60s) is below the threshold passes through.
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
    /// A 429 whose <c>Retry-After</c> (120s) exactly equals the threshold passes through (the boundary
    /// is inclusive; only values above the threshold throw).
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
    /// A 429 whose <c>Retry-After</c> (300s) exceeds the threshold throws
    /// <see cref="RetryAfterExceededException"/> carrying the retry-after value and threshold.
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
    /// A 429 whose <c>Retry-After</c> (121s) is just one second above the threshold throws, with the
    /// retry-after value preserved.
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
    /// A 429 with a very long <c>Retry-After</c> (1 hour) throws, with the full retry-after value
    /// preserved.
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
    /// A 429 whose <c>Retry-After</c> is an HTTP-date roughly 10 minutes in the future exceeds the
    /// threshold and throws, with the computed retry-after falling in the expected 500–700 second range.
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
    /// A 429 whose <c>Retry-After</c> is an HTTP-date about 30 seconds in the future is below the
    /// threshold and passes through.
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
    /// A fail-fast exception for a Spotify request URI carries <see cref="SupportedProviders.Spotify"/>
    /// and the originating request URI.
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
    /// A fail-fast exception for an Apple Music request URI carries
    /// <see cref="SupportedProviders.AppleMusic"/>.
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
    /// A fail-fast exception for a Tidal request URI carries <see cref="SupportedProviders.Tidal"/>.
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
    /// A fail-fast exception for an unrecognized request host carries a null provider.
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
    /// The fail-fast path logs a warning carrying the rate-limit-exceeded event id exactly once.
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
    /// With a zero threshold, any positive <c>Retry-After</c> (1s) exceeds it and throws.
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
    /// With a large threshold (3600s), a 30-minute <c>Retry-After</c> stays below it and passes through.
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

    /// <summary>Builds a <see cref="RetryAfterLimitHandler"/> with the given threshold and the test logger.</summary>
    private RetryAfterLimitHandler CreateHandler( int maxRetryAfterSeconds ) {
        return new RetryAfterLimitHandler( maxRetryAfterSeconds, _loggerMock.Object );
    }

    /// <summary>
    /// Sets the handler's inner handler to a stub that always returns <paramref name="response"/>.
    /// </summary>
    private static void SetupInnerHandler( RetryAfterLimitHandler handler, HttpResponseMessage response ) {
        TestDelegatingHandler innerHandler = new( response );
        handler.InnerHandler = innerHandler;
    }

    /// <summary>
    /// Sends a GET request (to the given URI or a default) through the handler and returns the response.
    /// </summary>
    private static async Task<HttpResponseMessage> SendRequestAsync( RetryAfterLimitHandler handler, Uri? requestUri = null ) {
        using HttpMessageInvoker invoker = new( handler );
        HttpRequestMessage request = new( HttpMethod.Get, requestUri ?? new Uri( "https://test.example.com/api" ) );
        return await invoker.SendAsync( request, CancellationToken.None );
    }

    /// <summary>Inner-handler stub that returns a fixed <see cref="HttpResponseMessage"/> for any request.</summary>
    private class TestDelegatingHandler( HttpResponseMessage response ) : HttpMessageHandler {
        /// <summary>Returns the fixed response without contacting any network.</summary>
        protected override Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken ) {
            return Task.FromResult( response );
        }
    }

    #endregion
}
