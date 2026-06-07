using BridgeBeats.Core.Infrastructure.Storage.Car;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests for <see cref="Varint.TryRead"/>.
/// </summary>
[TestClass]
public class VarintTests {

    [TestMethod]
    public void TryRead_SingleByte_Zero_Succeeds( ) {
        bool ok = Varint.TryRead( [0x00], out ulong value, out int bytesRead );

        Assert.IsTrue( ok );
        Assert.AreEqual( 0UL, value );
        Assert.AreEqual( 1, bytesRead );
    }

    [TestMethod]
    public void TryRead_SingleByte_Max_Succeeds( ) {
        // 0x7F = 127 (max single-byte varint)
        bool ok = Varint.TryRead( [0x7F], out ulong value, out int bytesRead );

        Assert.IsTrue( ok );
        Assert.AreEqual( 127UL, value );
        Assert.AreEqual( 1, bytesRead );
    }

    [TestMethod]
    public void TryRead_MultiByteValue_300_Succeeds( ) {
        // 300 = 0xAC 0x02 in LEB128
        bool ok = Varint.TryRead( [0xAC, 0x02], out ulong value, out int bytesRead );

        Assert.IsTrue( ok );
        Assert.AreEqual( 300UL, value );
        Assert.AreEqual( 2, bytesRead );
    }

    [TestMethod]
    public void TryRead_MultiByteValue_128_Succeeds( ) {
        // 128 = 0x80 0x01 in LEB128
        bool ok = Varint.TryRead( [0x80, 0x01], out ulong value, out int bytesRead );

        Assert.IsTrue( ok );
        Assert.AreEqual( 128UL, value );
        Assert.AreEqual( 2, bytesRead );
    }

    [TestMethod]
    public void TryRead_EmptySpan_ReturnsFalse( ) {
        bool ok = Varint.TryRead( ReadOnlySpan<byte>.Empty, out ulong value, out int bytesRead );

        Assert.IsFalse( ok );
        Assert.AreEqual( 0UL, value );
        Assert.AreEqual( 0, bytesRead );
    }

    [TestMethod]
    public void TryRead_TruncatedMultiByte_ReturnsFalse( ) {
        // Continuation bit set but no next byte
        bool ok = Varint.TryRead( [0x80], out ulong value, out int bytesRead );

        Assert.IsFalse( ok );
        Assert.AreEqual( 0UL, value );
        Assert.AreEqual( 0, bytesRead );
    }

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

    [TestMethod]
    public void TryRead_TenByteInput_ReturnsFalseForOversized( ) {
        // 10 bytes with continuation bits set — exceeds 9-byte cap
        byte[] tenBytes = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01];
        bool ok = Varint.TryRead( tenBytes, out ulong value, out int bytesRead );

        // 9th byte (0xFF) has continuation bit set; cap exceeded → returns false
        Assert.IsFalse( ok );
    }

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
