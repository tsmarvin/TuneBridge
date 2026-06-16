using BridgeBeats.Core.Infrastructure.Storage.Car;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for the unsigned LEB128 varint decoder used when parsing CAR archive headers and
/// block-length prefixes. Covers single-byte and multi-byte values, empty and truncated input,
/// the 9-byte maximum that fits a <see cref="ulong"/>, oversized (10-byte) rejection, and the
/// invariant that decoding consumes only the bytes belonging to the value and reports an accurate
/// <c>bytesRead</c> count.
/// </summary>
[TestClass]
public class VarintTests {

    /// <summary>
    /// Verifies that a single zero byte decodes to the value 0 with a one-byte read.
    /// </summary>
    [TestMethod]
    public void TryRead_SingleByte_Zero_Succeeds( ) {
        bool ok = Varint.TryRead( [0x00], out ulong value, out int bytesRead );

        Assert.IsTrue( ok );
        Assert.AreEqual( 0UL, value );
        Assert.AreEqual( 1, bytesRead );
    }

    /// <summary>
    /// Verifies that the largest single-byte payload (<c>0x7F</c>, high bit clear) decodes to 127
    /// with a one-byte read, confirming the upper bound before a continuation byte is required.
    /// </summary>
    [TestMethod]
    public void TryRead_SingleByte_Max_Succeeds( ) {
        // 0x7F = 127 (max single-byte varint)
        bool ok = Varint.TryRead( [0x7F], out ulong value, out int bytesRead );

        Assert.IsTrue( ok );
        Assert.AreEqual( 127UL, value );
        Assert.AreEqual( 1, bytesRead );
    }

    /// <summary>
    /// Verifies that the two-byte encoding <c>0xAC 0x02</c> decodes to 300, confirming the
    /// little-endian, 7-bits-per-byte continuation scheme across a byte boundary.
    /// </summary>
    [TestMethod]
    public void TryRead_MultiByteValue_300_Succeeds( ) {
        // 300 = 0xAC 0x02 in LEB128
        bool ok = Varint.TryRead( [0xAC, 0x02], out ulong value, out int bytesRead );

        Assert.IsTrue( ok );
        Assert.AreEqual( 300UL, value );
        Assert.AreEqual( 2, bytesRead );
    }

    /// <summary>
    /// Verifies that the two-byte encoding <c>0x80 0x01</c> decodes to 128, the smallest value that
    /// requires a continuation byte (one past the single-byte ceiling of 127).
    /// </summary>
    [TestMethod]
    public void TryRead_MultiByteValue_128_Succeeds( ) {
        // 128 = 0x80 0x01 in LEB128
        bool ok = Varint.TryRead( [0x80, 0x01], out ulong value, out int bytesRead );

        Assert.IsTrue( ok );
        Assert.AreEqual( 128UL, value );
        Assert.AreEqual( 2, bytesRead );
    }

    /// <summary>
    /// Verifies that decoding an empty span fails (returns false) and yields a zero value and a
    /// zero <c>bytesRead</c>, with no out-of-bounds read.
    /// </summary>
    [TestMethod]
    public void TryRead_EmptySpan_ReturnsFalse( ) {
        bool ok = Varint.TryRead( ReadOnlySpan<byte>.Empty, out ulong value, out int bytesRead );

        Assert.IsFalse( ok );
        Assert.AreEqual( 0UL, value );
        Assert.AreEqual( 0, bytesRead );
    }

    /// <summary>
    /// Verifies that a continuation byte with no successor (<c>0x80</c> alone, high bit set but the
    /// span ending) fails rather than reading past the buffer, reporting false with zero outputs.
    /// </summary>
    [TestMethod]
    public void TryRead_TruncatedMultiByte_ReturnsFalse( ) {
        // Continuation bit set but no next byte
        bool ok = Varint.TryRead( [0x80], out ulong value, out int bytesRead );

        Assert.IsFalse( ok );
        Assert.AreEqual( 0UL, value );
        Assert.AreEqual( 0, bytesRead );
    }

    /// <summary>
    /// Verifies that a nine-byte varint (the maximum width that still fits a 64-bit unsigned value)
    /// decodes successfully, reporting nine bytes read and a non-zero value.
    /// </summary>
    [TestMethod]
    public void TryRead_NineByteMaxValue_Succeeds( ) {
        // 9 bytes of max LEB128 (encodes 0x7FFFFFFFFFFFFFFF at 9 bytes)
        // Build a valid 9-byte varint
        byte[] nineBytes = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F];
        bool ok = Varint.TryRead( nineBytes, out ulong value, out int bytesRead );

        Assert.IsTrue( ok );
        Assert.AreEqual( 9, bytesRead );
        Assert.IsGreaterThan( 0UL, value );
    }

    /// <summary>
    /// Verifies that a ten-byte continuation sequence is rejected as oversized: a value wider than
    /// nine bytes cannot fit a <see cref="ulong"/>, so decoding fails rather than overflowing.
    /// </summary>
    [TestMethod]
    public void TryRead_TenByteInput_ReturnsFalseForOversized( ) {
        // 10 bytes with continuation bits set — exceeds 9-byte cap
        byte[] tenBytes = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01];
        bool ok = Varint.TryRead( tenBytes, out ulong value, out int bytesRead );

        // 9th byte (0xFF) has continuation bit set; cap exceeded → returns false
        Assert.IsFalse( ok );
    }

    /// <summary>
    /// Verifies that decoding stops at the first byte whose high bit is clear: given
    /// <c>0x05 0xFF 0xFF</c>, only the leading <c>0x05</c> is consumed, yielding value 5 and a
    /// one-byte read while leaving the trailing bytes untouched.
    /// </summary>
    [TestMethod]
    public void TryRead_ReadsOnlyNeededBytes( ) {
        // Varint followed by extra data — only the varint bytes should be consumed
        bool ok = Varint.TryRead( [0x05, 0xFF, 0xFF], out ulong value, out int bytesRead );

        Assert.IsTrue( ok );
        Assert.AreEqual( 5UL, value );
        Assert.AreEqual( 1, bytesRead );
    }
}

#pragma warning restore CS1591
