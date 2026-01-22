namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Service for generating QR codes.
/// </summary>
public interface IQrCodeService {

    /// <summary>
    /// Generates a QR code as a base64 data URI for the specified URL.
    /// </summary>
    /// <param name="url">The URL to encode in the QR code.</param>
    /// <param name="pixelsPerModule">The size of each QR module in pixels. Higher values create larger images.</param>
    /// <returns>A base64-encoded PNG data URI suitable for use in an img src attribute.</returns>
    string GenerateQrCodeDataUri( string url, int pixelsPerModule = 10 );
}
