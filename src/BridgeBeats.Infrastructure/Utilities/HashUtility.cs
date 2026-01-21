using System.Security.Cryptography;
using System.Text;

namespace BridgeBeats.Infrastructure.Utilities;

/// <summary>
/// Provides utility methods for hashing strings, primarily used for generating
/// URL-safe lookup keys in Redis cache operations.
/// </summary>
public static class HashUtility {

    /// <summary>
    /// Base32 alphabet (RFC 4648) used for URL-safe encoding.
    /// </summary>
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>
    /// Computes a SHA-256 hash of the input string and returns it as a lowercase
    /// base32-encoded string suitable for use as a cache key.
    /// </summary>
    /// <param name="input">The string to hash.</param>
    /// <returns>A lowercase base32-encoded SHA-256 hash string (52 characters).</returns>
    /// <exception cref="ArgumentNullException">Thrown when input is null.</exception>
    /// <remarks>
    /// The resulting hash is URL-safe and case-insensitive, making it suitable
    /// for Redis keys and other storage systems. The full SHA-256 hash (256 bits)
    /// is encoded as base32 for maximum collision resistance.
    /// </remarks>
    public static string ComputeSha256Base32( string input ) {
        ArgumentNullException.ThrowIfNull( input );

        byte[] hashBytes = SHA256.HashData( Encoding.UTF8.GetBytes( input ) );
        string base32Hash = ToBase32( hashBytes );

        return base32Hash.ToLowerInvariant( );
    }

    /// <summary>
    /// Computes a SHA-256 hash of a URL string for use as a Redis lookup key.
    /// The URL is normalized (lowercased) before hashing to ensure consistent lookups.
    /// </summary>
    /// <param name="url">The URL to hash.</param>
    /// <returns>A lowercase base32-encoded SHA-256 hash string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when url is null.</exception>
    /// <exception cref="ArgumentException">Thrown when url is empty or whitespace.</exception>
    public static string HashUrl( string url ) {
        ArgumentNullException.ThrowIfNull( url );
        if (string.IsNullOrWhiteSpace( url )) {
            throw new ArgumentException( "URL cannot be empty or whitespace", nameof( url ) );
        }

        // Normalize URL to lowercase for consistent hashing
        return ComputeSha256Base32( url.Trim( ).ToLowerInvariant( ) );
    }

    /// <summary>
    /// Converts a byte array to a base32 string using RFC 4648 alphabet.
    /// </summary>
    /// <param name="input">The byte array to convert.</param>
    /// <returns>A base32-encoded string without padding.</returns>
    private static string ToBase32( byte[] input ) {
        if (input == null || input.Length == 0) {
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
