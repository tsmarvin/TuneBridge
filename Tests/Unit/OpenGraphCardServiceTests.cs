using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Services.Cards;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="OpenGraphCardService"/> validating card URL generation and configuration.
/// </summary>
[TestClass]
public class OpenGraphCardServiceTests {

    /// <summary>
    /// Verifies that the BaseUrl property returns the configured base URL.
    /// </summary>
    [TestMethod]
    public void BaseUrl_WhenSet_ReturnsBaseUrl( ) {
        // Arrange
        string baseUrl = "bridgebeats.link";
        OpenGraphCardService service = new( baseUrl, 1, 500 );

        // Act
        string result = service.BaseUrl;

        // Assert
        Assert.AreEqual( baseUrl, result );
    }

    /// <summary>
    /// Verifies that IsEnabled returns true when base URL is configured.
    /// </summary>
    [TestMethod]
    public void IsEnabled_WhenBaseUrlIsNotEmpty_ReturnsTrue( ) {
        // Arrange
        string baseUrl = "bridgebeats.link";
        OpenGraphCardService service = new( baseUrl, 1, 500 );

        // Act
        bool result = service.IsEnabled;

        // Assert
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that IsEnabled returns false when base URL is empty.
    /// </summary>
    [TestMethod]
    public void IsEnabled_WhenBaseUrlIsEmpty_ReturnsFalse( ) {
        // Arrange
        string baseUrl = string.Empty;
        OpenGraphCardService service = new( baseUrl, 1, 500 );

        // Act
        bool result = service.IsEnabled;

        // Assert
        Assert.IsFalse( result );
    }

    /// <summary>
    /// Verifies that StoreResult returns a URL containing the configured base URL.
    /// </summary>
    [TestMethod]
    public void StoreResult_WhenCalled_ReturnsUrlWithBaseUrl( ) {
        // Arrange
        string baseUrl = "bridgebeats.link";
        OpenGraphCardService service = new( baseUrl, 1, 500 );
        MediaLinkResult result = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResult> {
                {
                    SupportedProviders.AppleMusic, new MusicLookupResult {
                        Title = "Test Track",
                        Artist = "Test Artist",
                        URL = "https://music.apple.com/us/album/test/123",
                        IsAlbum = false,
                        IsPrimary = true
                    }
                }
            }
        };

        // Act
        string cardUrl = service.StoreResult( result );

        // Assert
        Assert.StartsWith( $"https://{baseUrl}/card/", cardUrl, "Card URL should start with base URL" );
    }

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentOutOfRangeException"/> when expiration hours is zero.
    /// </summary>
    [TestMethod]
    public void Constructor_WithZeroExpirationHours_ThrowsArgumentOutOfRangeException( ) {
        // Arrange
        string baseUrl = "bridgebeats.link";

        // Act & Assert
        ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            ( ) => new OpenGraphCardService( baseUrl, 0, 500 )
        );
        Assert.AreEqual( "expirationHours", exception.ParamName );
    }

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentOutOfRangeException"/> when expiration hours is negative.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNegativeExpirationHours_ThrowsArgumentOutOfRangeException( ) {
        // Arrange
        string baseUrl = "bridgebeats.link";

        // Act & Assert
        ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            ( ) => new OpenGraphCardService( baseUrl, -1, 500 )
        );
        Assert.AreEqual( "expirationHours", exception.ParamName );
    }

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentOutOfRangeException"/> when cleanup interval is zero.
    /// </summary>
    [TestMethod]
    public void Constructor_WithZeroCleanupInterval_ThrowsArgumentOutOfRangeException( ) {
        // Arrange
        string baseUrl = "bridgebeats.link";

        // Act & Assert
        ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            ( ) => new OpenGraphCardService( baseUrl, 1, 0 )
        );
        Assert.AreEqual( "cleanupInterval", exception.ParamName );
    }

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentOutOfRangeException"/> when cleanup interval is negative.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNegativeCleanupInterval_ThrowsArgumentOutOfRangeException( ) {
        // Arrange
        string baseUrl = "bridgebeats.link";

        // Act & Assert
        ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            ( ) => new OpenGraphCardService( baseUrl, 1, -1 )
        );
        Assert.AreEqual( "cleanupInterval", exception.ParamName );
    }
}
