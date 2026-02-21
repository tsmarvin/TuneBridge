using BridgeBeats.Providers.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for the <see cref="LinkNormalizer"/> class.
/// </summary>
[TestClass]
public class LinkNormalizerTests {

    [TestMethod]
    public void NormalizeUrl_WithSpotifyTrackingParameter_RemovesParameter( ) {
        // Arrange
        string urlWithTracking = "https://open.spotify.com/track/2zyTP97uGsIc1C4KNNEkyn?si=MGp6ZDEjQX6yT3uI6ayMDQ";
        string expectedUrl = "https://open.spotify.com/track/2zyTP97uGsIc1C4KNNEkyn";

        // Act
        string normalizedUrl = LinkNormalizer.NormalizeUrl( urlWithTracking );

        // Assert
        Assert.AreEqual( expectedUrl, normalizedUrl );
    }

    [TestMethod]
    public void NormalizeUrl_WithoutQueryParameters_ReturnsUnchanged( ) {
        // Arrange
        string url = "https://open.spotify.com/track/2zyTP97uGsIc1C4KNNEkyn";

        // Act
        string normalizedUrl = LinkNormalizer.NormalizeUrl( url );

        // Assert
        Assert.AreEqual( url, normalizedUrl );
    }

    [TestMethod]
    public void NormalizeUrl_WithMultipleQueryParameters_RemovesAllParameters( ) {
        // Arrange
        string urlWithParams = "https://open.spotify.com/track/ABC123?si=xyz&utm_source=test&foo=bar";
        string expectedUrl = "https://open.spotify.com/track/ABC123";

        // Act
        string normalizedUrl = LinkNormalizer.NormalizeUrl( urlWithParams );

        // Assert
        Assert.AreEqual( expectedUrl, normalizedUrl );
    }

    [TestMethod]
    public void NormalizeUrl_WithFragment_RemovesFragment( ) {
        // Arrange
        string urlWithFragment = "https://open.spotify.com/track/ABC123#section";
        string expectedUrl = "https://open.spotify.com/track/ABC123";

        // Act
        string normalizedUrl = LinkNormalizer.NormalizeUrl( urlWithFragment );

        // Assert
        Assert.AreEqual( expectedUrl, normalizedUrl );
    }

    [TestMethod]
    public void NormalizeUrl_WithQueryParameterAndFragment_RemovesBoth( ) {
        // Arrange
        string urlWithBoth = "https://open.spotify.com/track/ABC123?si=xyz#section";
        string expectedUrl = "https://open.spotify.com/track/ABC123";

        // Act
        string normalizedUrl = LinkNormalizer.NormalizeUrl( urlWithBoth );

        // Assert
        Assert.AreEqual( expectedUrl, normalizedUrl );
    }

    [TestMethod]
    public void NormalizeUrl_WithAppleMusicUrl_RemovesQueryParameters( ) {
        // Arrange
        string urlWithParams = "https://music.apple.com/us/album/test/123456?i=789&utm_source=test";
        string expectedUrl = "https://music.apple.com/us/album/test/123456";

        // Act
        string normalizedUrl = LinkNormalizer.NormalizeUrl( urlWithParams );

        // Assert
        Assert.AreEqual( expectedUrl, normalizedUrl );
    }

    [TestMethod]
    public void NormalizeUrl_WithNullOrEmpty_ReturnsEmpty( ) {
        // Arrange & Act
        string resultNull = LinkNormalizer.NormalizeUrl( null! );
        string resultEmpty = LinkNormalizer.NormalizeUrl( string.Empty );
        string resultWhitespace = LinkNormalizer.NormalizeUrl( "   " );

        // Assert
        Assert.AreEqual( string.Empty, resultNull );
        Assert.AreEqual( string.Empty, resultEmpty );
        Assert.AreEqual( string.Empty, resultWhitespace );
    }
}
