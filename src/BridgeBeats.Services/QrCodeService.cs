using BridgeBeats.Contracts.Interfaces;
using QRCoder;

namespace BridgeBeats.Services;

/// <summary>
/// Service for generating QR codes using QRCoder library.
/// </summary>
public class QrCodeService : IQrCodeService {

    /// <inheritdoc />
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
