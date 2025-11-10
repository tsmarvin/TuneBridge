using System.Security.Cryptography;
using TuneBridge.Domain.Implementations.Auth;

namespace TuneBridge.Tests.Unit;

/// <summary>
/// Unit tests for AppleJwtHandler to verify JWT token generation.
/// Tests the creation and formatting of authentication tokens for Apple MusicKit API.
/// </summary>
[TestClass]
public class AppleJwtHandlerTests {
    private string _testKeyPath = null!;
    private string _testKeyContents = null!;
    private const string TestTeamId = "TEST123456";
    private const string TestKeyId = "KEY1234567";

    [TestInitialize]
    public void Initialize( ) {
        // Generate a valid ES256 (P-256) private key for testing
        using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        // Export as PKCS8 format which is what .p8 files use
        byte[] privateKeyBytes = ecdsa.ExportPkcs8PrivateKey( );

        // Create a properly formatted PEM string
        string base64Key = Convert.ToBase64String(privateKeyBytes);
        System.Text.StringBuilder formattedKey = new( );
        _ = formattedKey.AppendLine( "-----BEGIN PRIVATE KEY-----" );

        // Split into 64-character lines as per PEM format
        for (int i = 0; i < base64Key.Length; i += 64) {
            int length = Math.Min(64, base64Key.Length - i);
            _ = formattedKey.AppendLine( base64Key.Substring( i, length ) );
        }

        _ = formattedKey.AppendLine( "-----END PRIVATE KEY-----" );
        _testKeyContents = formattedKey.ToString( );

        _testKeyPath = Path.Combine( Path.GetTempPath( ), $"test_key_{Guid.NewGuid( )}.p8" );
        File.WriteAllText( _testKeyPath, _testKeyContents );
    }

    [TestMethod]
    public void Constructor_WithValidParameters_ShouldCreateInstance( ) {
        // Act & Assert
        AppleJwtHandler handler = new(TestTeamId, TestKeyId, _testKeyContents);
        Assert.IsNotNull( handler );
    }

    [TestMethod]
    public void Constructor_WithInvalidKey_ShouldThrowException( ) {
        // Arrange
        string invalidKeyContents = "-----BEGIN PRIVATE KEY-----\nINVALID\n-----END PRIVATE KEY-----";

        // Act & Assert - Invalid key should throw either CryptographicException or ArgumentException
        _ = Assert.ThrowsExactly<ArgumentException>( ( ) =>
            new AppleJwtHandler( TestTeamId, TestKeyId, invalidKeyContents ) );
    }

    [TestMethod]
    public void GetAuthHeader_ShouldReturnBearerToken( ) {
        // Arrange
        AppleJwtHandler handler = new(TestTeamId, TestKeyId, _testKeyContents);

        // Act
        System.Net.Http.Headers.AuthenticationHeaderValue authHeader = handler.NewAuthenticationHeader( );

        // Assert
        Assert.IsNotNull( authHeader );
        Assert.AreEqual( "Bearer", authHeader.Scheme );
        Assert.IsNotNull( authHeader.Parameter );
        Assert.IsTrue( authHeader.Parameter.Length > 0 );
    }

    [TestMethod]
    public void GetAuthHeader_ShouldReturnValidJwtStructure( ) {
        // Arrange
        AppleJwtHandler handler = new(TestTeamId, TestKeyId, _testKeyContents);

        // Act
        System.Net.Http.Headers.AuthenticationHeaderValue authHeader = handler.NewAuthenticationHeader( );
        string? token = authHeader.Parameter;

        // Assert - JWT should have 3 parts separated by dots
        Assert.IsNotNull( token );
        string[] parts = token.Split( '.' );
        Assert.AreEqual( 3, parts.Length );

        // Each part should be base64url encoded (not empty)
        foreach (string part in parts) {
            Assert.IsTrue( part.Length > 0 );
        }
    }

    [TestMethod]
    public void GetAuthHeader_CalledMultipleTimes_ShouldReturnDifferentTokens( ) {
        // Arrange
        AppleJwtHandler handler = new(TestTeamId, TestKeyId, _testKeyContents);

        // Act
        string? token1 = handler.NewAuthenticationHeader( ).Parameter;
        System.Threading.Thread.Sleep( 1000 ); // Ensure different timestamp
        string? token2 = handler.NewAuthenticationHeader( ).Parameter;

        // Assert - Tokens should be different due to different timestamps
        Assert.AreNotEqual( token1, token2 );
    }

    [TestCleanup]
    public void Cleanup( ) {
        if (File.Exists( _testKeyPath )) {
            File.Delete( _testKeyPath );
        }
    }
}
