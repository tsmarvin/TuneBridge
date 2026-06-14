using System.Security.Cryptography;
using System.Text;
using BridgeBeats.Core.Domain.Providers.Common;

namespace BridgeBeats.Core.Infrastructure.Utilities;

/// <summary>
/// Helpers that compute stable, lowercase base32 SHA-256 hashes of strings and normalized URLs
/// for use as cache and lookup keys, primarily for Redis cache operations.
/// </summary>
public static class HashUtility {

    /// <summary>
    /// The RFC 4648 base32 alphabet (uppercase A-Z and digits 2-7) used to encode hash bytes
    /// before they are lowercased.
    /// </summary>
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>
    /// Computes the SHA-256 hash of the UTF-8 bytes of <paramref name="input"/> and returns it as
    /// a lowercase base32 string.
    /// </summary>
    /// <param name="input">The value to hash.</param>
    /// <returns>The lowercase base32 encoding of the SHA-256 hash of <paramref name="input"/>.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="input"/> is null.</exception>
    /// <remarks>
    /// The full SHA-256 hash (256 bits) is encoded, giving a URL-safe, case-insensitive key.
    /// This is a non-cryptographic-identity use: the hash provides a stable key, not a secret.
    /// </remarks>
    public static string ComputeSha256Base32( string input ) {
        ArgumentNullException.ThrowIfNull( input );

        byte[] hashBytes = SHA256.HashData( Encoding.UTF8.GetBytes( input ) );
        string base32Hash = ToBase32( hashBytes );

        return base32Hash.ToLowerInvariant( );
    }

    /// <summary>
    /// Normalizes <paramref name="url"/> for cache-key stability and returns the lowercase base32
    /// SHA-256 hash of the normalized form.
    /// </summary>
    /// <param name="url">The URL to normalize and hash.</param>
    /// <returns>The lowercase base32 SHA-256 hash of the normalized URL.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="url"/> is null.</exception>
    /// <exception cref="System.ArgumentException">Thrown when <paramref name="url"/> is empty or whitespace.</exception>
    /// <remarks>
    /// Normalization is performed by
    /// <see cref="BridgeBeats.Core.Domain.Providers.Common.LinkNormalizer.Normalize(string)"/> so that
    /// links differing only in scheme, host casing, <c>www.</c>, fragment, or non-significant query
    /// parameters hash to the same key.
    /// </remarks>
    public static string HashUrl( string url ) {
        ArgumentNullException.ThrowIfNull( url );
        if (string.IsNullOrWhiteSpace( url )) {
            throw new ArgumentException( "URL cannot be empty or whitespace", nameof( url ) );
        }

        // Normalize URL using LinkNormalizer to strip query params, protocol, etc. for consistent hashing
        string normalized = LinkNormalizer.Normalize( url );
        return ComputeSha256Base32( normalized );
    }

    /// <summary>
    /// Encodes a byte array as a base32 string using <see cref="Base32Alphabet"/>, without padding.
    /// </summary>
    /// <param name="input">The bytes to encode.</param>
    /// <returns>The base32 representation of <paramref name="input"/>, or an empty string when the input is empty.</returns>
    private static string ToBase32( byte[] input ) {
        if (input.Length == 0) {
            return string.Empty;
        }

        StringBuilder result = new( );
        int bits = 0;
        int value = 0;

        foreach (byte b in input) {
            value = (value << 8) | b;
            bits += 8;

            while (bits >= 5) {
                bits -= 5;
                int index = (value >> bits) & 0x1F;
                _ = result.Append( Base32Alphabet[index] );
            }
        }

        if (bits > 0) {
            int index = (value << (5 - bits)) & 0x1F;
            _ = result.Append( Base32Alphabet[index] );
        }

        // Return without padding for URL safety
        return result.ToString( );
    }
}
