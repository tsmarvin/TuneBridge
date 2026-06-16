namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Generates QR codes for URLs as inline data URIs.
/// </summary>
/// <remarks>
/// Implemented in <c>BridgeBeats.Core</c> by <c>QrCodeService</c>
/// (<c>Domain/Services/QrCodeService.cs</c>).
/// </remarks>
public interface IQrCodeService {

    /// <summary>
    /// Renders a QR code for the given URL and returns it as a base64-encoded PNG data URI,
    /// suitable for use in an <c>img</c> <c>src</c> attribute.
    /// </summary>
    /// <param name="url">The URL to encode in the QR code.</param>
    /// <param name="pixelsPerModule">The size, in pixels, of each QR module (the smallest square); larger values produce a larger image. Defaults to 10.</param>
    /// <returns>A <c>data:</c> URI containing the rendered PNG QR code.</returns>
    string GenerateQrCodeDataUri( string url, int pixelsPerModule = 10 );
}
