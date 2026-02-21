using BridgeBeats.Core.Domain.Providers.Common;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="LinkNormalizer"/>.
/// </summary>
[TestClass]
public class LinkNormalizerTests {

    #region Query Parameter Removal Tests

    /// <summary>
    /// Verifies that Normalize removes query parameters from URLs.
    /// </summary>
    [TestMethod]
    public void Normalize_RemovesQueryParameters_FromUrl( ) {
        // Arrange
        string url = "https://open.spotify.com/track/2zyTP97uGsIc1C4KNNEkyn?si=MGp6ZDEjQX6yT3uI6ayMDQ";

        // Act
        string normalized = LinkNormalizer.Normalize( url );

        // Assert
        Assert.AreEqual( "open.spotify.com/track/2zytp97ugsic1c4knnekyn", normalized );
    }

    /// <summary>
    /// Verifies that Normalize removes query parameters with multiple params.
    /// </summary>
    [TestMethod]
    public void Normalize_RemovesMultipleQueryParameters( ) {
        // Arrange
        string url = "https://example.com/path?param1=value1&param2=value2";

        // Act
        string normalized = LinkNormalizer.Normalize( url );

        // Assert
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
    /// Verifies that Normalize removes both query params and fragments.
    /// </summary>
    [TestMethod]
    public void Normalize_RemovesQueryAndFragment( ) {
        // Arrange
        string url = "https://example.com/path?param=value#section";

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
    /// Verifies that Normalize handles all transformations together.
    /// </summary>
    [TestMethod]
    public void Normalize_AppliesAllTransformations( ) {
        // Arrange
        string url = "https://www.example.com/path/?param=value#section";

        // Act
        string normalized = LinkNormalizer.Normalize( url );

        // Assert
        Assert.AreEqual( "example.com/path", normalized );
    }

    /// <summary>
    /// Verifies that Spotify URLs with tracking params normalize correctly.
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
        Assert.AreEqual( "open.spotify.com/track/2zytp97ugsic1c4knnekyn", normalized1 );
    }

    #endregion
}
