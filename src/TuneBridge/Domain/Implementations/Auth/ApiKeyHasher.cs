using System.Security.Cryptography;
using System.Text;

namespace TuneBridge.Domain.Implementations.Auth;

/// <summary>
/// Provides API key hashing functionality using HMACSHA256.
/// </summary>
public class ApiKeyHasher {
    private readonly string _salt;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiKeyHasher"/> class.
    /// </summary>
    /// <param name="salt">The cryptographic salt used for hashing API keys.</param>
    /// <exception cref="ArgumentException">Thrown when salt is null or empty.</exception>
    public ApiKeyHasher( string salt ) {
        if (string.IsNullOrWhiteSpace( salt )) {
            throw new ArgumentException( "API key salt cannot be null or empty", nameof( salt ) );
        }
        _salt = salt;
    }

    /// <summary>
    /// Generates a cryptographically secure random API key.
    /// </summary>
    /// <returns>Base64-encoded API key string.</returns>
    public static string GenerateApiKey( ) {
        byte[] randomBytes = RandomNumberGenerator.GetBytes(32 );
        return Convert.ToBase64String( randomBytes );
    }

    /// <summary>
    /// Hashes an API key with the configured salt using HMACSHA256.
    /// </summary>
    /// <param name="apiKey">The API key to hash.</param>
    /// <returns>Base64-encoded hash of the API key.</returns>
    public string HashApiKey( string apiKey ) {
        using HMACSHA256 hmac = new( Encoding.UTF8.GetBytes( _salt ) );
        byte[] hash = hmac.ComputeHash( Encoding.UTF8.GetBytes( apiKey ) );
        return Convert.ToBase64String( hash );
    }

    /// <summary>
    /// Verifies an API key against a stored hash.
    /// </summary>
    /// <param name="apiKey">The API key to verify.</param>
    /// <param name="storedHash">The stored hash to compare against.</param>
    /// <returns>True if the API key matches the hash, false otherwise.</returns>
    public bool VerifyApiKey( string apiKey, string storedHash ) {
        string computedHash = HashApiKey( apiKey );
        return computedHash == storedHash;
    }
}
