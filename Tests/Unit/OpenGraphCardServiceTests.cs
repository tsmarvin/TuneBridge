using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Core.Domain.Services.Cards;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests <see cref="OpenGraphCardService"/>, the in-memory store of cross-provider results addressable by a
/// generated card id for social-preview (OpenGraph) pages. Covers the <c>Domain</c> and feature-flag
/// <c>IsEnabled</c> (gated on a non-empty domain), the card URL returned by <c>StoreResult</c>, and the
/// constructor's range guards on the expiration-hours and cleanup-interval arguments.
/// </summary>
[TestClass]
public class OpenGraphCardServiceTests {
    /// <summary>
    /// Verifies <see cref="OpenGraphCardService.Domain"/> returns the configured base domain.
    /// </summary>
    [TestMethod]
    public void Domain_WhenSet_ReturnsDomain( ) {
        // Arrange
        string domain = "bridgebeats.link";
        OpenGraphCardService service = new(domain, 1, 500);

        // Act
        string result = service.Domain;

        // Assert
        Assert.AreEqual( domain, result );
    }

    /// <summary>
    /// Verifies <see cref="OpenGraphCardService.IsEnabled"/> is true when a domain is configured.
    /// </summary>
    [TestMethod]
    public void IsEnabled_WhenDomainIsNotEmpty_ReturnsTrue( ) {
        // Arrange
        string domain = "bridgebeats.link";
        OpenGraphCardService service = new(domain, 1, 500);

        // Act
        bool result = service.IsEnabled;

        // Assert
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies <see cref="OpenGraphCardService.IsEnabled"/> is false when the domain is empty (the feature is off).
    /// </summary>
    [TestMethod]
    public void IsEnabled_WhenDomainIsEmpty_ReturnsFalse( ) {
        // Arrange
        string domain = string.Empty;
        OpenGraphCardService service = new(domain, 1, 500);

        // Act
        bool result = service.IsEnabled;

        // Assert
        Assert.IsFalse( result );
    }

    /// <summary>
    /// Verifies <see cref="OpenGraphCardService.StoreResult"/> stores a result and returns a card URL of the form
    /// <c>https://{domain}/card/{id}</c>.
    /// </summary>
    [TestMethod]
    public void StoreResult_WhenCalled_ReturnsUrlWithDomain( ) {
        // Arrange
        string domain = "bridgebeats.link";
        OpenGraphCardService service = new(domain, 1, 500);
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
        Assert.StartsWith( $"https://{domain}/card/", cardUrl, "Card URL should start with base URL" );
    }

    /// <summary>
    /// Verifies a zero expiration-hours argument is rejected with an <see cref="ArgumentOutOfRangeException"/> naming
    /// <c>expirationHours</c>.
    /// </summary>
    [TestMethod]
    public void Constructor_WithZeroExpirationHours_ThrowsArgumentOutOfRangeException( ) {
        // Arrange
        string domain = "bridgebeats.link";

        // Act & Assert
        ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new OpenGraphCardService(domain, 0, 500)
        );
        Assert.AreEqual( "expirationHours", exception.ParamName );
    }

    /// <summary>
    /// Verifies a negative expiration-hours argument is rejected with an <see cref="ArgumentOutOfRangeException"/>
    /// naming <c>expirationHours</c>.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNegativeExpirationHours_ThrowsArgumentOutOfRangeException( ) {
        // Arrange
        string domain = "bridgebeats.link";

        // Act & Assert
        ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new OpenGraphCardService(domain, -1, 500)
        );
        Assert.AreEqual( "expirationHours", exception.ParamName );
    }

    /// <summary>
    /// Verifies a zero cleanup-interval argument is rejected with an <see cref="ArgumentOutOfRangeException"/> naming
    /// <c>cleanupInterval</c>.
    /// </summary>
    [TestMethod]
    public void Constructor_WithZeroCleanupInterval_ThrowsArgumentOutOfRangeException( ) {
        // Arrange
        string domain = "bridgebeats.link";

        // Act & Assert
        ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new OpenGraphCardService(domain, 1, 0)
        );
        Assert.AreEqual( "cleanupInterval", exception.ParamName );
    }

    /// <summary>
    /// Verifies a negative cleanup-interval argument is rejected with an <see cref="ArgumentOutOfRangeException"/>
    /// naming <c>cleanupInterval</c>.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNegativeCleanupInterval_ThrowsArgumentOutOfRangeException( ) {
        // Arrange
        string domain = "bridgebeats.link";

        // Act & Assert
        ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new OpenGraphCardService(domain, 1, -1)
        );
        Assert.AreEqual( "cleanupInterval", exception.ParamName );
    }
}
