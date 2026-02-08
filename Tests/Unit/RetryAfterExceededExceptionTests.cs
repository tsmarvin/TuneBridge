using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Exceptions;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for RetryAfterExceededException to verify exception properties,
/// message formatting, and provider detection from URIs.
/// </summary>
[TestClass]
public class RetryAfterExceededExceptionTests {

    #region Constructor Tests

    /// <summary>
    /// Verifies that the exception constructor correctly sets all properties.
    /// </summary>
    [TestMethod]
    public void Constructor_WithAllParameters_ShouldSetProperties( ) {
        // Arrange
        TimeSpan retryAfterValue = TimeSpan.FromSeconds( 300 );
        TimeSpan threshold = TimeSpan.FromSeconds( 120 );
        Uri requestUri = new( "https://api.spotify.com/v1/tracks/123" );
        SupportedProviders provider = SupportedProviders.Spotify;

        // Act
        RetryAfterExceededException exception = new( retryAfterValue, threshold, requestUri, provider );

        // Assert
        Assert.AreEqual( retryAfterValue, exception.RetryAfterValue );
        Assert.AreEqual( threshold, exception.Threshold );
        Assert.AreEqual( requestUri, exception.RequestUri );
        Assert.AreEqual( provider, exception.Provider );
    }

    /// <summary>
    /// Verifies that the exception constructor allows null URI and provider values.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullUri_ShouldAllowNull( ) {
        // Arrange
        TimeSpan retryAfterValue = TimeSpan.FromSeconds( 300 );
        TimeSpan threshold = TimeSpan.FromSeconds( 120 );

        // Act
        RetryAfterExceededException exception = new( retryAfterValue, threshold, null, null );

        // Assert
        Assert.IsNull( exception.RequestUri );
        Assert.IsNull( exception.Provider );
    }

    /// <summary>
    /// Verifies that the exception constructor correctly sets the inner exception.
    /// </summary>
    [TestMethod]
    public void Constructor_WithInnerException_ShouldSetInnerException( ) {
        // Arrange
        TimeSpan retryAfterValue = TimeSpan.FromSeconds( 300 );
        TimeSpan threshold = TimeSpan.FromSeconds( 120 );
        InvalidOperationException innerException = new( "Inner error" );

        // Act
        RetryAfterExceededException exception = new(
            retryAfterValue, threshold, null, null, innerException
        );

        // Assert
        Assert.IsNotNull( exception.InnerException );
        Assert.AreEqual( innerException, exception.InnerException );
    }

    #endregion

    #region Message Tests

    /// <summary>
    /// Verifies that the exception message contains the retry-after value.
    /// </summary>
    [TestMethod]
    public void Message_ShouldContainRetryAfterValue( ) {
        // Arrange
        TimeSpan retryAfterValue = TimeSpan.FromSeconds( 300 );
        TimeSpan threshold = TimeSpan.FromSeconds( 120 );

        // Act
        RetryAfterExceededException exception = new( retryAfterValue, threshold, null, null );

        // Assert
        Assert.Contains( "300", exception.Message );
    }

    /// <summary>
    /// Verifies that the exception message contains the threshold value.
    /// </summary>
    [TestMethod]
    public void Message_ShouldContainThreshold( ) {
        // Arrange
        TimeSpan retryAfterValue = TimeSpan.FromSeconds( 300 );
        TimeSpan threshold = TimeSpan.FromSeconds( 120 );

        // Act
        RetryAfterExceededException exception = new( retryAfterValue, threshold, null, null );

        // Assert
        Assert.Contains( "120", exception.Message );
    }

    /// <summary>
    /// Verifies that the exception message contains the provider name when specified.
    /// </summary>
    [TestMethod]
    public void Message_ShouldContainProviderName( ) {
        // Arrange
        TimeSpan retryAfterValue = TimeSpan.FromSeconds( 300 );
        TimeSpan threshold = TimeSpan.FromSeconds( 120 );

        // Act
        RetryAfterExceededException exception = new(
            retryAfterValue, threshold, null, SupportedProviders.Spotify
        );

        // Assert
        Assert.Contains( "Spotify", exception.Message );
    }

    /// <summary>
    /// Verifies that the exception message contains "Unknown" when provider is null.
    /// </summary>
    [TestMethod]
    public void Message_WithNullProvider_ShouldContainUnknown( ) {
        // Arrange
        TimeSpan retryAfterValue = TimeSpan.FromSeconds( 300 );
        TimeSpan threshold = TimeSpan.FromSeconds( 120 );

        // Act
        RetryAfterExceededException exception = new( retryAfterValue, threshold, null, null );

        // Assert
        Assert.Contains( "Unknown", exception.Message );
    }

    /// <summary>
    /// Verifies that the exception message contains the request URI when specified.
    /// </summary>
    [TestMethod]
    public void Message_ShouldContainRequestUri( ) {
        // Arrange
        TimeSpan retryAfterValue = TimeSpan.FromSeconds( 300 );
        TimeSpan threshold = TimeSpan.FromSeconds( 120 );
        Uri requestUri = new( "https://api.spotify.com/v1/tracks/123" );

        // Act
        RetryAfterExceededException exception = new( retryAfterValue, threshold, requestUri, null );

        // Assert
        Assert.Contains( "api.spotify.com", exception.Message );
    }

    #endregion

    #region DetermineProviderFromUri Tests

    /// <summary>
    /// Verifies that DetermineProviderFromUri returns Spotify for Spotify API URIs.
    /// </summary>
    [TestMethod]
    public void DetermineProviderFromUri_WithSpotifyUri_ShouldReturnSpotify( ) {
        // Arrange
        Uri uri = new( "https://api.spotify.com/v1/tracks/123" );

        // Act
        SupportedProviders? result = RetryAfterExceededException.DetermineProviderFromUri( uri );

        // Assert
        Assert.AreEqual( SupportedProviders.Spotify, result );
    }

    /// <summary>
    /// Verifies that DetermineProviderFromUri returns Spotify for Spotify auth URIs.
    /// </summary>
    [TestMethod]
    public void DetermineProviderFromUri_WithSpotifyAuthUri_ShouldReturnSpotify( ) {
        // Arrange
        Uri uri = new( "https://accounts.spotify.com/api/token" );

        // Act
        SupportedProviders? result = RetryAfterExceededException.DetermineProviderFromUri( uri );

        // Assert
        Assert.AreEqual( SupportedProviders.Spotify, result );
    }

    /// <summary>
    /// Verifies that DetermineProviderFromUri returns AppleMusic for Apple Music API URIs.
    /// </summary>
    [TestMethod]
    public void DetermineProviderFromUri_WithAppleMusicUri_ShouldReturnAppleMusic( ) {
        // Arrange
        Uri uri = new( "https://api.music.apple.com/v1/catalog/us/songs/123" );

        // Act
        SupportedProviders? result = RetryAfterExceededException.DetermineProviderFromUri( uri );

        // Assert
        Assert.AreEqual( SupportedProviders.AppleMusic, result );
    }

    /// <summary>
    /// Verifies that DetermineProviderFromUri returns Tidal for Tidal API URIs.
    /// </summary>
    [TestMethod]
    public void DetermineProviderFromUri_WithTidalApiUri_ShouldReturnTidal( ) {
        // Arrange
        Uri uri = new( "https://openapi.tidal.com/v2/tracks/123" );

        // Act
        SupportedProviders? result = RetryAfterExceededException.DetermineProviderFromUri( uri );

        // Assert
        Assert.AreEqual( SupportedProviders.Tidal, result );
    }

    /// <summary>
    /// Verifies that DetermineProviderFromUri returns Tidal for Tidal auth URIs.
    /// </summary>
    [TestMethod]
    public void DetermineProviderFromUri_WithTidalAuthUri_ShouldReturnTidal( ) {
        // Arrange
        Uri uri = new( "https://auth.tidal.com/v1/oauth2/token" );

        // Act
        SupportedProviders? result = RetryAfterExceededException.DetermineProviderFromUri( uri );

        // Assert
        Assert.AreEqual( SupportedProviders.Tidal, result );
    }

    /// <summary>
    /// Verifies that DetermineProviderFromUri returns null for unknown URIs.
    /// </summary>
    [TestMethod]
    public void DetermineProviderFromUri_WithUnknownUri_ShouldReturnNull( ) {
        // Arrange
        Uri uri = new( "https://unknown-api.example.com/endpoint" );

        // Act
        SupportedProviders? result = RetryAfterExceededException.DetermineProviderFromUri( uri );

        // Assert
        Assert.IsNull( result );
    }

    /// <summary>
    /// Verifies that DetermineProviderFromUri returns null for null URIs.
    /// </summary>
    [TestMethod]
    public void DetermineProviderFromUri_WithNullUri_ShouldReturnNull( ) {
        // Act
        SupportedProviders? result = RetryAfterExceededException.DetermineProviderFromUri( null );

        // Assert
        Assert.IsNull( result );
    }

    /// <summary>
    /// Verifies that DetermineProviderFromUri is case-insensitive for host matching.
    /// </summary>
    [TestMethod]
    public void DetermineProviderFromUri_IsCaseInsensitive( ) {
        // Arrange
        Uri uri = new( "https://API.SPOTIFY.COM/v1/tracks/123" );

        // Act
        SupportedProviders? result = RetryAfterExceededException.DetermineProviderFromUri( uri );

        // Assert
        Assert.AreEqual( SupportedProviders.Spotify, result );
    }

    #endregion

    #region Exception Behavior Tests

    /// <summary>
    /// Verifies that the exception can be serialized to string.
    /// </summary>
    [TestMethod]
    public void Exception_ShouldBeSerializable( ) {
        // Arrange
        TimeSpan retryAfterValue = TimeSpan.FromSeconds( 300 );
        TimeSpan threshold = TimeSpan.FromSeconds( 120 );
        RetryAfterExceededException exception = new( retryAfterValue, threshold, null, null );

        // Act - Convert to string (basic serialization test)
        string result = exception.ToString( );

        // Assert
        Assert.IsNotNull( result );
        Assert.Contains( "RetryAfterExceededException", result );
    }

    /// <summary>
    /// Verifies that <see cref="RetryAfterExceededException"/> inherits from <see cref="Exception"/>.
    /// </summary>
    [TestMethod]
    public void Exception_ShouldInheritFromException( ) {
        // Arrange
        TimeSpan retryAfterValue = TimeSpan.FromSeconds( 300 );
        TimeSpan threshold = TimeSpan.FromSeconds( 120 );

        // Act
        RetryAfterExceededException exception = new( retryAfterValue, threshold, null, null );

        // Assert
        _ = Assert.IsInstanceOfType<Exception>( exception );
    }

    /// <summary>
    /// Verifies that <see cref="RetryAfterExceededException"/> can be caught as base <see cref="Exception"/>.
    /// </summary>
    [TestMethod]
    public void Exception_CanBeCaughtAsBaseException( ) {
        // Arrange
        TimeSpan retryAfterValue = TimeSpan.FromSeconds( 300 );
        TimeSpan threshold = TimeSpan.FromSeconds( 120 );

        // Act & Assert
        _ = Assert.ThrowsExactly<RetryAfterExceededException>(
            ( ) => throw new RetryAfterExceededException( retryAfterValue, threshold, null, null )
        );
    }

    #endregion

    #region Data Preservation Tests (for future re-queue)

    /// <summary>
    /// Verifies that the exception preserves all data needed for re-queue operations.
    /// </summary>
    [TestMethod]
    public void Exception_ShouldPreserveAllDataForRequeue( ) {
        // Arrange - Simulate a real Spotify rate limit scenario
        TimeSpan retryAfterValue = TimeSpan.FromSeconds( 3600 ); // 1 hour
        TimeSpan threshold = TimeSpan.FromSeconds( 120 );
        Uri requestUri = new( "https://api.spotify.com/v1/search?q=test&type=track" );
        SupportedProviders provider = SupportedProviders.Spotify;

        // Act
        RetryAfterExceededException exception = new( retryAfterValue, threshold, requestUri, provider );

        // Assert - All data needed for re-queue is preserved
        Assert.AreEqual( TimeSpan.FromSeconds( 3600 ), exception.RetryAfterValue );
        Assert.AreEqual( TimeSpan.FromSeconds( 120 ), exception.Threshold );
        Assert.AreEqual( "https://api.spotify.com/v1/search?q=test&type=track", exception.RequestUri?.ToString( ) );
        Assert.AreEqual( SupportedProviders.Spotify, exception.Provider );

        // Can calculate when to retry
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset suggestedRetryTime = now.Add(exception.RetryAfterValue);
        Assert.IsGreaterThan(now, suggestedRetryTime);
    }

    #endregion
}
