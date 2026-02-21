using BridgeBeats.Core.Domain.Providers.Common;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="LinkNormalizer"/>.
/// </summary>
[TestClass]
public class LinkNormalizerTests {

    #region Query Parameter Removal Tests

    /// <summary>
    /// Verifies that Normalize removes Spotify tracking query parameters from URLs while preserving case-sensitive IDs.
    /// </summary>
    [TestMethod]
    public void Normalize_RemovesQueryParameters_FromUrl( ) {
        // Arrange
        string url = "https://open.spotify.com/track/2zyTP97uGsIc1C4KNNEkyn?si=MGp6ZDEjQX6yT3uI6ayMDQ";

        // Act
        string normalized = LinkNormalizer.Normalize( url );

        // Assert - domain lowercase, path preserves case
        Assert.AreEqual( "open.spotify.com/track/2zyTP97uGsIc1C4KNNEkyn", normalized );
    }

    /// <summary>
    /// Verifies that Normalize removes tracking query parameters with multiple params.
    /// </summary>
    [TestMethod]
    public void Normalize_RemovesMultipleQueryParameters( ) {
        // Arrange
        string url = "https://example.com/path?si=tracking1&utm_source=facebook";

        // Act
        string normalized = LinkNormalizer.Normalize( url );

        // Assert - tracking params removed
        Assert.AreEqual( "example.com/path", normalized );
    }

    /// <summary>
    /// Verifies that Normalize removes fragments from URLs.
    /// </summary>
    [TestMethod]
    public void Normalize_RemovesFragments_FromUrl( ) {
        // Arrange
        string url = "https://example.com/path#section";

        // Act
        string normalized = LinkNormalizer.Normalize( url );

        // Assert
        Assert.AreEqual( "example.com/path", normalized );
    }

    /// <summary>
    /// Verifies that Normalize removes both tracking query params and fragments.
    /// </summary>
    [TestMethod]
    public void Normalize_RemovesQueryAndFragment( ) {
        // Arrange
        string url = "https://example.com/path?si=tracking#section";

        // Act
        string normalized = LinkNormalizer.Normalize( url );

        // Assert
        Assert.AreEqual( "example.com/path", normalized );
    }

    #endregion

    #region Protocol and Prefix Removal Tests

    /// <summary>
    /// Verifies that Normalize removes https protocol.
    /// </summary>
    [TestMethod]
    public void Normalize_RemovesHttpsProtocol( ) {
        // Arrange
        string url = "https://example.com/path";

        // Act
        string normalized = LinkNormalizer.Normalize( url );

        // Assert
        Assert.AreEqual( "example.com/path", normalized );
    }

    /// <summary>
    /// Verifies that Normalize removes http protocol.
    /// </summary>
    [TestMethod]
    public void Normalize_RemovesHttpProtocol( ) {
        // Arrange
        string url = "http://example.com/path";

        // Act
        string normalized = LinkNormalizer.Normalize( url );

        // Assert
        Assert.AreEqual( "example.com/path", normalized );
    }

    /// <summary>
    /// Verifies that Normalize removes www prefix.
    /// </summary>
    [TestMethod]
    public void Normalize_RemovesWwwPrefix( ) {
        // Arrange
        string url = "https://www.example.com/path";

        // Act
        string normalized = LinkNormalizer.Normalize( url );

        // Assert
        Assert.AreEqual( "example.com/path", normalized );
    }

    /// <summary>
    /// Verifies that Normalize removes trailing slash.
    /// </summary>
    [TestMethod]
    public void Normalize_RemovesTrailingSlash( ) {
        // Arrange
        string url = "https://example.com/path/";

        // Act
        string normalized = LinkNormalizer.Normalize( url );

        // Assert
        Assert.AreEqual( "example.com/path", normalized );
    }

    #endregion

    #region Edge Cases

    /// <summary>
    /// Verifies that Normalize returns empty for null input.
    /// </summary>
    [TestMethod]
    public void Normalize_ReturnsEmpty_ForNullInput( ) {
        // Act
        string normalized = LinkNormalizer.Normalize( null! );

        // Assert
        Assert.AreEqual( string.Empty, normalized );
    }

    /// <summary>
    /// Verifies that Normalize returns empty for empty input.
    /// </summary>
    [TestMethod]
    public void Normalize_ReturnsEmpty_ForEmptyInput( ) {
        // Act
        string normalized = LinkNormalizer.Normalize( "" );

        // Assert
        Assert.AreEqual( string.Empty, normalized );
    }

    /// <summary>
    /// Verifies that Normalize returns empty for whitespace input.
    /// </summary>
    [TestMethod]
    public void Normalize_ReturnsEmpty_ForWhitespaceInput( ) {
        // Act
        string normalized = LinkNormalizer.Normalize( "   " );

        // Assert
        Assert.AreEqual( string.Empty, normalized );
    }

    #endregion

    #region Complete Normalization Test

    /// <summary>
    /// Verifies that Normalize handles all transformations together while preserving case-sensitive paths.
    /// </summary>
    [TestMethod]
    public void Normalize_AppliesAllTransformations( ) {
        // Arrange
        string url = "https://www.Example.COM/Path/?si=tracking#section";

        // Act
        string normalized = LinkNormalizer.Normalize( url );

        // Assert - domain lowercase, path preserves case
        Assert.AreEqual( "example.com/Path", normalized );
    }

    /// <summary>
    /// Verifies that Spotify URLs with tracking params normalize correctly while preserving case-sensitive IDs.
    /// </summary>
    [TestMethod]
    public void Normalize_SpotifyUrlsWithTracking_NormalizeIdentically( ) {
        // Arrange
        string url1 = "https://open.spotify.com/track/2zyTP97uGsIc1C4KNNEkyn";
        string url2 = "https://open.spotify.com/track/2zyTP97uGsIc1C4KNNEkyn?si=MGp6ZDEjQX6yT3uI6ayMDQ";
        string url3 = "https://open.spotify.com/track/2zyTP97uGsIc1C4KNNEkyn?si=differentcode123";

        // Act
        string normalized1 = LinkNormalizer.Normalize( url1 );
        string normalized2 = LinkNormalizer.Normalize( url2 );
        string normalized3 = LinkNormalizer.Normalize( url3 );

        // Assert
        Assert.AreEqual( normalized1, normalized2, "URLs with and without ?si= should normalize to same value" );
        Assert.AreEqual( normalized1, normalized3, "URLs with different ?si= values should normalize to same value" );
        Assert.AreEqual( "open.spotify.com/track/2zyTP97uGsIc1C4KNNEkyn", normalized1 );
    }

    #endregion

    #region Apple Music Semantic Parameter Preservation Tests

    /// <summary>
    /// Verifies that Apple Music ?i= parameter is preserved as it's semantic (identifies track in album).
    /// </summary>
    [TestMethod]
    public void Normalize_PreservesAppleMusicTrackParameter( ) {
        // Arrange
        string url = "https://music.apple.com/us/album/test-album/123456?i=789012";

        // Act
        string normalized = LinkNormalizer.Normalize( url );

        // Assert - ?i= parameter preserved, tracking params removed
        Assert.AreEqual( "music.apple.com/us/album/test-album/123456?i=789012", normalized );
    }

    /// <summary>
    /// Verifies that Apple Music ?i= is preserved but tracking params are removed.
    /// </summary>
    [TestMethod]
    public void Normalize_PreservesAppleMusicTrackParameter_RemovesTrackingParams( ) {
        // Arrange
        string url = "https://music.apple.com/us/album/test-album/123456?i=789012&at=1000l3&ls=1&uo=4";

        // Act
        string normalized = LinkNormalizer.Normalize( url );

        // Assert - ?i= preserved, at/ls/uo removed
        Assert.AreEqual( "music.apple.com/us/album/test-album/123456?i=789012", normalized );
    }

    /// <summary>
    /// Verifies that Apple Music album URLs without ?i= have tracking params removed.
    /// </summary>
    [TestMethod]
    public void Normalize_AppleMusicAlbum_RemovesTrackingParams( ) {
        // Arrange
        string url = "https://music.apple.com/us/album/test-album/123456?at=1000l3&app=music";

        // Act
        string normalized = LinkNormalizer.Normalize( url );

        // Assert - all tracking params removed
        Assert.AreEqual( "music.apple.com/us/album/test-album/123456", normalized );
    }

    /// <summary>
    /// Verifies that different Apple Music URLs with same album+track are treated as distinct.
    /// </summary>
    [TestMethod]
    public void Normalize_AppleMusicDifferentTracks_NormalizeDifferently( ) {
        // Arrange
        string album = "https://music.apple.com/us/album/test-album/123456";
        string track1 = "https://music.apple.com/us/album/test-album/123456?i=789012";
        string track2 = "https://music.apple.com/us/album/test-album/123456?i=789013";

        // Act
        string normalizedAlbum = LinkNormalizer.Normalize( album );
        string normalizedTrack1 = LinkNormalizer.Normalize( track1 );
        string normalizedTrack2 = LinkNormalizer.Normalize( track2 );

        // Assert - album and tracks are distinct
        Assert.AreNotEqual( normalizedAlbum, normalizedTrack1, "Album and track should be different" );
        Assert.AreNotEqual( normalizedTrack1, normalizedTrack2, "Different tracks should be different" );
    }

    #endregion

    #region Case Sensitivity Tests

    /// <summary>
    /// Verifies that domain is normalized to lowercase but path preserves case.
    /// </summary>
    [TestMethod]
    public void Normalize_LowercasesDomain_PreservesPathCase( ) {
        // Arrange
        string url = "https://OPEN.SPOTIFY.COM/track/2zyTP97uGsIc1C4KNNEkyn";

        // Act
        string normalized = LinkNormalizer.Normalize( url );

        // Assert - domain lowercase, path preserves original case
        Assert.AreEqual( "open.spotify.com/track/2zyTP97uGsIc1C4KNNEkyn", normalized );
    }

    /// <summary>
    /// Verifies that different case IDs are treated as different resources.
    /// </summary>
    [TestMethod]
    public void Normalize_PreservesIDCase_DifferentCasesAreDifferent( ) {
        // Arrange
        string url1 = "https://example.com/track/AbCdEf";
        string url2 = "https://example.com/track/abcdef";

        // Act
        string normalized1 = LinkNormalizer.Normalize( url1 );
        string normalized2 = LinkNormalizer.Normalize( url2 );

        // Assert - IDs with different case should be different
        Assert.AreNotEqual( normalized1, normalized2, "Case-sensitive IDs should not match" );
    }

    #endregion
}
