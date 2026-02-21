using BridgeBeats.Contracts.Enums;
using BridgeBeats.Core.Domain.Utilities;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="ProviderUrlParser"/>.
/// </summary>
[TestClass]
public class ProviderUrlParserTests {

    #region Apple Music Tests

    /// <summary>
    /// Verifies that ExtractAppleMusicId extracts ID from standard album URL.
    /// </summary>
    [TestMethod]
    public void ExtractAppleMusicId_ExtractsId_FromAlbumUrl( ) {
        // Arrange
        string url = "https://music.apple.com/us/album/album-name/1234567890";

        // Act
        string? id = ProviderUrlParser.ExtractAppleMusicId( url );

        // Assert
        Assert.AreEqual( "1234567890", id );
    }

    /// <summary>
    /// Verifies that ExtractAppleMusicId extracts ID from standard song URL.
    /// </summary>
    [TestMethod]
    public void ExtractAppleMusicId_ExtractsId_FromSongUrl( ) {
        // Arrange
        string url = "https://music.apple.com/us/song/song-name/1234567890";

        // Act
        string? id = ProviderUrlParser.ExtractAppleMusicId( url );

        // Assert
        Assert.AreEqual( "1234567890", id );
    }

    /// <summary>
    /// Verifies that ExtractAppleMusicId extracts song ID from album URL with ?i= query parameter.
    /// </summary>
    [TestMethod]
    public void ExtractAppleMusicId_ExtractsSongId_FromAlbumUrlWithQueryParameter( ) {
        // Arrange
        string url = "https://music.apple.com/us/album/album-name/9876543210?i=1234567890";

        // Act
        string? id = ProviderUrlParser.ExtractAppleMusicId( url );

        // Assert
        Assert.AreEqual( "1234567890", id, "Should extract song ID from ?i= query parameter" );
    }

    /// <summary>
    /// Verifies that ExtractAppleMusicId handles query parameters in standard URLs.
    /// </summary>
    [TestMethod]
    public void ExtractAppleMusicId_ExtractsId_FromUrlWithQueryParameters( ) {
        // Arrange
        string url = "https://music.apple.com/us/album/album-name/1234567890?app=music&ign-mpt=uo%3D4";

        // Act
        string? id = ProviderUrlParser.ExtractAppleMusicId( url );

        // Assert
        Assert.AreEqual( "1234567890", id );
    }

    /// <summary>
    /// Verifies that ExtractAppleMusicId handles mixed case domain.
    /// </summary>
    [TestMethod]
    public void ExtractAppleMusicId_HandlesCase_MixedCaseDomain( ) {
        // Arrange
        string url = "https://Music.Apple.Com/us/album/album-name/1234567890";

        // Act
        string? id = ProviderUrlParser.ExtractAppleMusicId( url );

        // Assert
        Assert.AreEqual( "1234567890", id );
    }

    /// <summary>
    /// Verifies that ExtractAppleMusicId returns null for invalid URL.
    /// </summary>
    [TestMethod]
    public void ExtractAppleMusicId_ReturnsNull_ForInvalidUrl( ) {
        // Arrange
        string url = "https://example.com/not-apple-music";

        // Act
        string? id = ProviderUrlParser.ExtractAppleMusicId( url );

        // Assert
        Assert.IsNull( id );
    }

    /// <summary>
    /// Verifies that ExtractAppleMusicId returns null for null input.
    /// </summary>
    [TestMethod]
    public void ExtractAppleMusicId_ReturnsNull_ForNullInput( ) {
        // Act
        string? id = ProviderUrlParser.ExtractAppleMusicId( null! );

        // Assert
        Assert.IsNull( id );
    }

    /// <summary>
    /// Verifies that ExtractAppleMusicId returns null for empty input.
    /// </summary>
    [TestMethod]
    public void ExtractAppleMusicId_ReturnsNull_ForEmptyInput( ) {
        // Act
        string? id = ProviderUrlParser.ExtractAppleMusicId( "" );

        // Assert
        Assert.IsNull( id );
    }

    #endregion

    #region Spotify Tests

    /// <summary>
    /// Verifies that ExtractSpotifyId extracts ID from track URL.
    /// </summary>
    [TestMethod]
    public void ExtractSpotifyId_ExtractsId_FromTrackUrl( ) {
        // Arrange
        string url = "https://open.spotify.com/track/3n3Ppam7vgaVa1iaRUc9Lp";

        // Act
        string? id = ProviderUrlParser.ExtractSpotifyId( url );

        // Assert
        Assert.AreEqual( "3n3Ppam7vgaVa1iaRUc9Lp", id );
    }

    /// <summary>
    /// Verifies that ExtractSpotifyId extracts ID from album URL.
    /// </summary>
    [TestMethod]
    public void ExtractSpotifyId_ExtractsId_FromAlbumUrl( ) {
        // Arrange
        string url = "https://open.spotify.com/album/6WdSsBrH5QtofaTTqgwxOV";

        // Act
        string? id = ProviderUrlParser.ExtractSpotifyId( url );

        // Assert
        Assert.AreEqual( "6WdSsBrH5QtofaTTqgwxOV", id );
    }

    /// <summary>
    /// Verifies that ExtractSpotifyId extracts ID from prerelease URL.
    /// </summary>
    [TestMethod]
    public void ExtractSpotifyId_ExtractsId_FromPrereleaseUrl( ) {
        // Arrange
        string url = "https://open.spotify.com/prerelease/1ABC2DEF3GHI4JKL5MNO6P";

        // Act
        string? id = ProviderUrlParser.ExtractSpotifyId( url );

        // Assert
        Assert.AreEqual( "1ABC2DEF3GHI4JKL5MNO6P", id );
    }

    /// <summary>
    /// Verifies that ExtractSpotifyId handles URL with query parameters.
    /// </summary>
    [TestMethod]
    public void ExtractSpotifyId_ExtractsId_FromUrlWithQueryParameters( ) {
        // Arrange
        string url = "https://open.spotify.com/track/3n3Ppam7vgaVa1iaRUc9Lp?si=abc123";

        // Act
        string? id = ProviderUrlParser.ExtractSpotifyId( url );

        // Assert
        Assert.AreEqual( "3n3Ppam7vgaVa1iaRUc9Lp", id );
    }

    /// <summary>
    /// Verifies that ExtractSpotifyId handles case insensitivity.
    /// </summary>
    [TestMethod]
    public void ExtractSpotifyId_HandlesCase_CaseInsensitive( ) {
        // Arrange
        string url = "https://OPEN.SPOTIFY.COM/TRACK/3n3Ppam7vgaVa1iaRUc9Lp";

        // Act
        string? id = ProviderUrlParser.ExtractSpotifyId( url );

        // Assert
        Assert.AreEqual( "3n3Ppam7vgaVa1iaRUc9Lp", id );
    }

    /// <summary>
    /// Verifies that ExtractSpotifyId returns null for invalid URL.
    /// </summary>
    [TestMethod]
    public void ExtractSpotifyId_ReturnsNull_ForInvalidUrl( ) {
        // Arrange
        string url = "https://example.com/not-spotify";

        // Act
        string? id = ProviderUrlParser.ExtractSpotifyId( url );

        // Assert
        Assert.IsNull( id );
    }

    /// <summary>
    /// Verifies that ExtractSpotifyId returns null for null input.
    /// </summary>
    [TestMethod]
    public void ExtractSpotifyId_ReturnsNull_ForNullInput( ) {
        // Act
        string? id = ProviderUrlParser.ExtractSpotifyId( null! );

        // Assert
        Assert.IsNull( id );
    }

    /// <summary>
    /// Verifies that ExtractSpotifyId returns null for empty input.
    /// </summary>
    [TestMethod]
    public void ExtractSpotifyId_ReturnsNull_ForEmptyInput( ) {
        // Act
        string? id = ProviderUrlParser.ExtractSpotifyId( "" );

        // Assert
        Assert.IsNull( id );
    }

    /// <summary>
    /// Verifies that NormalizeUrl normalizes track URL with query parameters.
    /// </summary>
    [TestMethod]
    public void SpotifyNormalizeUrl_RemovesQueryParameters_FromTrackUrl( ) {
        // Arrange
        string url = "https://open.spotify.com/track/2zyTP97uGsIc1C4KNNEkyn?si=MGp6ZDEjQX6yT3uI6ayMDQ";

        // Act
        string normalized = BridgeBeats.Providers.Spotify.SpotifyLinkParser.NormalizeUrl( url );

        // Assert
        Assert.AreEqual( "https://open.spotify.com/track/2zyTP97uGsIc1C4KNNEkyn", normalized );
    }

    /// <summary>
    /// Verifies that NormalizeUrl normalizes album URL with query parameters.
    /// </summary>
    [TestMethod]
    public void SpotifyNormalizeUrl_RemovesQueryParameters_FromAlbumUrl( ) {
        // Arrange
        string url = "https://open.spotify.com/album/6WdSsBrH5QtofaTTqgwxOV?si=abc123xyz";

        // Act
        string normalized = BridgeBeats.Providers.Spotify.SpotifyLinkParser.NormalizeUrl( url );

        // Assert
        Assert.AreEqual( "https://open.spotify.com/album/6WdSsBrH5QtofaTTqgwxOV", normalized );
    }

    /// <summary>
    /// Verifies that NormalizeUrl leaves clean URLs unchanged.
    /// </summary>
    [TestMethod]
    public void SpotifyNormalizeUrl_LeavesCleanUrl_Unchanged( ) {
        // Arrange
        string url = "https://open.spotify.com/track/3n3Ppam7vgaVa1iaRUc9Lp";

        // Act
        string normalized = BridgeBeats.Providers.Spotify.SpotifyLinkParser.NormalizeUrl( url );

        // Assert
        Assert.AreEqual( "https://open.spotify.com/track/3n3Ppam7vgaVa1iaRUc9Lp", normalized );
    }

    /// <summary>
    /// Verifies that NormalizeUrl handles prerelease URLs.
    /// </summary>
    [TestMethod]
    public void SpotifyNormalizeUrl_HandlesPrerelease_WithQueryParameters( ) {
        // Arrange
        string url = "https://open.spotify.com/prerelease/1ABC2DEF3GHI4JKL5MNO6P?si=test123";

        // Act
        string normalized = BridgeBeats.Providers.Spotify.SpotifyLinkParser.NormalizeUrl( url );

        // Assert
        Assert.AreEqual( "https://open.spotify.com/prerelease/1ABC2DEF3GHI4JKL5MNO6P", normalized );
    }

    /// <summary>
    /// Verifies that NormalizeUrl returns empty string for null input.
    /// </summary>
    [TestMethod]
    public void SpotifyNormalizeUrl_ReturnsEmpty_ForNullInput( ) {
        // Act
        string normalized = BridgeBeats.Providers.Spotify.SpotifyLinkParser.NormalizeUrl( null );

        // Assert
        Assert.AreEqual( string.Empty, normalized );
    }

    /// <summary>
    /// Verifies that NormalizeUrl returns empty string for empty input.
    /// </summary>
    [TestMethod]
    public void SpotifyNormalizeUrl_ReturnsEmpty_ForEmptyInput( ) {
        // Act
        string normalized = BridgeBeats.Providers.Spotify.SpotifyLinkParser.NormalizeUrl( "" );

        // Assert
        Assert.AreEqual( string.Empty, normalized );
    }

    /// <summary>
    /// Verifies that NormalizeUrl returns original URL for non-Spotify URL.
    /// </summary>
    [TestMethod]
    public void SpotifyNormalizeUrl_ReturnsOriginal_ForNonSpotifyUrl( ) {
        // Arrange
        string url = "https://example.com/not-spotify";

        // Act
        string normalized = BridgeBeats.Providers.Spotify.SpotifyLinkParser.NormalizeUrl( url );

        // Assert
        Assert.AreEqual( url, normalized );
    }

    #endregion

    #region Tidal Tests

    /// <summary>
    /// Verifies that ExtractTidalId extracts ID from track URL.
    /// </summary>
    [TestMethod]
    public void ExtractTidalId_ExtractsId_FromTrackUrl( ) {
        // Arrange
        string url = "https://tidal.com/browse/track/123456789";

        // Act
        string? id = ProviderUrlParser.ExtractTidalId( url );

        // Assert
        Assert.AreEqual( "123456789", id );
    }

    /// <summary>
    /// Verifies that ExtractTidalId extracts ID from album URL.
    /// </summary>
    [TestMethod]
    public void ExtractTidalId_ExtractsId_FromAlbumUrl( ) {
        // Arrange
        string url = "https://tidal.com/browse/album/987654321";

        // Act
        string? id = ProviderUrlParser.ExtractTidalId( url );

        // Assert
        Assert.AreEqual( "987654321", id );
    }

    /// <summary>
    /// Verifies that ExtractTidalId extracts ID from listen.tidal.com URL.
    /// </summary>
    [TestMethod]
    public void ExtractTidalId_ExtractsId_FromListenSubdomain( ) {
        // Arrange
        string url = "https://listen.tidal.com/track/123456789";

        // Act
        string? id = ProviderUrlParser.ExtractTidalId( url );

        // Assert
        Assert.AreEqual( "123456789", id );
    }

    /// <summary>
    /// Verifies that ExtractTidalId extracts ID from URL without /browse/.
    /// </summary>
    [TestMethod]
    public void ExtractTidalId_ExtractsId_FromUrlWithoutBrowse( ) {
        // Arrange
        string url = "https://tidal.com/track/123456789";

        // Act
        string? id = ProviderUrlParser.ExtractTidalId( url );

        // Assert
        Assert.AreEqual( "123456789", id );
    }

    /// <summary>
    /// Verifies that ExtractTidalId returns null for artist URL (not supported).
    /// </summary>
    [TestMethod]
    public void ExtractTidalId_ReturnsNull_ForArtistUrl( ) {
        // Arrange
        string url = "https://tidal.com/browse/artist/123456";

        // Act
        string? id = ProviderUrlParser.ExtractTidalId( url );

        // Assert
        Assert.IsNull( id, "Artist URLs should not return an ID" );
    }

    /// <summary>
    /// Verifies that ExtractTidalId handles case insensitivity.
    /// </summary>
    [TestMethod]
    public void ExtractTidalId_HandlesCase_CaseInsensitive( ) {
        // Arrange
        string url = "https://TIDAL.COM/BROWSE/TRACK/123456789";

        // Act
        string? id = ProviderUrlParser.ExtractTidalId( url );

        // Assert
        Assert.AreEqual( "123456789", id );
    }

    /// <summary>
    /// Verifies that ExtractTidalId returns null for invalid URL.
    /// </summary>
    [TestMethod]
    public void ExtractTidalId_ReturnsNull_ForInvalidUrl( ) {
        // Arrange
        string url = "https://example.com/not-tidal";

        // Act
        string? id = ProviderUrlParser.ExtractTidalId( url );

        // Assert
        Assert.IsNull( id );
    }

    /// <summary>
    /// Verifies that ExtractTidalId returns null for null input.
    /// </summary>
    [TestMethod]
    public void ExtractTidalId_ReturnsNull_ForNullInput( ) {
        // Act
        string? id = ProviderUrlParser.ExtractTidalId( null! );

        // Assert
        Assert.IsNull( id );
    }

    /// <summary>
    /// Verifies that ExtractTidalId returns null for empty input.
    /// </summary>
    [TestMethod]
    public void ExtractTidalId_ReturnsNull_ForEmptyInput( ) {
        // Act
        string? id = ProviderUrlParser.ExtractTidalId( "" );

        // Assert
        Assert.IsNull( id );
    }

    #endregion

    #region ExtractId (Provider Switch) Tests

    /// <summary>
    /// Verifies that ExtractId routes to correct provider parser for Apple Music.
    /// </summary>
    [TestMethod]
    public void ExtractId_RoutesToAppleMusicParser_ForAppleMusicProvider( ) {
        // Arrange
        string url = "https://music.apple.com/us/album/album-name/1234567890";

        // Act
        string? id = ProviderUrlParser.ExtractId( SupportedProviders.AppleMusic, url );

        // Assert
        Assert.AreEqual( "1234567890", id );
    }

    /// <summary>
    /// Verifies that ExtractId routes to correct provider parser for Spotify.
    /// </summary>
    [TestMethod]
    public void ExtractId_RoutesToSpotifyParser_ForSpotifyProvider( ) {
        // Arrange
        string url = "https://open.spotify.com/track/3n3Ppam7vgaVa1iaRUc9Lp";

        // Act
        string? id = ProviderUrlParser.ExtractId( SupportedProviders.Spotify, url );

        // Assert
        Assert.AreEqual( "3n3Ppam7vgaVa1iaRUc9Lp", id );
    }

    /// <summary>
    /// Verifies that ExtractId routes to correct provider parser for Tidal.
    /// </summary>
    [TestMethod]
    public void ExtractId_RoutesToTidalParser_ForTidalProvider( ) {
        // Arrange
        string url = "https://tidal.com/browse/track/123456789";

        // Act
        string? id = ProviderUrlParser.ExtractId( SupportedProviders.Tidal, url );

        // Assert
        Assert.AreEqual( "123456789", id );
    }

    /// <summary>
    /// Verifies that ExtractId returns null for unsupported provider.
    /// </summary>
    [TestMethod]
    public void ExtractId_ReturnsNull_ForUnsupportedProvider( ) {
        // Arrange
        string url = "https://example.com/track/123";

        // Act
        string? id = ProviderUrlParser.ExtractId( (SupportedProviders)999, url );

        // Assert
        Assert.IsNull( id );
    }

    /// <summary>
    /// Verifies that ExtractId returns null for null URL.
    /// </summary>
    [TestMethod]
    public void ExtractId_ReturnsNull_ForNullUrl( ) {
        // Act
        string? id = ProviderUrlParser.ExtractId( SupportedProviders.Spotify, null! );

        // Assert
        Assert.IsNull( id );
    }

    /// <summary>
    /// Verifies that ExtractId returns null for empty URL.
    /// </summary>
    [TestMethod]
    public void ExtractId_ReturnsNull_ForEmptyUrl( ) {
        // Act
        string? id = ProviderUrlParser.ExtractId( SupportedProviders.Spotify, "" );

        // Assert
        Assert.IsNull( id );
    }

    #endregion
}
