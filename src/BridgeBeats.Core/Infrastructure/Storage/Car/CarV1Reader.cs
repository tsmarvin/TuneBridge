using System.Formats.Cbor;

namespace BridgeBeats.Core.Infrastructure.Storage.Car;

/// <summary>
/// Parses a CAR v1 file into a <see cref="CarFile"/> containing the root CID and all blocks.
/// </summary>
/// <remarks>
/// CAR v1 format: varint(header_len) | header DAG-CBOR {version:1, roots:[CID]} | sections*
/// Each section: varint(len) | CID bytes | block bytes (len covers both)
/// All block buffers are slices over the single download buffer — no per-block copies.
/// Per-block sha256 digest is verified against the CID.
/// </remarks>
internal static class CarV1Reader {

    private const int MaxBlockSize = 2 * 1024 * 1024; // 2 MB per block
    private const int ExpectedCidBytes = 4 + 32;       // CIDv1 dag-cbor sha2-256
    private const int CarV2Magic = 2;                  // CAR version 2 — rejected

    /// <summary>
    /// Parses a CAR v1 byte buffer and returns the root CID and block dictionary.
    /// Throws <see cref="CarParseException"/> on malformed or unsupported input.
    /// </summary>
    /// <param name="buffer">The raw CAR v1 bytes.</param>
    /// <remarks>
    /// Memory is bounded by the 512 MB download-buffer cap enforced upstream in
    /// ATProtoStorageService.MaxCarBytes — not by a block count. A per-block cap would
    /// false-positive against legitimate repo growth (the production repo grows ~1,810 records
    /// per 6-hour cycle; every record is its own IPLD block). The per-block 2 MB size cap and
    /// per-block sha256 digest verification remain active.
    /// </remarks>
    internal static CarFile Read( ReadOnlyMemory<byte> buffer ) {
        int offset = 0;
        ReadOnlySpan<byte> span = buffer.Span;

        // ── Parse header length varint ──────────────────────────────────────
        if (!Varint.TryRead( span.Slice( offset ), out ulong headerLen, out int headerLenBytes )) {
            throw new CarParseException( "Failed to read CAR header length varint." );
        }
        offset += headerLenBytes;

        if (headerLen == 0 || (ulong)span.Length < (ulong)offset + headerLen) {
            throw new CarParseException( $"CAR header length {headerLen} exceeds buffer." );
        }

        // ── Parse header DAG-CBOR ───────────────────────────────────────────
        ReadOnlyMemory<byte> headerBytes = buffer.Slice( offset, (int)headerLen );
        Cid rootCid = ParseCarHeader( headerBytes );
        offset += (int)headerLen;

        // ── Parse block sections ────────────────────────────────────────────
        Dictionary<string, ReadOnlyMemory<byte>> blocks = [];

        while (offset < span.Length) {
            // Section length varint
            if (!Varint.TryRead( span.Slice( offset ), out ulong sectionLen, out int sectionLenBytes )) {
                throw new CarParseException( $"Failed to read section length varint at offset {offset}." );
            }
            offset += sectionLenBytes;

            if (sectionLen == 0) {
                // Zero-length section = stream terminator (valid in some CAR dialects)
                break;
            }

            if ((ulong)(span.Length - offset) < sectionLen) {
                throw new CarParseException( $"Section length {sectionLen} at offset {offset} exceeds remaining buffer." );
            }

            int sectionStart = offset;

            // CID bytes (fixed 36 bytes for CIDv1 dag-cbor sha256)
            if (span.Length - offset < ExpectedCidBytes) {
                throw new CarParseException( $"Section at offset {offset} too short for CID." );
            }
            Cid blockCid = Cid.ParseFromBytes( span.Slice( offset, ExpectedCidBytes ) );
            offset += ExpectedCidBytes;

            // Block bytes
            int blockLen = (int)sectionLen - ExpectedCidBytes;
            if (blockLen < 0) {
                throw new CarParseException( $"Section length {sectionLen} smaller than CID size at offset {sectionStart}." );
            }
            if (blockLen > MaxBlockSize) {
                throw new CarParseException( $"Block at offset {offset} exceeds 2 MB cap ({blockLen} bytes)." );
            }

            // Verify sha256 digest
            ReadOnlySpan<byte> blockSpan = span.Slice( offset, blockLen );
            if (!blockCid.VerifyBlock( blockSpan )) {
                throw new CarParseException( $"Block digest mismatch at offset {offset} (CID: {blockCid})." );
            }

            // Store as a slice over the original buffer (no copy)
            blocks[blockCid.KeyHex] = buffer.Slice( offset, blockLen );
            offset += blockLen;
        }

        return new CarFile( rootCid, blocks );
    }

    private static Cid ParseCarHeader( ReadOnlyMemory<byte> headerBytes ) =>
        ParseRootCidFromRawHeader( headerBytes );

    private static Cid ParseRootCidFromRawHeader( ReadOnlyMemory<byte> headerBytes ) {
        try {
            CborReader reader = new( headerBytes, CborConformanceMode.Lax );
            _ = reader.ReadStartMap( );

            Cid? rootCid = null;
            int? version = null;

            while (reader.PeekState( ) != CborReaderState.EndMap) {
                string key = reader.ReadTextString( );
                if (key == "roots") {
                    _ = reader.ReadStartArray( );
                    while (reader.PeekState( ) != CborReaderState.EndArray) {
                        CborTag tag = reader.ReadTag( );
                        if (tag != (CborTag)42) {
                            throw new CarParseException( $"CAR header root CID has unexpected tag {(ulong)tag}." );
                        }
                        byte[] linkBytes = reader.ReadByteString( );
                        rootCid = Cid.FromDagCborLinkBytes( linkBytes );
                    }
                    reader.ReadEndArray( );
                } else if (key == "version") {
                    version = reader.ReadInt32( );
                    if (version == CarV2Magic) {
                        throw new CarParseException( "CAR v2 is not supported; only v1 is accepted." );
                    }
                    if (version != 1) {
                        throw new CarParseException( $"Unsupported CAR version {version}; expected 1." );
                    }
                } else {
                    reader.SkipValue( );
                }
            }

            reader.ReadEndMap( );

            if (rootCid is null) {
                throw new CarParseException( "CAR header missing roots." );
            }

            return rootCid.Value;
        } catch (CarParseException) {
            throw;
        } catch (Exception ex) when (ex is CborContentException or OverflowException or InvalidOperationException) {
            throw new CarParseException( "Failed to parse CAR header CBOR.", ex );
        }
    }
}

/// <summary>
/// The result of parsing a CAR v1 file: a root CID and a dictionary of blocks
/// keyed by their CID hex string.
/// </summary>
internal sealed class CarFile {

    internal CarFile( Cid root, Dictionary<string, ReadOnlyMemory<byte>> blocks ) {
        Root = root;
        Blocks = blocks;
    }

    /// <summary>The root (commit) CID.</summary>
    internal Cid Root { get; }

    /// <summary>All blocks, keyed by their CID hex string.</summary>
    internal Dictionary<string, ReadOnlyMemory<byte>> Blocks { get; }
}
