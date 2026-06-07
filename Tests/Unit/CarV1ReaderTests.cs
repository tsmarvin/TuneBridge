using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Storage.Car;
using BridgeBeats.Tests.Unit.Helpers;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests for <see cref="CarV1Reader.Read"/>.
/// </summary>
[TestClass]
public class CarV1ReaderTests {

    private static readonly MediaLinkResultRecord s_testRecord = new(
        results: [
            new ProviderResultRecord(
                provider: "spotify",
                artist: "Test Artist",
                title: "Test Track",
                url: "https://open.spotify.com/track/abc",
                marketRegion: "US"
            )
        ],
        lookedUpAt: new DateTimeOffset( 2024, 1, 1, 0, 0, 0, TimeSpan.Zero )
    );

    [TestMethod]
    public void Read_ValidCar_ReturnsCarFile( ) {
        byte[] carBytes = TestCarBuilder.BuildRepoCarWithRecords(
            "did:plc:test123",
            "link.bridgebeats.lookup",
            [("rkey1", s_testRecord)]
        );

        CarFile carFile = CarV1Reader.Read( carBytes );

        Assert.IsNotNull( carFile );
        Assert.IsNotEmpty( carFile.Blocks );
    }

    /// <summary>
    /// An empty repo emits exactly 2 blocks: the commit block and the empty MST root node.
    /// The test name "ZeroBlocks" was a misnomer; this test pins the empty-repo block shape.
    /// </summary>
    [TestMethod]
    public void Read_ValidCar_EmptyRepo_HasCommitAndMstRoot_Succeeds( ) {
        // Empty repo: no record blocks, just an MST root node and commit
        byte[] carBytes = TestCarBuilder.BuildRepoCarWithRecords(
            "did:plc:emptyrepo",
            "link.bridgebeats.lookup",
            []
        );

        CarFile carFile = CarV1Reader.Read( carBytes );

        Assert.IsNotNull( carFile );
        // Empty repo = commit block + empty MST node = exactly 2 blocks
        Assert.HasCount( 2, carFile.Blocks );
    }

    [TestMethod]
    public void Read_DigestMismatch_ThrowsCarParseException( ) {
        byte[] carBytes = TestCarBuilder.BuildRepoCarWithRecords(
            "did:plc:test123",
            "link.bridgebeats.lookup",
            [("rkey1", s_testRecord)]
        );

        // Corrupt a byte in the block data area (past the header)
        // Find a position well into the file to corrupt a block payload
        byte[] corrupted = (byte[])carBytes.Clone( );
        int corruptPos = corrupted.Length - 10;
        corrupted[corruptPos] ^= 0xFF;

        _ = Assert.ThrowsExactly<CarParseException>( ( ) => CarV1Reader.Read( corrupted ) );
    }

    [TestMethod]
    public void Read_TruncatedSection_ThrowsCarParseException( ) {
        byte[] carBytes = TestCarBuilder.BuildRepoCarWithRecords(
            "did:plc:test123",
            "link.bridgebeats.lookup",
            [("rkey1", s_testRecord)]
        );

        // Truncate to half
        byte[] truncated = carBytes[..(carBytes.Length / 2)];

        _ = Assert.ThrowsExactly<CarParseException>( ( ) => CarV1Reader.Read( truncated ) );
    }

    [TestMethod]
    public void Read_Version2Header_ThrowsCarParseException( ) {
        // Build a fake CAR header with version=2
        byte[] fakeCarBytes = BuildCarWithVersion( 2 );

        _ = Assert.ThrowsExactly<CarParseException>( ( ) => CarV1Reader.Read( fakeCarBytes ) );
    }

    [TestMethod]
    public void Read_CidV0InSections_ThrowsCarParseException( ) {
        // Build a valid CAR but inject a CIDv0 prefix into a section
        byte[] carBytes = TestCarBuilder.BuildRepoCarWithRecords(
            "did:plc:test123",
            "link.bridgebeats.lookup",
            [("rkey1", s_testRecord)]
        );

        // Find the first section (after the header) and patch the first byte to 0x12 (CIDv0)
        // We need to find where sections start. Parse the header length first.
        byte[] patched = (byte[])carBytes.Clone( );
        int offset = 0;
        _ = Varint.TryRead( patched.AsSpan( ), out ulong headerLen, out int headerLenBytes );
        offset += headerLenBytes + (int)headerLen;

        // Now at the first section. Read the section length varint.
        _ = Varint.TryRead( patched.AsSpan( offset ), out ulong _, out int sectionLenBytes );
        offset += sectionLenBytes;

        // patch first byte of CID to 0x12 (CIDv0 marker)
        patched[offset] = 0x12;

        _ = Assert.ThrowsExactly<CarParseException>( ( ) => CarV1Reader.Read( patched ) );
    }

    // ─── Duplicate CID: last-write-wins semantics ─────────────────────────────

    /// <summary>
    /// Verifies that a CAR containing a block section emitted twice for the same CID
    /// succeeds, with the dictionary retaining exactly one entry (last-write-wins).
    /// Content-addressed blocks are immutable, so both writes have identical content.
    /// </summary>
    [TestMethod]
    public void Read_DuplicateCidSection_LastWriteWins( ) {
        byte[] carBytes = TestCarBuilder.BuildCarWithDuplicateCid( out string duplicatedCidHex );

        // Should not throw — duplicate CIDs are legal in CAR v1
        CarFile carFile = CarV1Reader.Read( carBytes );

        Assert.IsNotNull( carFile );
        // The duplicated block must appear exactly once in the dictionary
        Assert.IsTrue( carFile.Blocks.ContainsKey( duplicatedCidHex ) );
    }

    // ─── >2MB block section ───────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a block section declaring a length > 2 MB is rejected with CarParseException.
    /// </summary>
    [TestMethod]
    public void Read_OversizedBlockSection_ThrowsCarParseException( ) {
        byte[] carBytes = TestCarBuilder.BuildCarWithOversizedBlockSection( );

        _ = Assert.ThrowsExactly<CarParseException>( ( ) => CarV1Reader.Read( carBytes ) );
    }

    private static byte[] BuildCarWithVersion( int version ) {
        using System.IO.MemoryStream ms = new( );

        // Build header with specified version
        System.Formats.Cbor.CborWriter w = new( System.Formats.Cbor.CborConformanceMode.Lax );
        w.WriteStartMap( null );
        w.WriteTextString( "roots" );
        w.WriteStartArray( null );
        w.WriteEndArray( );
        w.WriteTextString( "version" );
        w.WriteInt32( version );
        w.WriteEndMap( );
        byte[] header = w.Encode( );

        TestCarBuilder.WriteVarint( ms, (ulong)header.Length );
        ms.Write( header );
        return ms.ToArray( );
    }
}

#pragma warning restore CS1591
