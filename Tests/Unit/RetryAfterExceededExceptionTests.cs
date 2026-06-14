using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Exceptions;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="RetryAfterExceededException"/>, thrown when a provider's
/// <c>Retry-After</c> exceeds the configured threshold. Verify that the constructors set the
/// retry-after, threshold, request URI, provider, and inner exception; that the formatted message
/// includes the retry-after seconds, threshold, provider name (or <c>Unknown</c>), and host; the
/// static <see cref="RetryAfterExceededException.DetermineProviderFromUri"/> host-to-provider mapping
/// (including case-insensitivity, null, and unknown hosts); standard exception behavior; and that all
/// data is preserved for a later re-queue.
/// </summary>
[TestClass]
public class RetryAfterExceededExceptionTests {

    #region Constructor Tests

    /// <summary>
    /// The full constructor sets <c>RetryAfterValue</c>, <c>Threshold</c>, <c>RequestUri</c>, and
    /// <c>Provider</c>.
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

    /// <summary>The constructor accepts a null request URI and provider, leaving both null.</summary>
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

    /// <summary>The inner-exception constructor sets <see cref="System.Exception.InnerException"/>.</summary>
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

    /// <summary>The message includes the retry-after value in seconds (300).</summary>
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

    /// <summary>The message includes the threshold in seconds (120).</summary>
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

    /// <summary>The message includes the provider name when a provider is supplied.</summary>
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

    /// <summary>The message reads <c>Unknown</c> for the provider when none is supplied.</summary>
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

    /// <summary>The message includes the request URI host when a request URI is supplied.</summary>
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

    /// <summary>A Spotify API host maps to <see cref="SupportedProviders.Spotify"/>.</summary>
    [TestMethod]
    public void DetermineProviderFromUri_WithSpotifyUri_ShouldReturnSpotify( ) {
        // Arrange
        Uri uri = new( "https://api.spotify.com/v1/tracks/123" );

        // Act
        SupportedProviders? result = RetryAfterExceededException.DetermineProviderFromUri( uri );

        // Assert
        Assert.AreEqual( SupportedProviders.Spotify, result );
    }

    /// <summary>A Spotify accounts/auth host maps to <see cref="SupportedProviders.Spotify"/>.</summary>
    [TestMethod]
    public void DetermineProviderFromUri_WithSpotifyAuthUri_ShouldReturnSpotify( ) {
        // Arrange
        Uri uri = new( "https://accounts.spotify.com/api/token" );

        // Act
        SupportedProviders? result = RetryAfterExceededException.DetermineProviderFromUri( uri );

        // Assert
        Assert.AreEqual( SupportedProviders.Spotify, result );
    }

    /// <summary>An Apple Music API host maps to <see cref="SupportedProviders.AppleMusic"/>.</summary>
    [TestMethod]
    public void DetermineProviderFromUri_WithAppleMusicUri_ShouldReturnAppleMusic( ) {
        // Arrange
        Uri uri = new( "https://api.music.apple.com/v1/catalog/us/songs/123" );

        // Act
        SupportedProviders? result = RetryAfterExceededException.DetermineProviderFromUri( uri );

        // Assert
        Assert.AreEqual( SupportedProviders.AppleMusic, result );
    }

    /// <summary>A Tidal API host maps to <see cref="SupportedProviders.Tidal"/>.</summary>
    [TestMethod]
    public void DetermineProviderFromUri_WithTidalApiUri_ShouldReturnTidal( ) {
        // Arrange
        Uri uri = new( "https://openapi.tidal.com/v2/tracks/123" );

        // Act
        SupportedProviders? result = RetryAfterExceededException.DetermineProviderFromUri( uri );

        // Assert
        Assert.AreEqual( SupportedProviders.Tidal, result );
    }

    /// <summary>A Tidal auth host maps to <see cref="SupportedProviders.Tidal"/>.</summary>
    [TestMethod]
    public void DetermineProviderFromUri_WithTidalAuthUri_ShouldReturnTidal( ) {
        // Arrange
        Uri uri = new( "https://auth.tidal.com/v1/oauth2/token" );

        // Act
        SupportedProviders? result = RetryAfterExceededException.DetermineProviderFromUri( uri );

        // Assert
        Assert.AreEqual( SupportedProviders.Tidal, result );
    }

    /// <summary>A host matching no known provider returns <c>null</c>.</summary>
    [TestMethod]
    public void DetermineProviderFromUri_WithUnknownUri_ShouldReturnNull( ) {
        // Arrange
        Uri uri = new( "https://unknown-api.example.com/endpoint" );

        // Act
        SupportedProviders? result = RetryAfterExceededException.DetermineProviderFromUri( uri );

        // Assert
        Assert.IsNull( result );
    }

    /// <summary>A null URI returns <c>null</c>.</summary>
    [TestMethod]
    public void DetermineProviderFromUri_WithNullUri_ShouldReturnNull( ) {
        // Act
        SupportedProviders? result = RetryAfterExceededException.DetermineProviderFromUri( null );

        // Assert
        Assert.IsNull( result );
    }

    /// <summary>Host matching is case-insensitive: an upper-case Spotify host still maps to Spotify.</summary>
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
    /// <c>ToString</c> produces a non-null representation that names the exception type.
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

    /// <summary>The type derives from <see cref="System.Exception"/>.</summary>
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

    /// <summary>The exception can be thrown and caught as its own type.</summary>
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
    /// The exception preserves retry-after, threshold, request URI, and provider intact, so a caller
    /// can compute a future retry time (now + retry-after) for re-queueing.
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
        Assert.IsGreaterThan( now, suggestedRetryTime );
    }

    #endregion
}
