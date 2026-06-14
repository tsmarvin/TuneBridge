using System.Security.Cryptography;
using System.Text;

namespace BridgeBeats.Core.Infrastructure.Identity;

/// <summary>
/// Generates and verifies API keys using a keyed HMAC-SHA256 hash.
/// </summary>
/// <remarks>
/// Hashing is deterministic: the same API key always produces the same hash for a given salt.
/// This is intentional so a presented key can be looked up directly against the unique
/// <c>ApiKeyHash</c> index on <see cref="ApplicationUser"/>. It is not a per-record salted
/// password hash. The salt supplied to the constructor is used as the HMAC key, not as a
/// per-key random salt.
/// </remarks>
public class ApiKeyHasher {
    private readonly string _salt;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiKeyHasher"/> class.
    /// </summary>
    /// <param name="salt">
    /// The secret used as the HMAC key when hashing API keys. Typically sourced from configuration.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="salt"/> is null, empty, or whitespace.</exception>
    public ApiKeyHasher( string salt ) {
        if (string.IsNullOrWhiteSpace( salt )) {
            throw new ArgumentException( "API key salt cannot be null or empty", nameof( salt ) );
        }
        _salt = salt;
    }

    /// <summary>
    /// Generates a cryptographically secure random API key.
    /// </summary>
    /// <returns>
    /// A base64-encoded string of 32 cryptographically random bytes, produced by
    /// <see cref="System.Security.Cryptography.RandomNumberGenerator"/>.
    /// </returns>
    public static string GenerateApiKey( ) {
        byte[] randomBytes = RandomNumberGenerator.GetBytes( 32 );
        return Convert.ToBase64String( randomBytes );
    }

    /// <summary>
    /// Computes the HMAC-SHA256 hash of an API key, keyed by the configured salt.
    /// </summary>
    /// <param name="apiKey">The plaintext API key to hash.</param>
    /// <returns>The base64-encoded HMAC-SHA256 hash of <paramref name="apiKey"/>.</returns>
    public string HashApiKey( string apiKey ) {
        using HMACSHA256 hmac = new( Encoding.UTF8.GetBytes( _salt ) );
        byte[] hash = hmac.ComputeHash( Encoding.UTF8.GetBytes( apiKey ) );
        return Convert.ToBase64String( hash );
    }

    /// <summary>
    /// Verifies a presented API key against a previously stored hash.
    /// </summary>
    /// <remarks>
    /// Recomputes the hash of <paramref name="apiKey"/> and compares it to <paramref name="storedHash"/>
    /// using an ordinal string equality check.
    /// </remarks>
    /// <param name="apiKey">The plaintext API key presented for verification.</param>
    /// <param name="storedHash">The previously stored base64-encoded hash to compare against.</param>
    /// <returns><see langword="true"/> if the recomputed hash equals <paramref name="storedHash"/>; otherwise <see langword="false"/>.</returns>
    public bool VerifyApiKey( string apiKey, string storedHash ) {
        string computedHash = HashApiKey( apiKey );
        return computedHash == storedHash;
    }
}
