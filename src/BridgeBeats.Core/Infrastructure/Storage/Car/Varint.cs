namespace BridgeBeats.Core.Infrastructure.Storage.Car;

/// <summary>
/// Decodes LEB128 unsigned variable-length integers (varints) as used by the CARv1
/// container format for its header length and per-section length prefixes.
/// </summary>
/// <remarks>
/// A varint stores an unsigned integer little-endian in groups of seven bits per byte:
/// the low seven bits of each byte carry data and the high bit (<c>0x80</c>) is a
/// continuation flag that is set on every byte except the last. Decoding is hard-capped
/// at nine bytes, the maximum needed to represent a 64-bit value, so a truncated or
/// maliciously over-long varint cannot run past the end of the buffer or overflow.
/// </remarks>
internal static class Varint {

    /// <summary>
    /// Maximum number of bytes a single varint may occupy. Nine seven-bit groups cover the
    /// full 64-bit range; reading stops here so malformed input cannot run unbounded.
    /// </summary>
    private const int MaxVarintBytes = 9;

    /// <summary>
    /// Attempts to decode a single LEB128 unsigned varint from the start of <paramref name="source"/>.
    /// Caps at nine bytes per the CARv1 spec.
    /// </summary>
    /// <param name="source">The bytes to read from; decoding starts at index zero.</param>
    /// <param name="value">
    /// On success, the decoded unsigned value. Reset to zero when decoding fails.
    /// </param>
    /// <param name="bytesRead">
    /// On success, the number of bytes the varint occupied. Reset to zero when decoding fails.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if a complete varint was decoded within the nine-byte cap;
    /// <see langword="false"/> if the input was truncated (a continuation bit was set with no
    /// further bytes) or exceeded the cap. No exception is thrown on malformed input.
    /// </returns>
    internal static bool TryRead( ReadOnlySpan<byte> source, out ulong value, out int bytesRead ) {
        value = 0;
        bytesRead = 0;

        for (int i = 0; i < MaxVarintBytes && i < source.Length; i++) {
            byte b = source[i];
            value |= (ulong)(b & 0x7F) << (7 * i);
            bytesRead++;

            if ((b & 0x80) == 0) {
                return true;
            }
        }

        // Either source was too short or exceeded 9-byte cap
        value = 0;
        bytesRead = 0;
        return false;
    }
}
