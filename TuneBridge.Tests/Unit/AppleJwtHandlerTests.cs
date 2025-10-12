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
        // Generate a test ES256 private key for testing
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyBytes = ecdsa.ExportECPrivateKey();
        var base64Key = Convert.ToBase64String(privateKeyBytes);
        
        _testKeyContents = $"-----BEGIN PRIVATE KEY-----\n{base64Key}\n-----END PRIVATE KEY-----";
        
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
    public void Constructor_WithInvalidKey_ShouldThrowCryptographicException()
    {
        // Arrange
        var invalidKeyContents = "-----BEGIN PRIVATE KEY-----\nINVALID\n-----END PRIVATE KEY-----";

        // Act & Assert
        Assert.Throws<CryptographicException>(() => 
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
