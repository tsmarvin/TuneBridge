using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Core.Domain.Services.Cards;
using BridgeBeats.Core.Infrastructure.Storage;

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
        OpenGraphCardService service = new(domain, 1, 500, 10000);

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
        OpenGraphCardService service = new(domain, 1, 500, 10000);

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
        OpenGraphCardService service = new(domain, 1, 500, 10000);

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
        OpenGraphCardService service = new(domain, 1, 500, 10000);
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
            () => new OpenGraphCardService(domain, 0, 500, 10000)
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
            () => new OpenGraphCardService(domain, -1, 500, 10000)
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
            () => new OpenGraphCardService(domain, 1, 0, 10000)
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
            () => new OpenGraphCardService(domain, 1, -1, 10000)
        );
        Assert.AreEqual( "cleanupInterval", exception.ParamName );
    }

    /// <summary>
    /// Verifies a zero max-entries argument is rejected with an <see cref="ArgumentOutOfRangeException"/>
    /// naming <c>maxEntries</c>.
    /// </summary>
    [TestMethod]
    public void Constructor_WithZeroMaxEntries_ThrowsArgumentOutOfRangeException( ) {
        // Arrange
        string domain = "bridgebeats.link";

        // Act & Assert
        ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new OpenGraphCardService(domain, 1, 500, 0)
        );
        Assert.AreEqual( "maxEntries", exception.ParamName );
    }

    /// <summary>
    /// Verifies a negative max-entries argument is rejected with an <see cref="ArgumentOutOfRangeException"/>
    /// naming <c>maxEntries</c>.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNegativeMaxEntries_ThrowsArgumentOutOfRangeException( ) {
        // Arrange
        string domain = "bridgebeats.link";

        // Act & Assert
        ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new OpenGraphCardService(domain, 1, 500, -1)
        );
        Assert.AreEqual( "maxEntries", exception.ParamName );
    }

    /// <summary>
    /// Verifies that storing exactly cap distinct entries retains all of them and every entry resolves
    /// via GetResult.
    /// </summary>
    [TestMethod]
    public void Store_WithinCap_RetainsAllEntries( ) {
        // Arrange
        const int Cap = 10;
        OpenGraphCardService service = new("bridgebeats.link", 1, 500, Cap);

        // Act — store up to the cap with distinct entries
        List<string> storedIds = [];
        for (int i = 0; i < Cap; i++) {
            MediaLinkResult result = MakeResult( $"isrc{i:D6}" );
            string url = service.StoreResult( result );
            string id = url.Split( '/' ).Last( );
            storedIds.Add( id );
        }

        // Assert — store is at the cap and every entry resolves
        Assert.AreEqual( Cap, service.Count );
        foreach (string id in storedIds) {
            Assert.IsNotNull( service.GetResult( id ), $"Entry for id {id} should still be retrievable." );
        }
    }

    /// <summary>
    /// Verifies that storing many more distinct entries than the cap never allows the count to exceed
    /// the cap (nearest-expiry eviction keeps the store bounded under sustained writes).
    /// </summary>
    [TestMethod]
    public void Store_UnderSustainedDistinctWrites_StaysBounded( ) {
        // Arrange
        const int Cap = 10;
        OpenGraphCardService service = new("bridgebeats.link", 1, 500, Cap);

        // Act — store 100 distinct entries; count must never exceed the cap.
        // The Count <= Cap invariant is exact here because writes are sequential. In production the
        // cap is soft: a concurrent distinct-id writer can transiently push the count above maxEntries
        // by up to (concurrent writers − 1) before the lock-guarded eviction path catches up.
        for (int i = 0; i < 100; i++) {
            MediaLinkResult result = MakeResult( $"isrc{i:D6}" );
            _ = service.StoreResult( result );
            Assert.IsLessThanOrEqualTo( Cap, service.Count, $"Count {service.Count} exceeded cap {Cap} after {i + 1} writes." );
        }
    }

    /// <summary>
    /// Verifies that after writing cap entries then 5 more distinct ones, the 5 most-recently-written
    /// entries are still resolvable and the count stays bounded.
    /// </summary>
    [TestMethod]
    public void Store_AfterEviction_StillValidEntriesRetained( ) {
        // Arrange
        const int Cap = 10;
        OpenGraphCardService service = new("bridgebeats.link", 1, 500, Cap);

        // Act — fill to the cap, then write 5 more distinct entries
        for (int i = 0; i < Cap; i++) {
            _ = service.StoreResult( MakeResult( $"isrc{i:D6}" ) );
        }

        List<string> recentIds = [];
        for (int i = Cap; i < Cap + 5; i++) {
            MediaLinkResult result = MakeResult( $"isrc{i:D6}" );
            string url = service.StoreResult( result );
            string id = url.Split( '/' ).Last( );
            recentIds.Add( id );
        }

        // Assert — count is still bounded and the 5 recently-written entries resolve
        Assert.IsLessThanOrEqualTo( Cap, service.Count, $"Count {service.Count} exceeded cap {Cap}." );
        foreach (string id in recentIds) {
            Assert.IsNotNull( service.GetResult( id ), $"Recently written entry {id} should still be retrievable after eviction." );
        }
    }

    /// <summary>
    /// Builds a <see cref="MediaLinkResult"/> with a single Spotify entry carrying the given ISRC,
    /// so that each distinct ISRC produces a distinct rkey and therefore a distinct card id.
    /// </summary>
    /// <param name="isrc">The ISRC to embed as the external id.</param>
    private static MediaLinkResult MakeResult( string isrc ) {
        return new MediaLinkResult {
            Results = new Dictionary<SupportedProviders, MusicLookupResult> {
                {
                    SupportedProviders.Spotify, new MusicLookupResult {
                        ExternalId = isrc,
                        IsAlbum = false,
                        Title = "Track",
                        Artist = "Artist",
                        URL = $"https://open.spotify.com/track/{isrc}"
                    }
                }
            }
        };
    }
}
