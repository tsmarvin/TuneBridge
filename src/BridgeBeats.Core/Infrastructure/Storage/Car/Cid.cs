using System.Security.Cryptography;

namespace BridgeBeats.Core.Infrastructure.Storage.Car;

/// <summary>
/// Represents a CIDv1 in the atproto format: 0x01 0x71 0x12 0x20 + 32-byte sha256 digest.
/// CIDv0 (starts with 0x12) is rejected per spec.
/// </summary>
internal readonly struct Cid {

    // CIDv1 codec prefix: version=0x01, codec=dag-cbor=0x71, hash-fn=sha2-256=0x12, hash-len=0x20
    private const byte CidV1Version = 0x01;
    private const byte DagCborCodec = 0x71;
    private const byte Sha256HashFn = 0x12;
    private const byte Sha256HashLen = 0x20;
    private const int ExpectedCidBytes = 4 + 32; // 4-byte prefix + 32-byte digest

    private const byte CidV0FirstByte = 0x12;

    private readonly byte[] _digest; // 32-byte sha256 digest

    private Cid( byte[] digest ) {
        _digest = digest;
    }

    /// <summary>
    /// Hex string used as dictionary key.
    /// </summary>
    internal string KeyHex => Convert.ToHexStringLower( _digest );

    /// <summary>
    /// Parses a CIDv1 from raw bytes (4-byte prefix + 32-byte digest).
    /// Throws CarParseException for CIDv0 or malformed CIDs.
    /// </summary>
    internal static Cid ParseFromBytes( ReadOnlySpan<byte> bytes ) {
        if (bytes.Length > 0 && bytes[0] == CidV0FirstByte) {
            throw new CarParseException( "CIDv0 is not supported; atproto repos use CIDv1 only." );
        }

        if (bytes.Length != ExpectedCidBytes) {
            throw new CarParseException(
                bytes.Length < ExpectedCidBytes
                    ? $"CID bytes too short: expected exactly {ExpectedCidBytes}, got {bytes.Length}."
                    : $"CID bytes too long: expected exactly {ExpectedCidBytes}, got {bytes.Length}." );
        }

        if (bytes[0] != CidV1Version || bytes[1] != DagCborCodec ||
            bytes[2] != Sha256HashFn || bytes[3] != Sha256HashLen) {
            throw new CarParseException(
                $"Unexpected CID prefix 0x{bytes[0]:x2} 0x{bytes[1]:x2} 0x{bytes[2]:x2} 0x{bytes[3]:x2}; " +
                "expected CIDv1 dag-cbor sha2-256 (0x01 0x71 0x12 0x20)." );
        }

        byte[] digest = bytes.Slice( 4, 32 ).ToArray( );
        return new Cid( digest );
    }

    /// <summary>
    /// Parses a CID from a DAG-CBOR tag-42 link value.
    /// Tag-42 link bytes have a 0x00 multibase prefix that must be stripped before the CID bytes.
    /// </summary>
    internal static Cid FromDagCborLinkBytes( ReadOnlySpan<byte> bytes ) {
        if (bytes.Length < 1 || bytes[0] != 0x00) {
            throw new CarParseException(
                $"DAG-CBOR tag-42 link missing 0x00 multibase prefix (first byte: 0x{(bytes.Length > 0 ? bytes[0] : 0xFF):x2})." );
        }

        return ParseFromBytes( bytes.Slice( 1 ) );
    }

    /// <summary>
    /// Verifies that the given block bytes hash to this CID's digest.
    /// </summary>
    internal bool VerifyBlock( ReadOnlySpan<byte> blockBytes ) {
        Span<byte> hash = stackalloc byte[32];
        int written = SHA256.HashData( blockBytes, hash );
        return written == 32 && hash.SequenceEqual( _digest );
    }

    /// <summary>
    /// Returns a base32 multibase representation for diagnostics.
    /// </summary>
    public override string ToString( ) {
        // Build: 0x01 0x71 0x12 0x20 + digest, then base32lower with 'b' prefix
        byte[] cidBytes = new byte[ExpectedCidBytes];
        cidBytes[0] = CidV1Version;
        cidBytes[1] = DagCborCodec;
        cidBytes[2] = Sha256HashFn;
        cidBytes[3] = Sha256HashLen;
        _digest.CopyTo( cidBytes, 4 );
        return "b" + Base32Encode( cidBytes );
    }

    private static string Base32Encode( byte[] data ) {
        const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";
        System.Text.StringBuilder sb = new( );
        int bits = 0;
        int accumulator = 0;

        foreach (byte b in data) {
            accumulator = (accumulator << 8) | b;
            bits += 8;
            while (bits >= 5) {
                bits -= 5;
                _ = sb.Append( Alphabet[(accumulator >> bits) & 0x1F] );
            }
        }

        if (bits > 0) {
            _ = sb.Append( Alphabet[(accumulator << (5 - bits)) & 0x1F] );
        }

        return sb.ToString( );
    }
}
