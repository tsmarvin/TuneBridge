using BridgeBeats.Core.Domain.Providers.Common;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests <see cref="LinkNormalizer"/>, which canonicalizes a link for cache-key stability: it strips the
/// scheme and <c>www.</c>, lowercases the host while preserving path case, drops the fragment and the trailing
/// slash, and removes every query parameter except Apple Music's <c>i</c> track-within-album selector. Grouped by
/// query/fragment removal, protocol/prefix removal, edge cases, full normalization, Apple Music parameter
/// preservation, and case sensitivity.
/// </summary>
[TestClass]
public class LinkNormalizerTests {

    #region Query Parameter Removal Tests

    /// <summary>
    /// Verifies a Spotify tracking query parameter (<c>?si=</c>) is stripped, leaving host and path.
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
    /// Verifies all of several query parameters are removed.
    /// </summary>
    [TestMethod]
    public void Normalize_RemovesMultipleQueryParameters( ) {
        // Arrange - mix of known tracking params and unknown params
        string url = "https://example.com/path?si=tracking1&foo=bar&utm_source=facebook";

        // Act
        string normalized = LinkNormalizer.Normalize( url );

        // Assert - all params removed
        Assert.AreEqual( "example.com/path", normalized );
    }

    /// <summary>
    /// Verifies the URL fragment (<c>#section</c>) is removed.
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
    /// Verifies both the query string and the fragment are removed together.
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
    /// Verifies the <c>https://</c> scheme is stripped.
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
    /// Verifies the <c>http://</c> scheme is stripped.
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
    /// Verifies the <c>www.</c> host prefix is stripped.
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
    /// Verifies a trailing slash is trimmed.
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
    /// Verifies a null input normalizes to an empty string rather than throwing.
    /// </summary>
    [TestMethod]
    public void Normalize_ReturnsEmpty_ForNullInput( ) {
        // Act
        string normalized = LinkNormalizer.Normalize( null! );

        // Assert
        Assert.AreEqual( string.Empty, normalized );
    }

    /// <summary>
    /// Verifies an empty input normalizes to an empty string.
    /// </summary>
    [TestMethod]
    public void Normalize_ReturnsEmpty_ForEmptyInput( ) {
        // Act
        string normalized = LinkNormalizer.Normalize( "" );

        // Assert
        Assert.AreEqual( string.Empty, normalized );
    }

    /// <summary>
    /// Verifies a whitespace-only input normalizes to an empty string.
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
    /// Verifies a URL exercising every rule at once (scheme, <c>www.</c>, mixed-case host, trailing slash, query,
    /// fragment) normalizes to a lowercased host with case-preserved path and nothing else.
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
    /// Verifies the same Spotify track with no <c>?si=</c>, with one <c>?si=</c>, and with a different <c>?si=</c>
    /// all normalize to the same value, the key property that makes tracking-laden links share a cache key.
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
    /// Verifies the Apple Music <c>i</c> track selector is preserved (it identifies a track within an album, so it
    /// is semantically significant unlike tracking parameters).
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
    /// Verifies the <c>i</c> parameter is kept while the surrounding tracking parameters (<c>at</c>, <c>ls</c>,
    /// <c>uo</c>) are removed.
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
    /// Verifies an Apple Music album URL with only tracking parameters (no <c>i</c>) normalizes to just host and path.
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
    /// Verifies the album, and two tracks within it differing only by <c>i</c>, all normalize to distinct values,
    /// confirming the preserved <c>i</c> parameter keeps separate tracks from collapsing to one cache key.
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
    /// Verifies the host is lowercased while the path (a case-sensitive provider id) keeps its original case.
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
    /// Verifies two URLs whose path ids differ only in case normalize to different values (path case is significant).
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
