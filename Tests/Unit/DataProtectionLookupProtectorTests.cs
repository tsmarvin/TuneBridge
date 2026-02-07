using BridgeBeats.Core.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="DataProtectionLookupProtector"/>.
/// </summary>
[TestClass]
public class DataProtectionLookupProtectorTests {

    private DataProtectionLookupProtector _protector = null!;

    /// <summary>
    /// Initializes test resources before each test.
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
    /// Verifies that Protect followed by Unprotect returns the original data.
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
    /// Verifies that Protect returns null for null input.
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
    /// Verifies that Unprotect returns null for null input.
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
    /// Verifies that encrypted data differs from the original plaintext.
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
    /// Verifies that different key IDs produce different ciphertext for the same plaintext.
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
    /// Verifies that data encrypted with one key ID cannot be decrypted with a different key ID.
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
    /// Verifies that the constructor throws for null IDataProtectionProvider.
    /// </summary>
    [TestMethod]
    public void Constructor_ThrowsArgumentNullException_ForNullProvider( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new DataProtectionLookupProtector( null! )
        );
    }

    /// <summary>
    /// Verifies that Protect works with empty string input.
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
