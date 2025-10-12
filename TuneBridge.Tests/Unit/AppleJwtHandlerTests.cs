using System.Security.Cryptography;
using TuneBridge.Domain.Implementations.Auth;

namespace TuneBridge.Tests.Unit;

/// <summary>
/// Unit tests for AppleJwtHandler to verify JWT token generation.
/// Tests the creation and formatting of authentication tokens for Apple MusicKit API.
/// </summary>
public class AppleJwtHandlerTests : IDisposable
{
    private string _testKeyPath = null!;
    private string _testKeyContents = null!;
    private const string TestTeamId = "TEST123456";
    private const string TestKeyId = "KEY1234567";

    public AppleJwtHandlerTests()
    {
        // Generate a valid ES256 (P-256) private key for testing
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        // Export as PKCS8 format which is what .p8 files use
        var privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();
        
        // Create a properly formatted PEM string
        var base64Key = Convert.ToBase64String(privateKeyBytes);
        var formattedKey = new System.Text.StringBuilder();
        formattedKey.AppendLine("-----BEGIN PRIVATE KEY-----");
        
        // Split into 64-character lines as per PEM format
        for (int i = 0; i < base64Key.Length; i += 64)
        {
            int length = Math.Min(64, base64Key.Length - i);
            formattedKey.AppendLine(base64Key.Substring(i, length));
        }
        
        formattedKey.AppendLine("-----END PRIVATE KEY-----");
        _testKeyContents = formattedKey.ToString();
        
        _testKeyPath = Path.Combine(Path.GetTempPath(), $"test_key_{Guid.NewGuid()}.p8");
        File.WriteAllText(_testKeyPath, _testKeyContents);
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Act & Assert
        var handler = new AppleJwtHandler(TestTeamId, TestKeyId, _testKeyContents);
        Assert.NotNull(handler);
    }

    [Fact]
    public void Constructor_WithInvalidKey_ShouldThrowException()
    {
        // Arrange
        var invalidKeyContents = "-----BEGIN PRIVATE KEY-----\nINVALID\n-----END PRIVATE KEY-----";

        // Act & Assert - Invalid key should throw either CryptographicException or ArgumentException
        Assert.ThrowsAny<Exception>(() => 
            new AppleJwtHandler(TestTeamId, TestKeyId, invalidKeyContents));
    }

    [Fact]
    public void GetAuthHeader_ShouldReturnBearerToken()
    {
        // Arrange
        var handler = new AppleJwtHandler(TestTeamId, TestKeyId, _testKeyContents);

        // Act
        var authHeader = handler.NewAuthenticationHeader();

        // Assert
        Assert.NotNull(authHeader);
        Assert.Equal("Bearer", authHeader.Scheme);
        Assert.NotNull(authHeader.Parameter);
        Assert.NotEmpty(authHeader.Parameter);
    }

    [Fact]
    public void GetAuthHeader_ShouldReturnValidJwtStructure()
    {
        // Arrange
        var handler = new AppleJwtHandler(TestTeamId, TestKeyId, _testKeyContents);

        // Act
        var authHeader = handler.NewAuthenticationHeader();
        var token = authHeader.Parameter;

        // Assert - JWT should have 3 parts separated by dots
        Assert.NotNull(token);
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        
        // Each part should be base64url encoded (not empty)
        Assert.All(parts, part => Assert.NotEmpty(part));
    }

    [Fact]
    public void GetAuthHeader_CalledMultipleTimes_ShouldReturnDifferentTokens()
    {
        // Arrange
        var handler = new AppleJwtHandler(TestTeamId, TestKeyId, _testKeyContents);

        // Act
        var token1 = handler.NewAuthenticationHeader().Parameter;
        System.Threading.Thread.Sleep(1000); // Ensure different timestamp
        var token2 = handler.NewAuthenticationHeader().Parameter;

        // Assert - Tokens should be different due to different timestamps
        Assert.NotEqual(token1, token2);
    }

    public void Dispose()
    {
        if (File.Exists(_testKeyPath))
        {
            File.Delete(_testKeyPath);
        }
    }
}
