using BridgeBeats.Providers.AppleMusic;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for AppleMusicLinkParser to verify URL parsing logic.
/// </summary>
[TestClass]
public class AppleMusicLinkParserTests {

    /// <summary>
    /// Verifies that TryParseUri extracts track ID correctly when URL contains 'ls' query parameter.
    /// </summary>
    [TestMethod]
    public void TryParseUri_WithTrackIdAndLsQueryParam_ExtractsTrackIdCorrectly( ) {
        // Arrange - URL from issue #1
        string link = "https://music.apple.com/cl/album/rundgang-um-die-transzendentale-saule-der-singularitat/286930912?i=286931431&ls";

        // Act
        bool result = AppleMusicLinkParser.TryParseUri( link, out string requestUri, out string storefront, out bool isAlbum );

        // Assert
        Assert.IsTrue( result, "Should successfully parse the Apple Music URL" );
        Assert.IsFalse( isAlbum, "Should recognize this as a track (not album) due to ?i= parameter" );
        Assert.AreEqual( "cl", storefront, "Should extract 'cl' as storefront" );
        Assert.AreEqual( "cl/songs/286931431", requestUri, "Should extract song ID '286931431' without query parameters" );
    }

    /// <summary>
    /// Verifies that TryParseUri extracts track ID correctly when URL contains 'uo' query parameter.
    /// </summary>
    [TestMethod]
    public void TryParseUri_WithTrackIdAndUoQueryParam_ExtractsTrackIdCorrectly( ) {
        // Arrange - URL from issue #2
        string link = "https://music.apple.com/au/album/apricots/1531704818?i=1531704822&uo=4";

        // Act
        bool result = AppleMusicLinkParser.TryParseUri( link, out string requestUri, out string storefront, out bool isAlbum );

        // Assert
        Assert.IsTrue( result, "Should successfully parse the Apple Music URL" );
        Assert.IsFalse( isAlbum, "Should recognize this as a track (not album) due to ?i= parameter" );
        Assert.AreEqual( "au", storefront, "Should extract 'au' as storefront" );
        Assert.AreEqual( "au/songs/1531704822", requestUri, "Should extract song ID '1531704822' without query parameters" );
    }

    /// <summary>
    /// Verifies that TryParseUri extracts track ID correctly when URL has no additional query parameters.
    /// </summary>
    [TestMethod]
    public void TryParseUri_WithTrackIdWithoutQueryParams_ExtractsTrackIdCorrectly( ) {
        // Arrange - URL without additional query parameters
        string link = "https://music.apple.com/us/album/test-album/123456?i=789012";

        // Act
        bool result = AppleMusicLinkParser.TryParseUri( link, out string requestUri, out string storefront, out bool isAlbum );

        // Assert
        Assert.IsTrue( result, "Should successfully parse the Apple Music URL" );
        Assert.IsFalse( isAlbum, "Should recognize this as a track (not album) due to ?i= parameter" );
        Assert.AreEqual( "us", storefront, "Should extract 'us' as storefront" );
        Assert.AreEqual( "us/songs/789012", requestUri, "Should extract song ID '789012'" );
    }

    /// <summary>
    /// Verifies that TryParseUri extracts album ID correctly when URL has query parameters but no track ID.
    /// </summary>
    [TestMethod]
    public void TryParseUri_WithAlbumIdAndQueryParams_ExtractsAlbumIdCorrectly( ) {
        // Arrange - Album URL with query parameters (no ?i= so should treat as album)
        string link = "https://music.apple.com/us/album/test-album/123456?l=en&app=music";

        // Act
        bool result = AppleMusicLinkParser.TryParseUri( link, out string requestUri, out string storefront, out bool isAlbum );

        // Assert
        Assert.IsTrue( result, "Should successfully parse the Apple Music URL" );
        Assert.IsTrue( isAlbum, "Should recognize this as an album (no ?i= parameter)" );
        Assert.AreEqual( "us", storefront, "Should extract 'us' as storefront" );
        Assert.AreEqual( "us/albums/123456", requestUri, "Should extract album ID '123456' without query parameters" );
    }

    /// <summary>
    /// Verifies that TryParseUri extracts ID correctly when URL contains multiple query parameters.
    /// </summary>
    [TestMethod]
    public void TryParseUri_WithMultipleQueryParams_ExtractsIdCorrectly( ) {
        // Arrange - URL with multiple query parameters
        string link = "https://music.apple.com/jp/album/test/999?i=111&at=1000l3&app=music&ls=1";

        // Act
        bool result = AppleMusicLinkParser.TryParseUri( link, out string requestUri, out string storefront, out bool isAlbum );

        // Assert
        Assert.IsTrue( result, "Should successfully parse the Apple Music URL" );
        Assert.IsFalse( isAlbum, "Should recognize this as a track due to ?i= parameter" );
        Assert.AreEqual( "jp", storefront, "Should extract 'jp' as storefront" );
        Assert.AreEqual( "jp/songs/111", requestUri, "Should extract song ID '111' without any query parameters" );
    }

    /// <summary>
    /// Verifies that TryParseUri extracts album ID correctly when URL only has non-track query parameters.
    /// </summary>
    [TestMethod]
    public void TryParseUri_AlbumWithQueryParamsOnly_ExtractsAlbumIdCorrectly( ) {
        // Arrange - Album URL with query parameters but no ?i= (should be treated as album)
        string link = "https://music.apple.com/us/album/test-album/286930912?ls=1&app=music";

        // Act
        bool result = AppleMusicLinkParser.TryParseUri( link, out string requestUri, out string storefront, out bool isAlbum );

        // Assert
        Assert.IsTrue( result, "Should successfully parse the Apple Music URL" );
        Assert.IsTrue( isAlbum, "Should recognize this as an album (no ?i= parameter)" );
        Assert.AreEqual( "us", storefront, "Should extract 'us' as storefront" );
        Assert.AreEqual( "us/albums/286930912", requestUri, "Should extract album ID '286930912' without query parameters" );
    }

    /// <summary>
    /// Verifies that TryParseUri extracts album ID correctly when URL contains affiliate token query parameter.
    /// </summary>
    [TestMethod]
    public void TryParseUri_AlbumWithAtQueryParam_ExtractsAlbumIdCorrectly( ) {
        // Arrange - Album URL with affiliate token query parameter
        string link = "https://music.apple.com/gb/album/test/1234567890?at=1000l3Hc&uo=4";

        // Act
        bool result = AppleMusicLinkParser.TryParseUri( link, out string requestUri, out string storefront, out bool isAlbum );

        // Assert
        Assert.IsTrue( result, "Should successfully parse the Apple Music URL" );
        Assert.IsTrue( isAlbum, "Should recognize this as an album (no ?i= parameter)" );
        Assert.AreEqual( "gb", storefront, "Should extract 'gb' as storefront" );
        Assert.AreEqual( "gb/albums/1234567890", requestUri, "Should extract album ID '1234567890' without query parameters" );
    }

    /// <summary>
    /// Verifies that TryParseUri extracts song ID correctly from direct song URLs with query parameters.
    /// </summary>
    [TestMethod]
    public void TryParseUri_DirectSongUrlWithQueryParams_ExtractsSongIdCorrectly( ) {
        // Arrange - Direct song URL (not album with ?i=) with query parameters
        string link = "https://music.apple.com/us/song/test-song/987654321?at=1000l3Hc&app=music";

        // Act
        bool result = AppleMusicLinkParser.TryParseUri( link, out string requestUri, out string storefront, out bool isAlbum );

        // Assert
        Assert.IsTrue( result, "Should successfully parse the Apple Music URL" );
        Assert.IsFalse( isAlbum, "Should recognize this as a song" );
        Assert.AreEqual( "us", storefront, "Should extract 'us' as storefront" );
        Assert.AreEqual( "us/songs/987654321", requestUri, "Should extract song ID '987654321' without query parameters" );
    }
}
