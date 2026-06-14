using System.Formats.Cbor;
using System.Text;

namespace BridgeBeats.Core.Infrastructure.Storage.Car;

/// <summary>
/// Walks the Merkle Search Tree (MST) of an atproto repository to enumerate its records in key
/// order, given the blocks decoded by <see cref="CarV1Reader"/>.
/// </summary>
/// <remarks>
/// The walk starts at the commit block (named by the repo root CID), reads its <c>data</c> link to
/// find the MST root, then traverses the tree in key order. Each MST node holds an optional left
/// subtree link (<c>l</c>) and an ordered list of entries (<c>e</c>); each entry carries a
/// prefix-compressed key (<c>p</c> = the number of leading bytes shared with the previous key per
/// the atproto MST spec; equal to character count for today's ASCII rkeys,
/// <c>k</c> = the differing suffix), a value CID (<c>v</c>), and an optional right subtree link
/// (<c>t</c>). Reconstructed keys take the form <c>{collection}/{rkey}</c>. In-order traversal
/// recurses into <c>l</c>, then for each entry reconstructs the key, yields <c>(key, v)</c>, and
/// recurses into <c>t</c>.
/// The walk is hardened against malicious trees: depth is capped at 64, a visited-set rejects any
/// node seen twice (cycle or DAG doubling), node and record counts are both capped at the block
/// count, the first entry of a node must have <c>p=0</c>, prefix lengths must be non-negative and
/// within the current key, and the supplied <see cref="System.Threading.CancellationToken"/> is honored.
/// </remarks>
internal static class MstWalker {

    /// <summary>
    /// Maximum recursion depth for the tree walk. Exceeding it throws, guarding against a cycle or a
    /// pathologically deep tree.
    /// </summary>
    private const int MaxWalkDepth = 64;

    /// <summary>
    /// Enumerates every record in the repository in MST key order. Returns an empty sequence for an
    /// empty repo.
    /// </summary>
    /// <param name="car">The decoded CAR file whose root names the commit block.</param>
    /// <param name="cancellationToken">A token observed during the (lazily evaluated) traversal.</param>
    /// <returns>
    /// A tuple of the lazy sequence of <c>(Key, ValueCidHex)</c> pairs and the commit
    /// <c>version</c> read from the commit block (or 0 if absent).
    /// </returns>
    /// <exception cref="CarParseException">
    /// Thrown when the commit block is missing, the commit lacks a <c>data</c> link, or any walk
    /// safety bound is breached. Because the sequence is lazy, traversal-time failures surface as the
    /// returned enumerable is iterated.
    /// </exception>
    internal static (IEnumerable<(string Key, string ValueCidHex)> Records, int CommitVersion) EnumerateRecords(
        CarFile car,
        CancellationToken cancellationToken = default
    ) {
        string commitHex = car.Root.KeyHex;
        if (!car.Blocks.TryGetValue( commitHex, out ReadOnlyMemory<byte> commitBytes )) {
            throw new CarParseException( $"Commit block {commitHex} not found in CAR." );
        }

        (string mstRootHex, int commitVersion) = ReadCommitDataLinkAndVersion( commitBytes );

        // Visited-node set: a valid MST is a tree — no node may appear twice.
        // Caps are derived from block count: a valid walk cannot visit more nodes
        // than there are blocks in the file, and cannot yield more records than blocks.
        HashSet<string> visited = new( car.Blocks.Count );
        int maxNodes = car.Blocks.Count;
        int[] recordCount = [0];

        return (WalkNode( car, mstRootHex, "", 0, visited, maxNodes, recordCount, cancellationToken ), commitVersion);
    }

    /// <summary>
    /// Reads the commit block, extracting the MST root link from its <c>data</c> field and the
    /// repository <c>version</c>. Uses
    /// <see cref="System.Formats.Cbor.CborConformanceMode.Lax"/>; canonical DAG-CBOR key ordering
    /// is not enforced by this decoder.
    /// </summary>
    /// <param name="commitBytes">The DAG-CBOR bytes of the commit block.</param>
    /// <returns>A tuple of the MST root node's digest (lowercase hex) and the commit version.</returns>
    /// <exception cref="CarParseException">
    /// Thrown when the <c>data</c> field is present but not a tag-42 CID link, when the <c>data</c>
    /// link is absent or null (no MST root was resolved), or when the commit CBOR is malformed.
    /// </exception>
    private static (string mstRootHex, int commitVersion) ReadCommitDataLinkAndVersion( ReadOnlyMemory<byte> commitBytes ) {
        try {
            CborReader reader = new( commitBytes, CborConformanceMode.Lax );
            _ = reader.ReadStartMap( );

            string? mstRootHexParsed = null;
            int commitVersion = 0;

            while (reader.PeekState( ) != CborReaderState.EndMap) {
                string key = reader.ReadTextString( );
                if (key == "data") {
                    if (reader.PeekState( ) == CborReaderState.Null) {
                        reader.ReadNull( );
                    } else {
                        CborTag tag = reader.ReadTag( );
                        if (tag != (CborTag)42) {
                            throw new CarParseException( $"Commit 'data' field has unexpected tag {(ulong)tag}." );
                        }
                        byte[] linkBytes = reader.ReadByteString( );
                        mstRootHexParsed = Cid.FromDagCborLinkBytes( linkBytes ).KeyHex;
                    }
                } else if (key == "version") {
                    commitVersion = reader.ReadInt32( );
                } else {
                    reader.SkipValue( );
                }
            }

            if (mstRootHexParsed is null) {
                throw new CarParseException( "Commit block missing required 'data' link." );
            }

            return (mstRootHexParsed, commitVersion);
        } catch (CarParseException) {
            throw;
        } catch (Exception ex) when (ex is CborContentException or OverflowException or InvalidOperationException) {
            throw new CarParseException( "Failed to parse commit block CBOR.", ex );
        }
    }

    /// <summary>
    /// Recursively walks one MST node and its subtrees in key order, yielding each entry's
    /// reconstructed key and value CID. Performs an in-order traversal: the left subtree, then for
    /// each entry the entry itself followed by its right subtree.
    /// </summary>
    /// <param name="car">The decoded CAR file used to resolve child node blocks.</param>
    /// <param name="nodeHex">The digest (lowercase hex) of the node block to walk.</param>
    /// <param name="prevKeyInNode">
    /// The most recently yielded key, used as the base for prefix-compression reconstruction of this
    /// node's first entry.
    /// </param>
    /// <param name="depth">The current walk depth, checked against <see cref="MaxWalkDepth"/>.</param>
    /// <param name="visited">The set of node digests already visited, used for cycle detection.</param>
    /// <param name="maxNodes">The cap on visited nodes and yielded records (the repository block count).</param>
    /// <param name="recordCount">A single-element array carrying the running record count across the recursion.</param>
    /// <param name="cancellationToken">A token checked at each node entry.</param>
    /// <returns>The lazy sequence of <c>(Key, ValueCidHex)</c> pairs contributed by this node and its subtrees.</returns>
    /// <exception cref="CarParseException">
    /// Thrown when depth exceeds <see cref="MaxWalkDepth"/>, the node is revisited (a cycle), the
    /// node or record count exceeds <paramref name="maxNodes"/>, the node block is missing, the first
    /// entry's prefix length is not zero, or an entry's prefix length exceeds the current key length.
    /// </exception>
    private static IEnumerable<(string Key, string ValueCidHex)> WalkNode(
        CarFile car,
        string nodeHex,
        string prevKeyInNode,
        int depth,
        HashSet<string> visited,
        int maxNodes,
        int[] recordCount,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested( );

        if (depth > MaxWalkDepth) {
            throw new CarParseException( $"MST walk exceeded maximum depth {MaxWalkDepth}; possible cycle or malformed tree." );
        }

        // Cycle/DAG detection: a valid MST is a tree — each node may be visited at most once.
        if (!visited.Add( nodeHex )) {
            throw new CarParseException(
                $"MST node {nodeHex} encountered more than once; CAR contains a cycle or doubling DAG." );
        }

        // Hard cap: cannot visit more nodes than there are blocks in the file.
        if (visited.Count > maxNodes) {
            throw new CarParseException(
                $"MST walk visited {visited.Count} nodes, exceeding the block-count cap of {maxNodes}." );
        }

        if (!car.Blocks.TryGetValue( nodeHex, out ReadOnlyMemory<byte> nodeBytes )) {
            throw new CarParseException( $"MST node block {nodeHex} not found in CAR." );
        }

        (string? leftHex, List<MstNodeEntry> entries) = ParseMstNode( nodeBytes );

        // Validate first entry in node has p==0 (keys in a node are absolute at entry[0])
        if (entries.Count > 0 && entries[0].PrefixLen != 0) {
            throw new CarParseException(
                $"MST node first entry has p={entries[0].PrefixLen}; must be 0." );
        }

        // Recurse into left subtree first (in-order)
        if (leftHex is not null) {
            foreach ((string key, string valueCidHex) in WalkNode( car, leftHex, prevKeyInNode, depth + 1, visited, maxNodes, recordCount, cancellationToken )) {
                prevKeyInNode = key;
                yield return (key, valueCidHex);
            }
        }

        // Process entries; after left subtree, the current prefix is the last key yielded from left
        string currentPrefix = prevKeyInNode;

        foreach (MstNodeEntry entry in entries) {
            if (entry.PrefixLen > currentPrefix.Length) {
                throw new CarParseException(
                    $"MST entry p={entry.PrefixLen} exceeds current key length {currentPrefix.Length}." );
            }

            string key;
            try {
                key = currentPrefix[..entry.PrefixLen] + entry.KeySuffix;
            } catch (ArgumentOutOfRangeException ex) {
                throw new CarParseException(
                    $"MST entry p={entry.PrefixLen} caused invalid slice on key of length {currentPrefix.Length}.", ex );
            }
            currentPrefix = key;

            // Cap on total records yielded: cannot exceed block count.
            recordCount[0]++;
            if (recordCount[0] > maxNodes) {
                throw new CarParseException(
                    $"MST walk yielded {recordCount[0]} records, exceeding the block-count cap of {maxNodes}." );
            }

            yield return (key, entry.ValueCidHex);

            if (entry.RightChildHex is not null) {
                foreach ((string childKey, string childValueCidHex) in WalkNode( car, entry.RightChildHex, key, depth + 1, visited, maxNodes, recordCount, cancellationToken )) {
                    currentPrefix = childKey;
                    yield return (childKey, childValueCidHex);
                }
            }
        }
    }

    /// <summary>
    /// Decodes a single MST node block into its left-subtree link and its ordered entry list.
    /// Uses <see cref="System.Formats.Cbor.CborConformanceMode.Lax"/>; canonical DAG-CBOR key
    /// ordering is not enforced by this decoder.
    /// </summary>
    /// <param name="nodeBytes">The DAG-CBOR bytes of the MST node.</param>
    /// <returns>
    /// A tuple of the left subtree's node digest (lowercase hex, or <see langword="null"/> when the
    /// <c>l</c> field is null) and the parsed entries (empty when no <c>e</c> field is present).
    /// </returns>
    /// <exception cref="CarParseException">Thrown when a link has an unexpected tag, an entry is malformed, or the node CBOR is invalid.</exception>
    private static (string? leftHex, List<MstNodeEntry> entries) ParseMstNode( ReadOnlyMemory<byte> nodeBytes ) {
        try {
            CborReader reader = new( nodeBytes, CborConformanceMode.Lax );
            _ = reader.ReadStartMap( );

            string? leftHex = null;
            List<MstNodeEntry>? entries = null;

            while (reader.PeekState( ) != CborReaderState.EndMap) {
                string key = reader.ReadTextString( );

                if (key == "l") {
                    leftHex = ReadOptionalCidLink( reader );
                } else if (key == "e") {
                    entries = ReadEntries( reader );
                } else {
                    reader.SkipValue( );
                }
            }

            reader.ReadEndMap( );
            return (leftHex, entries ?? []);
        } catch (CarParseException) {
            throw;
        } catch (Exception ex) when (ex is CborContentException or OverflowException or InvalidOperationException) {
            throw new CarParseException( "Failed to parse MST node CBOR.", ex );
        }
    }

    /// <summary>
    /// Reads an optional subtree or value CID link: a CBOR null, or a tag-42 CID link.
    /// </summary>
    /// <param name="reader">The CBOR reader positioned at a null or a tag.</param>
    /// <returns>The linked node's digest (lowercase hex), or <see langword="null"/> when the value is CBOR null.</returns>
    /// <exception cref="CarParseException">Thrown when a non-null value is not a tag-42 CID link.</exception>
    private static string? ReadOptionalCidLink( CborReader reader ) {
        if (reader.PeekState( ) == CborReaderState.Null) {
            reader.ReadNull( );
            return null;
        }

        CborTag tag = reader.ReadTag( );
        if (tag != (CborTag)42) {
            throw new CarParseException( $"MST link has unexpected tag {(ulong)tag}." );
        }

        byte[] linkBytes = reader.ReadByteString( );
        return Cid.FromDagCborLinkBytes( linkBytes ).KeyHex;
    }

    /// <summary>
    /// Reads the <c>e</c> entry array of an MST node, decoding each entry's prefix length (<c>p</c>),
    /// key suffix (<c>k</c>), value CID link (<c>v</c>), and optional right subtree link (<c>t</c>).
    /// </summary>
    /// <param name="reader">The CBOR reader positioned at the start of the entry array.</param>
    /// <returns>The decoded entries in array order.</returns>
    /// <exception cref="CarParseException">
    /// Thrown when an entry has a negative prefix length, a <c>v</c> link with an unexpected tag, or
    /// is missing its required <c>k</c> (key suffix) or <c>v</c> (value CID) field.
    /// </exception>
    private static List<MstNodeEntry> ReadEntries( CborReader reader ) {
        _ = reader.ReadStartArray( );
        List<MstNodeEntry> entries = [];

        while (reader.PeekState( ) != CborReaderState.EndArray) {
            _ = reader.ReadStartMap( );

            int prefixLen = 0;
            string? keySuffix = null;
            string? valueHex = null;
            string? rightChildHex = null;

            while (reader.PeekState( ) != CborReaderState.EndMap) {
                string fieldKey = reader.ReadTextString( );

                if (fieldKey == "p") {
                    prefixLen = reader.ReadInt32( );
                    // A negative prefix length is structurally invalid and would
                    // cause an ArgumentOutOfRangeException in the slice below if unchecked.
                    if (prefixLen < 0) {
                        throw new CarParseException(
                            $"MST entry has negative prefix length p={prefixLen}; must be >= 0." );
                    }
                } else if (fieldKey == "k") {
                    byte[] keyBytes = reader.ReadByteString( );
                    keySuffix = Encoding.UTF8.GetString( keyBytes );
                } else if (fieldKey == "v") {
                    CborTag tag = reader.ReadTag( );
                    if (tag != (CborTag)42) {
                        throw new CarParseException( $"MST entry 'v' has unexpected tag {(ulong)tag}." );
                    }
                    byte[] linkBytes = reader.ReadByteString( );
                    valueHex = Cid.FromDagCborLinkBytes( linkBytes ).KeyHex;
                } else if (fieldKey == "t") {
                    rightChildHex = ReadOptionalCidLink( reader );
                } else {
                    reader.SkipValue( );
                }
            }

            reader.ReadEndMap( );

            if (keySuffix is null) {
                throw new CarParseException( "MST entry missing 'k' (key suffix) field." );
            }

            if (valueHex is null) {
                throw new CarParseException( "MST entry missing 'v' (value CID) field." );
            }

            entries.Add( new MstNodeEntry( prefixLen, keySuffix, valueHex, rightChildHex ) );
        }

        reader.ReadEndArray( );
        return entries;
    }

    /// <summary>
    /// One decoded MST node entry: the prefix-compressed key parts, the value CID, and the optional
    /// right subtree link.
    /// </summary>
    /// <param name="PrefixLen">
    /// The <c>p</c> field: the number of leading <b>bytes</b> (per the atproto MST spec) this key
    /// shares with the previous key. The reconstruction uses a C# string slice, so character count
    /// and byte count are equal only for ASCII rkeys — the format used for all atproto rkeys today.
    /// A multi-byte UTF-8 rkey character would cause the slice to mis-align; that case does not
    /// arise in practice with the current key alphabet.
    /// The full key is the previous key's first <paramref name="PrefixLen"/> characters followed by
    /// <paramref name="KeySuffix"/>.
    /// </param>
    /// <param name="KeySuffix">The <c>k</c> field: the differing key suffix (UTF-8 decoded).</param>
    /// <param name="ValueCidHex">The <c>v</c> field: the digest (lowercase hex) of the record value block.</param>
    /// <param name="RightChildHex">The <c>t</c> field: the digest of the right subtree node, or <see langword="null"/> if absent.</param>
    private sealed record MstNodeEntry(
        int PrefixLen,
        string KeySuffix,
        string ValueCidHex,
        string? RightChildHex
    );
}
