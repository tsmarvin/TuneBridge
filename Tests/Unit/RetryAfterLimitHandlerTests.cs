using System.Net;
using System.Net.Http.Headers;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Exceptions;
using BridgeBeats.Providers.Common;
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

    [TestInitialize]
    public void Initialize( ) {
        _loggerMock = new Mock<ILogger<RetryAfterLimitHandler>>( );
    }

    #region Constructor Tests

    [TestMethod]
    public void Constructor_WithValidParameters_ShouldCreateInstance( ) {
        // Act
        RetryAfterLimitHandler handler = new( DefaultMaxRetryAfterSeconds, _loggerMock.Object );

        // Assert
        Assert.IsNotNull( handler );
    }

    [TestMethod]
    public void Constructor_WithZeroThreshold_ShouldCreateInstance( ) {
        // Act - Zero means any Retry-After will exceed threshold
        RetryAfterLimitHandler handler = new( 0, _loggerMock.Object );

        // Assert
        Assert.IsNotNull( handler );
    }

    #endregion

    #region SendAsync Tests - Non-429 Responses

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

        // Assert - Verify warning was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>( ),
                It.Is<It.IsAnyType>( ( v, t ) => v.ToString( )!.Contains( "Rate limit exceeded threshold" ) ),
                It.IsAny<Exception?>( ),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>( )
            ),
            Times.Once
        );
    }

    #endregion

    #region Edge Cases

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

    private RetryAfterLimitHandler CreateHandler( int maxRetryAfterSeconds ) {
        return new RetryAfterLimitHandler( maxRetryAfterSeconds, _loggerMock.Object );
    }

    private static void SetupInnerHandler( RetryAfterLimitHandler handler, HttpResponseMessage response ) {
        TestDelegatingHandler innerHandler = new( response );
        handler.InnerHandler = innerHandler;
    }

    private static async Task<HttpResponseMessage> SendRequestAsync( RetryAfterLimitHandler handler, Uri? requestUri = null ) {
        using HttpMessageInvoker invoker = new( handler );
        HttpRequestMessage request = new( HttpMethod.Get, requestUri ?? new Uri( "https://test.example.com/api" ) );
        return await invoker.SendAsync( request, CancellationToken.None );
    }

    /// <summary>
    /// Test handler that returns a predefined response.
    /// </summary>
    private class TestDelegatingHandler( HttpResponseMessage response ) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken ) {
            return Task.FromResult( response );
        }
    }

    #endregion
}
