using BridgeBeats.Contracts.Interfaces;
using QRCoder;

namespace BridgeBeats.Core.Domain.Services;

/// <summary>
/// Renders a URL into a QR code encoded as a PNG <c>data:</c> URI. Stateless; holds no
/// configuration and can be reused across requests.
/// </summary>
public class QrCodeService : IQrCodeService {

    /// <summary>
    /// Generates a QR code for the given URL and returns it as a base64-encoded PNG
    /// <c>data:</c> URI, suitable for embedding directly in an <c>img</c> tag.
    /// </summary>
    /// <param name="url">The URL to encode. Must not be null, empty, or whitespace.</param>
    /// <param name="pixelsPerModule">
    /// The pixel size of each QR module (the smallest square). Larger values produce a larger
    /// image. Defaults to <c>10</c>.
    /// </param>
    /// <returns>A <c>data:image/png;base64,...</c> URI containing the rendered QR code.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="url"/> is null, empty, or whitespace.</exception>
    public string GenerateQrCodeDataUri( string url, int pixelsPerModule = 10 ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( url );

        using QRCodeGenerator qrGenerator = new( );
        using QRCodeData qrCodeData = qrGenerator.CreateQrCode( url, QRCodeGenerator.ECCLevel.M );
        using PngByteQRCode qrCode = new( qrCodeData );

        byte[] pngBytes = qrCode.GetGraphic( pixelsPerModule );
        string base64 = Convert.ToBase64String( pngBytes );

        return $"data:image/png;base64,{base64}";
    }
}
