using System.Formats.Cbor;
using System.Text;

namespace BridgeBeats.Core.Infrastructure.Storage.Car;

/// <summary>
/// Walks an atproto Merkle Search Tree (MST) and enumerates all records in key order.
/// </summary>
/// <remarks>
/// MST node structure: {l: CID|null, e: [{p:int, k:bytes, v:CID, t:CID|null}]}
/// In-order walk: recurse l → per entry: reconstruct key, yield (key, v), recurse t.
/// Keys are "{collection}/{rkey}"; prefix compression via the p (prefix-len) field.
/// </remarks>
internal static class MstWalker {

    private const int MaxWalkDepth = 64;

    /// <summary>
    /// Enumerates all (key, valueCidHex) pairs in the MST rooted at the commit in <paramref name="car"/>.
    /// Returns an empty sequence for an empty repo.
    /// Throws <see cref="CarParseException"/> for structural violations.
    /// </summary>
    /// <returns>Sequence of (key, valueCidHex) plus the commit version (or 0 if absent).</returns>
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
        // Cap derived from block count: a valid walk cannot yield more records than there are blocks in the file.
        HashSet<string> visited = new( car.Blocks.Count );
        int maxNodes = car.Blocks.Count;
        int[] recordCount = [0];

        return (WalkNode( car, mstRootHex, "", 0, visited, maxNodes, recordCount, cancellationToken ), commitVersion);
    }

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
                    // SEC-003: a negative prefix length is structurally invalid and would
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

    private sealed record MstNodeEntry(
        int PrefixLen,
        string KeySuffix,
        string ValueCidHex,
        string? RightChildHex
    );
}
