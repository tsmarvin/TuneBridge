using System.Formats.Cbor;
using System.Security.Cryptography;
using BridgeBeats.Contracts.Records;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Unit.Helpers;

/// <summary>
/// Builds synthetic CAR v1 files for unit testing without requiring binary fixtures.
/// DAG-CBOR key ordering follows the deterministic spec: length-first, then lexicographic.
/// All CIDs are real sha256 digests over the corresponding block bytes.
/// </summary>
internal static class TestCarBuilder {

    // CIDv1 dag-cbor sha256 prefix: version=0x01, codec=0x71, hash-fn=0x12, hash-len=0x20
    private static readonly byte[] s_cidPrefix = [0x01, 0x71, 0x12, 0x20];

    // ─── Public record type for MST node entries ─────────────────────────────

    internal sealed record MstEntry(
        int PrefixLen,
        string KeySuffix,
        byte[] ValueCidBytes,
        byte[]? RightChildCidBytes = null
    );

    // ─── CID helpers ─────────────────────────────────────────────────────────

    internal static byte[] ComputeCidBytes( ReadOnlySpan<byte> blockBytes ) {
        byte[] digest = SHA256.HashData( blockBytes );
        byte[] cidBytes = new byte[4 + 32];
        s_cidPrefix.CopyTo( cidBytes, 0 );
        digest.CopyTo( cidBytes, 4 );
        return cidBytes;
    }

    /// <summary>
    /// Wraps CID bytes in the tag-42 DAG-CBOR link encoding used in MST node entries.
    /// Format: byte string of [0x00, ...cidBytes]
    /// </summary>
    internal static byte[] MakeLinkBytes( byte[] cidBytes ) {
        byte[] linkBytes = new byte[1 + cidBytes.Length];
        linkBytes[0] = 0x00; // multibase prefix
        cidBytes.CopyTo( linkBytes, 1 );
        return linkBytes;
    }

    // ─── Varint encoding ─────────────────────────────────────────────────────

    internal static void WriteVarint( MemoryStream stream, ulong value ) {
        while (value >= 0x80) {
            stream.WriteByte( (byte)((value & 0x7F) | 0x80) );
            value >>= 7;
        }
        stream.WriteByte( (byte)value );
    }

    // ─── Block builders ──────────────────────────────────────────────────────

    /// <summary>
    /// Serializes a <see cref="MediaLinkResultRecord"/> to DAG-CBOR bytes.
    /// </summary>
    internal static byte[] BuildRecordBlock( MediaLinkResultRecord record ) {
        CborWriter writer = new( CborConformanceMode.Lax );
        writer.WriteStartMap( null );

        // DAG-CBOR canonical key order: length-first, then lexicographic.
        // Key lengths: results(7) < isPartial(9) < lookedUpAt(10)
        // Write results first, then isPartial (only when true), then lookedUpAt.

        WriteCborTextString( writer, "results" );
        writer.WriteStartArray( null );
        foreach (ProviderResultRecord r in record.Results) {
            BuildProviderResultBlock( writer, r );
        }
        writer.WriteEndArray( );

        if (record.IsPartial) {
            WriteCborTextString( writer, "isPartial" );
            writer.WriteBoolean( true );
        }

        WriteCborTextString( writer, "lookedUpAt" );
        WriteCborTextString( writer, record.LookedUpAt.ToString( "O" ) );

        writer.WriteEndMap( );
        return writer.Encode( );
    }

    private static void BuildProviderResultBlock( CborWriter writer, ProviderResultRecord r ) {
        writer.WriteStartMap( null );

        // Write fields in DAG-CBOR key order: length-first, then lexicographic within the same length.
        // Key lengths: url(3), title(5), artUrl(6), artist(6), isAlbum(7), provider(8), externalId(10), marketRegion(12)
        // Sorted order: url(3) < title(5) < artUrl(6)=artist(6)[lex] < isAlbum(7) < provider(8) < externalId(10) < marketRegion(12)

        WriteCborTextString( writer, "url" );
        WriteCborTextString( writer, r.Url );

        WriteCborTextString( writer, "title" );
        WriteCborTextString( writer, r.Title );

        if (r.ArtUrl is not null) {
            // artUrl (6) before artist (6) — lexicographic tie-break: 'artUrl' < 'artist'
            WriteCborTextString( writer, "artUrl" );
            WriteCborTextString( writer, r.ArtUrl );
        }

        WriteCborTextString( writer, "artist" );
        WriteCborTextString( writer, r.Artist );

        if (r.IsAlbum.HasValue) {
            WriteCborTextString( writer, "isAlbum" );
            writer.WriteBoolean( r.IsAlbum.Value );
        }

        WriteCborTextString( writer, "provider" );
        WriteCborTextString( writer, r.Provider );

        if (r.ExternalId is not null) {
            WriteCborTextString( writer, "externalId" );
            WriteCborTextString( writer, r.ExternalId );
        }

        WriteCborTextString( writer, "marketRegion" );
        WriteCborTextString( writer, r.MarketRegion );

        writer.WriteEndMap( );
    }

    /// <summary>
    /// Builds an MST node block with optional left subtree and a list of entries.
    /// </summary>
    internal static byte[] BuildMstNode( byte[]? leftCidBytes, IReadOnlyList<MstEntry> entries ) {
        CborWriter writer = new( CborConformanceMode.Lax );
        writer.WriteStartMap( null );

        // e field (entries array) — key "e" length 1
        WriteCborTextString( writer, "e" );
        writer.WriteStartArray( null );
        foreach (MstEntry entry in entries) {
            writer.WriteStartMap( null );
            // k (1), p (1), t (1), v (1) — all length 1, alphabetic: k < p < t < v
            WriteCborTextString( writer, "k" );
            writer.WriteByteString( System.Text.Encoding.UTF8.GetBytes( entry.KeySuffix ) );

            WriteCborTextString( writer, "p" );
            writer.WriteInt32( entry.PrefixLen );

            WriteCborTextString( writer, "t" );
            if (entry.RightChildCidBytes is not null) {
                writer.WriteTag( (CborTag)42 );
                writer.WriteByteString( MakeLinkBytes( entry.RightChildCidBytes ) );
            } else {
                writer.WriteNull( );
            }

            WriteCborTextString( writer, "v" );
            writer.WriteTag( (CborTag)42 );
            writer.WriteByteString( MakeLinkBytes( entry.ValueCidBytes ) );

            writer.WriteEndMap( );
        }
        writer.WriteEndArray( );

        // l field — key "l" length 1 (comes after "e" alphabetically)
        WriteCborTextString( writer, "l" );
        if (leftCidBytes is not null) {
            writer.WriteTag( (CborTag)42 );
            writer.WriteByteString( MakeLinkBytes( leftCidBytes ) );
        } else {
            writer.WriteNull( );
        }

        writer.WriteEndMap( );
        return writer.Encode( );
    }

    /// <summary>
    /// Builds a commit block pointing to the MST root.
    /// </summary>
    /// <param name="did">The DID of the repo owner.</param>
    /// <param name="mstRootCidBytes">CID bytes for the MST root node.</param>
    /// <param name="version">Commit version field value (default 3).</param>
    internal static byte[] BuildCommit( string did, byte[] mstRootCidBytes, int version = 3 ) {
        CborWriter writer = new( CborConformanceMode.Lax );
        writer.WriteStartMap( null );

        // Keys by length: rev(3), did(3), sig(3), data(4), prev(4), version(7)
        // rev (3), did (3), sig (3) — alphabetical: did < rev < sig
        // data (4), prev (4) — alphabetical: data < prev
        // version (7)

        WriteCborTextString( writer, "did" );
        WriteCborTextString( writer, did );

        WriteCborTextString( writer, "rev" );
        WriteCborTextString( writer, "3" );

        WriteCborTextString( writer, "sig" );
        writer.WriteByteString( new byte[64] ); // placeholder signature

        WriteCborTextString( writer, "data" );
        writer.WriteTag( (CborTag)42 );
        writer.WriteByteString( MakeLinkBytes( mstRootCidBytes ) );

        WriteCborTextString( writer, "prev" );
        writer.WriteNull( );

        WriteCborTextString( writer, "version" );
        writer.WriteInt32( version );

        writer.WriteEndMap( );
        return writer.Encode( );
    }

    // ─── Security test helpers ────────────────────────────────────────────────

    /// <summary>
    /// Builds a CAR where a single MST node is referenced twice: once as the left child (l)
    /// of a parent node and once as the right child (t) of an entry in that same parent node.
    /// This is a "doubling DAG" pattern that a valid MST tree prohibits.
    /// Without cycle detection, a chained variant causes 2^N traversals.
    /// </summary>
    internal static byte[] BuildDoublingDagCar( ) {
        byte[] recBytes = BuildRecordBlock( new BridgeBeats.Contracts.Records.MediaLinkResultRecord(
            results: [new BridgeBeats.Contracts.Records.ProviderResultRecord(
                "spotify", "Artist", "Track",
                "https://open.spotify.com/track/x", "US" )],
            lookedUpAt: new DateTimeOffset( 2024, 1, 1, 0, 0, 0, TimeSpan.Zero )
        ) );
        byte[] recCid = ComputeCidBytes( recBytes );

        // nodeA: standalone leaf — will be referenced twice by nodeB
        byte[] nodeABytes = BuildMstNode( null,
            [new MstEntry( 0, "link.bridgebeats.lookup/a", recCid )] );
        byte[] nodeACid = ComputeCidBytes( nodeABytes );

        // nodeB: l=nodeA, entry[0].t=nodeA  ← nodeA referenced TWICE
        byte[] nodeBBytes = BuildMstNode( nodeACid,
            [new MstEntry( 0, "link.bridgebeats.lookup/b", recCid, nodeACid )] );
        byte[] nodeBCid = ComputeCidBytes( nodeBBytes );

        byte[] commitBytes = BuildCommit( "did:plc:test", nodeBCid );
        byte[] commitCid = ComputeCidBytes( commitBytes );

        Dictionary<byte[], byte[]> blocks = new( ByteArrayComparer.Instance ) {
            [commitCid] = commitBytes,
            [nodeBCid] = nodeBBytes,
            [nodeACid] = nodeABytes,
            [recCid] = recBytes,
        };
        return BuildCar( commitCid, blocks );
    }

    /// <summary>
    /// Builds a raw DAG-CBOR byte sequence of <paramref name="nestingDepth"/> nested single-element
    /// arrays, terminated by an empty array. Each level costs exactly 1 byte (0x81 = array of 1).
    /// A depth of 33 exceeds the DagCborConverter.MaxDepth of 32, triggering CarParseException.
    /// </summary>
    internal static byte[] BuildDeeplyNestedCborBytes( int nestingDepth ) {
        using MemoryStream ms = new( );
        // Write nestingDepth levels of 0x81 (array of 1 item), then 0x80 (empty array)
        for (int i = 0; i < nestingDepth; i++) {
            ms.WriteByte( 0x81 ); // array(1)
        }
        ms.WriteByte( 0x80 ); // array(0) — innermost
        return ms.ToArray( );
    }

    /// <summary>
    /// Builds a CAR containing an MST entry whose <c>p</c> (prefix-len) field is set to
    /// <paramref name="negativePrefixLen"/> (a negative integer). The second entry's negative
    /// p value should be rejected by MstWalker as a CarParseException.
    /// </summary>
    internal static byte[] BuildCarWithNegativePrefixLen( int negativePrefixLen = -1 ) {
        byte[] recBytes = BuildRecordBlock( new BridgeBeats.Contracts.Records.MediaLinkResultRecord(
            results: [new BridgeBeats.Contracts.Records.ProviderResultRecord(
                "spotify", "Artist", "Track",
                "https://open.spotify.com/track/x", "US" )],
            lookedUpAt: new DateTimeOffset( 2024, 1, 1, 0, 0, 0, TimeSpan.Zero )
        ) );
        byte[] recCid = ComputeCidBytes( recBytes );

        // Build the node bytes manually so we can write a negative p value.
        // First entry: p=0, k="link.bridgebeats.lookup/rkey1", valid
        // Second entry: p=negativePrefixLen (e.g. -1) — structurally invalid
        CborWriter writer = new( CborConformanceMode.Lax );
        writer.WriteStartMap( null );

        WriteCborTextString( writer, "e" );
        writer.WriteStartArray( null );

        // Entry 0: valid
        writer.WriteStartMap( null );
        WriteCborTextString( writer, "k" );
        writer.WriteByteString( System.Text.Encoding.UTF8.GetBytes( "link.bridgebeats.lookup/rkey1" ) );
        WriteCborTextString( writer, "p" );
        writer.WriteInt32( 0 );
        WriteCborTextString( writer, "t" );
        writer.WriteNull( );
        WriteCborTextString( writer, "v" );
        writer.WriteTag( (CborTag)42 );
        writer.WriteByteString( MakeLinkBytes( recCid ) );
        writer.WriteEndMap( );

        // Entry 1: negative p
        writer.WriteStartMap( null );
        WriteCborTextString( writer, "k" );
        writer.WriteByteString( System.Text.Encoding.UTF8.GetBytes( "suffix" ) );
        WriteCborTextString( writer, "p" );
        writer.WriteInt32( negativePrefixLen );
        WriteCborTextString( writer, "t" );
        writer.WriteNull( );
        WriteCborTextString( writer, "v" );
        writer.WriteTag( (CborTag)42 );
        writer.WriteByteString( MakeLinkBytes( recCid ) );
        writer.WriteEndMap( );

        writer.WriteEndArray( );

        WriteCborTextString( writer, "l" );
        writer.WriteNull( );

        writer.WriteEndMap( );

        byte[] mstNodeBytes = writer.Encode( );
        byte[] mstCid = ComputeCidBytes( mstNodeBytes );
        byte[] commitBytes = BuildCommit( "did:plc:test", mstCid );
        byte[] commitCid = ComputeCidBytes( commitBytes );

        Dictionary<byte[], byte[]> blocks = new( ByteArrayComparer.Instance ) {
            [commitCid] = commitBytes,
            [mstCid] = mstNodeBytes,
            [recCid] = recBytes,
        };
        return BuildCar( commitCid, blocks );
    }

    /// <summary>
    /// Builds a CAR whose first block section has a declared length larger than 2 MB,
    /// which should be rejected by CarV1Reader before any block content is read.
    /// </summary>
    internal static byte[] BuildCarWithOversizedBlockSection( ) {
        using MemoryStream ms = new( );

        // Build a minimal valid header pointing to a fake commit CID
        byte[] fakeCid = ComputeCidBytes( new byte[1] { 0xFF } );
        byte[] header = BuildCarHeaderPublic( fakeCid );
        WriteVarint( ms, (ulong)header.Length );
        ms.Write( header );

        // Write one section whose declared length is 3 MB (> MaxBlockSize 2 MB)
        // section_len = CID_bytes(36) + block_bytes
        ulong oversizedLen = (ulong)(36 + 3 * 1024 * 1024);
        WriteVarint( ms, oversizedLen );
        // Write the CID bytes (real CID for the over-limit block)
        ms.Write( fakeCid );
        // Don't write the block bytes — the parser should throw before reading them
        // because blockLen > MaxBlockSize is checked before reading block content.

        return ms.ToArray( );
    }

    /// <summary>
    /// Builds a CAR with a duplicate CID section: the same block appears twice in the stream.
    /// Per last-write-wins semantics, the dictionary should contain exactly one entry per CID.
    /// </summary>
    internal static byte[] BuildCarWithDuplicateCid( out string duplicatedCidHex ) {
        byte[] blockBytes = System.Text.Encoding.UTF8.GetBytes( "duplicate-block-content" );
        byte[] cid = ComputeCidBytes( blockBytes );
        duplicatedCidHex = Convert.ToHexStringLower( cid.AsSpan( 4 ) ); // KeyHex uses digest bytes only

        byte[] mstNodeBytes = BuildMstNode( null, [] );
        byte[] mstCid = ComputeCidBytes( mstNodeBytes );
        byte[] commitBytes = BuildCommit( "did:plc:test", mstCid );
        byte[] commitCid = ComputeCidBytes( commitBytes );

        // We build the CAR manually to emit the duplicate-content block twice
        using MemoryStream ms = new( );
        byte[] header = BuildCarHeaderPublic( commitCid );
        WriteVarint( ms, (ulong)header.Length );
        ms.Write( header );

        // Emit blockBytes block TWICE with the same CID
        foreach (byte[] _ in new[] { blockBytes, blockBytes }) {
            ulong sectionLen = (ulong)(cid.Length + blockBytes.Length);
            WriteVarint( ms, sectionLen );
            ms.Write( cid );
            ms.Write( blockBytes );
        }

        // Emit commit and MST node normally
        void WriteBlock( byte[] c, byte[] b ) {
            ulong sl = (ulong)(c.Length + b.Length);
            WriteVarint( ms, sl );
            ms.Write( c );
            ms.Write( b );
        }

        WriteBlock( mstCid, mstNodeBytes );
        WriteBlock( commitCid, commitBytes );

        return ms.ToArray( );
    }

    /// <summary>
    /// Builds a CAR header byte array — public surface used by security test helpers.
    /// </summary>
    internal static byte[] BuildCarHeaderPublic( byte[] rootCidBytes ) =>
        BuildCarHeader( rootCidBytes );

    /// <summary>
    /// Builds a complete CAR v1 file from a dictionary of blocks and a root CID.
    /// </summary>
    /// <param name="rootCidBytes">CID bytes for the root (commit) block.</param>
    /// <param name="blocks">All blocks keyed by their CID bytes.</param>
    internal static byte[] BuildCar( byte[] rootCidBytes, Dictionary<byte[], byte[]> blocks ) {
        using MemoryStream stream = new( );

        // Build DAG-CBOR header: {version: 1, roots: [rootCid]}
        byte[] header = BuildCarHeader( rootCidBytes );

        // Write varint(header_len) | header
        WriteVarint( stream, (ulong)header.Length );
        stream.Write( header );

        // Write sections: varint(cid_len + block_len) | cid_bytes | block_bytes
        foreach (KeyValuePair<byte[], byte[]> kvp in blocks) {
            byte[] cidBytes = kvp.Key;
            byte[] blockBytes = kvp.Value;
            ulong sectionLen = (ulong)(cidBytes.Length + blockBytes.Length);
            WriteVarint( stream, sectionLen );
            stream.Write( cidBytes );
            stream.Write( blockBytes );
        }

        return stream.ToArray( );
    }

    private static byte[] BuildCarHeader( byte[] rootCidBytes ) {
        CborWriter writer = new( CborConformanceMode.Lax );
        writer.WriteStartMap( null );

        // roots (5) before version (7)
        WriteCborTextString( writer, "roots" );
        writer.WriteStartArray( null );
        writer.WriteTag( (CborTag)42 );
        writer.WriteByteString( MakeLinkBytes( rootCidBytes ) );
        writer.WriteEndArray( );

        WriteCborTextString( writer, "version" );
        writer.WriteInt32( 1 );

        writer.WriteEndMap( );
        return writer.Encode( );
    }

    private static void WriteCborTextString( CborWriter writer, string value ) {
        writer.WriteTextString( value );
    }

    // ─── High-level factory: single-collection repo ───────────────────────────

    /// <summary>
    /// Builds a minimal CAR representing a repo with the given lookup records.
    /// Each record gets a deterministic rkey based on its index.
    /// </summary>
    internal static byte[] BuildRepoCarWithRecords(
        string did,
        string collection,
        IReadOnlyList<(string Rkey, MediaLinkResultRecord Record)> records
    ) {
        Dictionary<byte[], byte[]> blocks = new( ByteArrayComparer.Instance );

        // Build a record block for each entry
        List<(string Key, byte[] CidBytes)> mstEntries = [];
        foreach ((string rkey, MediaLinkResultRecord record) in records) {
            byte[] recordBytes = BuildRecordBlock( record );
            byte[] cidBytes = ComputeCidBytes( recordBytes );
            blocks[cidBytes] = recordBytes;
            mstEntries.Add( ($"{collection}/{rkey}", cidBytes) );
        }

        // Sort entries by key (MST is lexicographically ordered)
        mstEntries.Sort( static ( a, b ) => string.Compare( a.Key, b.Key, StringComparison.Ordinal ) );

        // Build MST entries with prefix compression
        List<MstEntry> entries = [];
        string prevKey = "";
        foreach ((string key, byte[] cidBytes) in mstEntries) {
            int prefixLen = CommonPrefixLength( prevKey, key );
            string suffix = key[prefixLen..];
            entries.Add( new MstEntry( prefixLen, suffix, cidBytes ) );
            prevKey = key;
        }

        // Build single MST node
        byte[] mstNodeBytes = BuildMstNode( null, entries );
        byte[] mstCidBytes = ComputeCidBytes( mstNodeBytes );
        blocks[mstCidBytes] = mstNodeBytes;

        // Build commit
        byte[] commitBytes = BuildCommit( did, mstCidBytes );
        byte[] commitCidBytes = ComputeCidBytes( commitBytes );
        blocks[commitCidBytes] = commitBytes;

        return BuildCar( commitCidBytes, blocks );
    }

    /// <summary>
    /// Builds a minimal CAR representing a repo with the given lookup records and a specified commit version.
    /// Useful for testing warn-and-proceed on non-3 commit versions.
    /// </summary>
    internal static byte[] BuildRepoCarWithRecordsAndVersion(
        string did,
        string collection,
        IReadOnlyList<(string Rkey, MediaLinkResultRecord Record)> records,
        int commitVersion
    ) {
        Dictionary<byte[], byte[]> blocks = new( ByteArrayComparer.Instance );

        List<(string Key, byte[] CidBytes)> mstEntries = [];
        foreach ((string rkey, MediaLinkResultRecord record) in records) {
            byte[] recordBytes = BuildRecordBlock( record );
            byte[] cidBytes = ComputeCidBytes( recordBytes );
            blocks[cidBytes] = recordBytes;
            mstEntries.Add( ($"{collection}/{rkey}", cidBytes) );
        }

        mstEntries.Sort( static ( a, b ) => string.Compare( a.Key, b.Key, StringComparison.Ordinal ) );

        List<MstEntry> entries = [];
        string prevKey = "";
        foreach ((string key, byte[] cidBytes) in mstEntries) {
            int prefixLen = CommonPrefixLength( prevKey, key );
            string suffix = key[prefixLen..];
            entries.Add( new MstEntry( prefixLen, suffix, cidBytes ) );
            prevKey = key;
        }

        byte[] mstNodeBytes = BuildMstNode( null, entries );
        byte[] mstCidBytes = ComputeCidBytes( mstNodeBytes );
        blocks[mstCidBytes] = mstNodeBytes;

        byte[] commitBytes = BuildCommit( did, mstCidBytes, commitVersion );
        byte[] commitCidBytes = ComputeCidBytes( commitBytes );
        blocks[commitCidBytes] = commitBytes;

        return BuildCar( commitCidBytes, blocks );
    }

    private static int CommonPrefixLength( string a, string b ) {
        int len = Math.Min( a.Length, b.Length );
        int i = 0;
        while (i < len && a[i] == b[i]) {
            i++;
        }
        return i;
    }

    // ─── Malformed-input helpers (hardening tests) ────────────────────────────

    /// <summary>
    /// Builds a CAR header whose "roots" array contains a CID byte string that is one byte longer
    /// than the expected 36 bytes (37 bytes total). Used to verify that ParseFromBytes rejects
    /// oversized CID byte sequences rather than silently truncating them.
    /// </summary>
    internal static byte[] BuildCarWithOversizedCidInHeader( ) {
        byte[] normalCid = ComputeCidBytes( new byte[] { 0xAA } );
        // Produce a 37-byte link: 0x00 multibase prefix + 36-byte CID + 1 extra trailing byte
        byte[] oversizedLinkBytes = new byte[1 + normalCid.Length + 1];
        oversizedLinkBytes[0] = 0x00; // multibase prefix
        normalCid.CopyTo( oversizedLinkBytes, 1 );
        oversizedLinkBytes[^1] = 0xFF; // extra trailing byte

        CborWriter writer = new( CborConformanceMode.Lax );
        writer.WriteStartMap( null );

        WriteCborTextString( writer, "roots" );
        writer.WriteStartArray( null );
        writer.WriteTag( (CborTag)42 );
        writer.WriteByteString( oversizedLinkBytes );
        writer.WriteEndArray( );

        WriteCborTextString( writer, "version" );
        writer.WriteInt32( 1 );

        writer.WriteEndMap( );
        byte[] header = writer.Encode( );

        using MemoryStream ms = new( );
        WriteVarint( ms, (ulong)header.Length );
        ms.Write( header );
        return ms.ToArray( );
    }

    /// <summary>
    /// Builds a CAR whose header DAG-CBOR map contains only "roots" — the "version" key is absent.
    /// Used to verify that ParseRootCidFromRawHeader rejects headers missing the required version field.
    /// </summary>
    internal static byte[] BuildCarWithHeaderMissingVersion( ) {
        byte[] fakeCid = ComputeCidBytes( new byte[] { 0xBB } );

        CborWriter writer = new( CborConformanceMode.Lax );
        writer.WriteStartMap( null );

        WriteCborTextString( writer, "roots" );
        writer.WriteStartArray( null );
        writer.WriteTag( (CborTag)42 );
        writer.WriteByteString( MakeLinkBytes( fakeCid ) );
        writer.WriteEndArray( );

        // "version" key intentionally omitted

        writer.WriteEndMap( );
        byte[] header = writer.Encode( );

        using MemoryStream ms = new( );
        WriteVarint( ms, (ulong)header.Length );
        ms.Write( header );
        return ms.ToArray( );
    }

    /// <summary>
    /// Builds a commit block where the "data" field is explicitly set to CBOR null.
    /// Used to verify that ReadCommitDataLinkAndVersion rejects null/absent data links.
    /// </summary>
    internal static byte[] BuildCommitWithNullData( string did, int version = 3 ) {
        CborWriter writer = new( CborConformanceMode.Lax );
        writer.WriteStartMap( null );

        // Same key ordering as BuildCommit: did(3) < rev(3) < sig(3) < data(4) < prev(4) < version(7)
        WriteCborTextString( writer, "did" );
        WriteCborTextString( writer, did );

        WriteCborTextString( writer, "rev" );
        WriteCborTextString( writer, "3" );

        WriteCborTextString( writer, "sig" );
        writer.WriteByteString( new byte[64] );

        WriteCborTextString( writer, "data" );
        writer.WriteNull( ); // explicitly null — no data link

        WriteCborTextString( writer, "prev" );
        writer.WriteNull( );

        WriteCborTextString( writer, "version" );
        writer.WriteInt32( version );

        writer.WriteEndMap( );
        return writer.Encode( );
    }

    /// <summary>
    /// Comparer for byte array dictionary keys.
    /// </summary>
    private sealed class ByteArrayComparer : IEqualityComparer<byte[]> {
        internal static readonly ByteArrayComparer Instance = new( );
        public bool Equals( byte[]? x, byte[]? y ) => x is not null && y is not null && x.AsSpan( ).SequenceEqual( y );
        public int GetHashCode( byte[] obj ) {
            HashCode hc = new( );
            hc.AddBytes( obj );
            return hc.ToHashCode( );
        }
    }
}

#pragma warning restore CS1591
