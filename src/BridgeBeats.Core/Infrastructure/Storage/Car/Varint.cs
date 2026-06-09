namespace BridgeBeats.Core.Infrastructure.Storage.Car;

internal static class Varint {

    private const int MaxVarintBytes = 9;

    /// <summary>
    /// Attempts to read an unsigned LEB-128 varint from a span.
    /// Caps at 9 bytes per the CAR v1 spec.
    /// </summary>
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
