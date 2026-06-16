using BridgeBeats.Core.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests <see cref="DataProtectionLookupProtector"/>, the lookup protector that wraps an
/// <see cref="IDataProtectionProvider"/> and derives a per-key-id protector to encrypt and decrypt searchable
/// PII. Covers protect/unprotect round-trips, null and empty-string passthrough, ciphertext differing from
/// plaintext, key-id isolation, decrypting with the wrong key id, and the null-provider constructor guard.
/// </summary>
[TestClass]
public class DataProtectionLookupProtectorTests {

    /// <summary>The protector under test, backed by a real DataProtection provider built in <see cref="Setup"/>.</summary>
    private DataProtectionLookupProtector _protector = null!;

    /// <summary>
    /// Builds a DataProtection provider with a fixed application name and constructs the protector under test
    /// before each test.
    /// </summary>
    [TestInitialize]
    public void Setup( ) {
        // Use the ephemeral Data Protection provider for test isolation
        ServiceCollection services = new( );
        _ = services.AddDataProtection( )
            .SetApplicationName( "BridgeBeats.Tests" );

        ServiceProvider provider = services.BuildServiceProvider( );
        IDataProtectionProvider dpProvider = provider.GetRequiredService<IDataProtectionProvider>( );
        _protector = new DataProtectionLookupProtector( dpProvider );
    }

    /// <summary>
    /// Verifies protecting then unprotecting a value under the same key id returns the original plaintext.
    /// </summary>
    [TestMethod]
    public void Protect_Unprotect_RoundTrip_ReturnsOriginalData( ) {
        // Arrange
        string keyId = "v1";
        string originalData = "sensitive-token-value";

        // Act
        string? encrypted = _protector.Protect( keyId, originalData );
        string? decrypted = _protector.Unprotect( keyId, encrypted );

        // Assert
        Assert.AreEqual( originalData, decrypted );
    }

    /// <summary>
    /// Verifies <see cref="DataProtectionLookupProtector.Protect"/> passes a null input through as null.
    /// </summary>
    [TestMethod]
    public void Protect_ReturnsNull_ForNullInput( ) {
        // Arrange
        string keyId = "v1";

        // Act
        string? result = _protector.Protect( keyId, null );

        // Assert
        Assert.IsNull( result );
    }

    /// <summary>
    /// Verifies <see cref="DataProtectionLookupProtector.Unprotect"/> passes a null input through as null.
    /// </summary>
    [TestMethod]
    public void Unprotect_ReturnsNull_ForNullInput( ) {
        // Arrange
        string keyId = "v1";

        // Act
        string? result = _protector.Unprotect( keyId, null );

        // Assert
        Assert.IsNull( result );
    }

    /// <summary>
    /// Verifies the protected output is non-null and differs from the plaintext input.
    /// </summary>
    [TestMethod]
    public void Protect_ProducesCiphertext_DifferentFromPlaintext( ) {
        // Arrange
        string keyId = "v1";
        string originalData = "my-secret-token";

        // Act
        string? encrypted = _protector.Protect( keyId, originalData );

        // Assert
        Assert.IsNotNull( encrypted );
        Assert.AreNotEqual( originalData, encrypted );
    }

    /// <summary>
    /// Verifies protecting identical plaintext under two different key ids yields different ciphertext, confirming
    /// each key id derives a distinct protector.
    /// </summary>
    [TestMethod]
    public void Protect_DifferentKeyIds_ProduceDifferentCiphertext( ) {
        // Arrange
        string originalData = "same-data";

        // Act
        string? encrypted1 = _protector.Protect( "v1", originalData );
        string? encrypted2 = _protector.Protect( "v2", originalData );

        // Assert
        Assert.IsNotNull( encrypted1 );
        Assert.IsNotNull( encrypted2 );
        Assert.AreNotEqual( encrypted1, encrypted2 );
    }

    /// <summary>
    /// Verifies unprotecting ciphertext with a different key id than it was protected under throws a
    /// <see cref="System.Security.Cryptography.CryptographicException"/>.
    /// </summary>
    [TestMethod]
    public void Unprotect_WithWrongKeyId_ThrowsException( ) {
        // Arrange
        string originalData = "sensitive-data";
        string? encrypted = _protector.Protect( "v1", originalData );

        // Act & Assert
        _ = Assert.ThrowsExactly<System.Security.Cryptography.CryptographicException>( ( ) =>
            _protector.Unprotect( "v2", encrypted )
        );
    }

    /// <summary>
    /// Verifies the constructor rejects a null <see cref="IDataProtectionProvider"/> with an
    /// <see cref="ArgumentNullException"/>.
    /// </summary>
    [TestMethod]
    public void Constructor_ThrowsArgumentNullException_ForNullProvider( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new DataProtectionLookupProtector( null! )
        );
    }

    /// <summary>
    /// Verifies an empty string protects and unprotects back to an empty string (the empty value is encrypted,
    /// not treated as null passthrough).
    /// </summary>
    [TestMethod]
    public void Protect_Unprotect_RoundTrip_WorksWithEmptyString( ) {
        // Arrange
        string keyId = "v1";
        string originalData = "";

        // Act
        string? encrypted = _protector.Protect( keyId, originalData );
        string? decrypted = _protector.Unprotect( keyId, encrypted );

        // Assert
        Assert.AreEqual( originalData, decrypted );
    }
}
