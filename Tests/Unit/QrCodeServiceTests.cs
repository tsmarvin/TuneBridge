using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Domain.Services;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="QrCodeService"/> (the <see cref="IQrCodeService"/> implementation).
/// Verify that <c>GenerateQrCodeDataUri</c> renders a URL to a base64 PNG <c>data:</c> URI,
/// that distinct URLs and larger <c>pixelsPerModule</c> values produce distinct/larger images,
/// that the output carries a valid PNG signature, and that null/empty/whitespace input is rejected.
/// </summary>
[TestClass]
public class QrCodeServiceTests {

    /// <summary>The service under test, created fresh before each test.</summary>
    private IQrCodeService _service = null!;

    /// <summary>Creates a fresh <see cref="QrCodeService"/> before each test.</summary>
    [TestInitialize]
    public void TestInitialize( ) {
        _service = new QrCodeService( );
    }

    /// <summary>
    /// A valid URL produces a substantial base64 PNG <c>data:image/png;base64,</c> URI.
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

    /// <summary>Two different URLs encode to two different QR-code data URIs.</summary>
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
    /// A larger <c>pixelsPerModule</c> renders a larger image, so the base64 payload for the same
    /// URL grows with the module size.
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

    /// <summary>A <c>null</c> URL throws <see cref="ArgumentNullException"/>.</summary>
    [TestMethod]
    public void GenerateQrCodeDataUri_WithNullUrl_ThrowsArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) => _service.GenerateQrCodeDataUri( null! ) );
    }

    /// <summary>An empty URL throws <see cref="ArgumentException"/>.</summary>
    [TestMethod]
    public void GenerateQrCodeDataUri_WithEmptyUrl_ThrowsArgumentException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentException>( ( ) => _service.GenerateQrCodeDataUri( string.Empty ) );
    }

    /// <summary>A whitespace-only URL throws <see cref="ArgumentException"/>.</summary>
    [TestMethod]
    public void GenerateQrCodeDataUri_WithWhitespaceUrl_ThrowsArgumentException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentException>( ( ) => _service.GenerateQrCodeDataUri( "   " ) );
    }

    /// <summary>
    /// The data URI's base64 payload decodes to bytes whose first four match the PNG signature
    /// (137, 'P', 'N', 'G'), confirming a real PNG is emitted.
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
