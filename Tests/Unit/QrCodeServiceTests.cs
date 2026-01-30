using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Domain.Services;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="QrCodeService"/>.
/// </summary>
[TestClass]
public class QrCodeServiceTests {

    private IQrCodeService _service = null!;

    /// <summary>
    /// Initializes the test fixture.
    /// </summary>
    [TestInitialize]
    public void TestInitialize( ) {
        _service = new QrCodeService( );
    }

    /// <summary>
    /// Verifies that GenerateQrCodeDataUri returns a valid data URI for a valid URL.
    /// </summary>
    [TestMethod]
    public void GenerateQrCodeDataUri_WithValidUrl_ReturnsDataUri( ) {
        // Arrange
        string url = "https://example.com/card/12345";

        // Act
        string result = _service.GenerateQrCodeDataUri( url );

        // Assert
        Assert.StartsWith( "data:image/png;base64,", result );
        Assert.IsGreaterThan( 100, result.Length, "Base64 PNG should be substantial" );
    }

    /// <summary>
    /// Verifies that different URLs produce different QR codes.
    /// </summary>
    [TestMethod]
    public void GenerateQrCodeDataUri_WithDifferentUrls_ReturnsDifferentQrCodes( ) {
        // Arrange
        string url1 = "https://example.com/card/12345";
        string url2 = "https://example.com/card/67890";

        // Act
        string result1 = _service.GenerateQrCodeDataUri( url1 );
        string result2 = _service.GenerateQrCodeDataUri( url2 );

        // Assert
        Assert.AreNotEqual( result1, result2 );
    }

    /// <summary>
    /// Verifies that larger pixelsPerModule produces larger images.
    /// </summary>
    [TestMethod]
    public void GenerateQrCodeDataUri_WithCustomPixelsPerModule_ReturnsLargerImage( ) {
        // Arrange
        string url = "https://example.com/card/12345";

        // Act
        string smallResult = _service.GenerateQrCodeDataUri( url, pixelsPerModule: 5 );
        string largeResult = _service.GenerateQrCodeDataUri( url, pixelsPerModule: 20 );

        // Assert
        // Larger pixels per module should produce a larger base64 string (larger image)
        Assert.IsGreaterThan( smallResult.Length, largeResult.Length, "Larger pixelsPerModule should produce larger image" );
    }

    /// <summary>
    /// Verifies that null URL throws ArgumentNullException.
    /// </summary>
    [TestMethod]
    public void GenerateQrCodeDataUri_WithNullUrl_ThrowsArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) => _service.GenerateQrCodeDataUri( null! ) );
    }

    /// <summary>
    /// Verifies that empty URL throws ArgumentException.
    /// </summary>
    [TestMethod]
    public void GenerateQrCodeDataUri_WithEmptyUrl_ThrowsArgumentException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentException>( ( ) => _service.GenerateQrCodeDataUri( string.Empty ) );
    }

    /// <summary>
    /// Verifies that whitespace URL throws ArgumentException.
    /// </summary>
    [TestMethod]
    public void GenerateQrCodeDataUri_WithWhitespaceUrl_ThrowsArgumentException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentException>( ( ) => _service.GenerateQrCodeDataUri( "   " ) );
    }

    /// <summary>
    /// Verifies that the returned data URI contains valid PNG data.
    /// </summary>
    [TestMethod]
    public void GenerateQrCodeDataUri_ReturnsValidBase64PngData( ) {
        // Arrange
        string url = "https://example.com/test";

        // Act
        string result = _service.GenerateQrCodeDataUri( url );

        // Assert
        string base64Part = result.Replace( "data:image/png;base64,", string.Empty );
        byte[] bytes = Convert.FromBase64String( base64Part );

        // PNG files start with specific magic bytes: 137 80 78 71 13 10 26 10
        Assert.IsGreaterThan( 8, bytes.Length, "PNG file should have more than 8 bytes" );
        Assert.AreEqual( (byte)137, bytes[0], "PNG signature byte 1" );
        Assert.AreEqual( (byte)80, bytes[1], "PNG signature byte 2 ('P')" );
        Assert.AreEqual( (byte)78, bytes[2], "PNG signature byte 3 ('N')" );
        Assert.AreEqual( (byte)71, bytes[3], "PNG signature byte 4 ('G')" );
    }
}
