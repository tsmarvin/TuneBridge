using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Storage.Car;
using BridgeBeats.Tests.Unit.Helpers;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests <see cref="CarV1Reader.Read"/>, the hand-written CARv1 parser that reads an ATProto repo
/// export into a block map, with strong defenses against malformed or malicious input.
/// </summary>
/// <remarks>
/// The tests cover the happy path (a valid CAR yields a <see cref="CarFile"/> with blocks), the
/// minimal empty repository (commit + MST-root blocks), and the parser's rejection rules, each
/// surfacing as a <see cref="CarParseException"/>: a per-block digest mismatch, a truncated section,
/// a CAR v2 (or non-1 version) header, a CIDv0 in a section, an oversized block section, an oversized
/// CID, and a header missing the version field. A duplicate-CID section is accepted with last-write
/// semantics. Fixtures are built with the test helper <c>TestCarBuilder</c>.
/// </remarks>
[TestClass]
public class CarV1ReaderTests {

    /// <summary>Shared persisted lookup record used as block content across the fixtures.</summary>
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

    /// <summary>
    /// Verifies that reading a valid CAR returns a non-null <see cref="CarFile"/> with a non-empty block
    /// map.
    /// </summary>
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
    /// Verifies that an empty repository CAR still parses, containing exactly the two structural blocks
    /// (the commit and the MST root).
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

    /// <summary>
    /// Verifies that corrupting a block's bytes so its SHA-256 digest no longer matches its CID throws
    /// <see cref="CarParseException"/>, confirming the content-addressing integrity check.
    /// </summary>
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

    /// <summary>
    /// Verifies that a CAR truncated mid-stream throws <see cref="CarParseException"/>, confirming the
    /// parser bounds-checks every slice rather than reading past the buffer.
    /// </summary>
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

    /// <summary>
    /// Verifies that a CAR whose header declares version 2 throws <see cref="CarParseException"/>, since
    /// only CARv1 is supported.
    /// </summary>
    [TestMethod]
    public void Read_Version2Header_ThrowsCarParseException( ) {
        // Build a fake CAR header with version=2
        byte[] fakeCarBytes = BuildCarWithVersion( 2 );

        _ = Assert.ThrowsExactly<CarParseException>( ( ) => CarV1Reader.Read( fakeCarBytes ) );
    }

    /// <summary>
    /// Verifies that patching a section's CID prefix to a CIDv0 form throws
    /// <see cref="CarParseException"/>, confirming only CIDv1 (dag-cbor, sha2-256) CIDs are accepted.
    /// The fixture uses <see cref="Varint.TryRead"/> to skip the header and section-length varints to
    /// reach the CID byte.
    /// </summary>
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
    /// Verifies that a CAR containing two sections with the same CID parses successfully and the block
    /// map retains an entry for the duplicated CID (last write wins), rather than rejecting the repeat.
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
    /// Verifies that a section whose declared block size exceeds the parser's per-block cap throws
    /// <see cref="CarParseException"/>, confirming the anti-DoS size limit.
    /// </summary>
    [TestMethod]
    public void Read_OversizedBlockSection_ThrowsCarParseException( ) {
        byte[] carBytes = TestCarBuilder.BuildCarWithOversizedBlockSection( );

        _ = Assert.ThrowsExactly<CarParseException>( ( ) => CarV1Reader.Read( carBytes ) );
    }

    // ─── Hardening: oversized CID rejected ───────────────────────────────────

    /// <summary>
    /// Verifies that a CID encoded with an oversized length in the header throws
    /// <see cref="CarParseException"/>, confirming CID lengths are bounds-checked.
    /// </summary>
    [TestMethod]
    public void Read_OversizedCidBytes_ThrowsCarParseException( ) {
        byte[] carBytes = TestCarBuilder.BuildCarWithOversizedCidInHeader( );

        _ = Assert.ThrowsExactly<CarParseException>( ( ) => CarV1Reader.Read( carBytes ) );
    }

    // ─── Hardening: missing version field rejected ────────────────────────────

    /// <summary>
    /// Verifies that a header lacking the required <c>version</c> field throws
    /// <see cref="CarParseException"/>, since the parser cannot confirm it is reading a CARv1.
    /// </summary>
    [TestMethod]
    public void Read_HeaderMissingVersion_ThrowsCarParseException( ) {
        byte[] carBytes = TestCarBuilder.BuildCarWithHeaderMissingVersion( );

        _ = Assert.ThrowsExactly<CarParseException>( ( ) => CarV1Reader.Read( carBytes ) );
    }

    /// <summary>
    /// Builds a minimal CAR whose CBOR header declares the supplied version (with an empty roots array),
    /// for the version-rejection test.
    /// </summary>
    /// <param name="version">The CAR version to encode in the header.</param>
    /// <returns>The encoded CAR bytes.</returns>
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
