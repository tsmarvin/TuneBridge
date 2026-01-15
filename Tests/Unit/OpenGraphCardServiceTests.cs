using BridgeBeats.Domain.Contracts.DTOs;
using BridgeBeats.Domain.Implementations.Services;
using BridgeBeats.Domain.Interfaces;
using BridgeBeats.Domain.Types.Enums;

namespace BridgeBeats.Tests.Unit;

[TestClass]
public class OpenGraphCardServiceTests {

    [TestMethod]
    public void BaseUrl_WhenSet_ReturnsBaseUrl( ) {
        // Arrange
        string baseUrl = "bridgebeats.link";
        IOpenGraphCardService service = new OpenGraphCardService( baseUrl, 1, 500 );

        // Act
        string result = service.BaseUrl;

        // Assert
        Assert.AreEqual( baseUrl, result );
    }

    [TestMethod]
    public void IsEnabled_WhenBaseUrlIsNotEmpty_ReturnsTrue( ) {
        // Arrange
        string baseUrl = "bridgebeats.link";
        IOpenGraphCardService service = new OpenGraphCardService( baseUrl, 1, 500 );

        // Act
        bool result = service.IsEnabled;

        // Assert
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void IsEnabled_WhenBaseUrlIsEmpty_ReturnsFalse( ) {
        // Arrange
        string baseUrl = string.Empty;
        IOpenGraphCardService service = new OpenGraphCardService( baseUrl, 1, 500 );

        // Act
        bool result = service.IsEnabled;

        // Assert
        Assert.IsFalse( result );
    }

    [TestMethod]
    public void StoreResult_WhenCalled_ReturnsUrlWithBaseUrl( ) {
        // Arrange
        string baseUrl = "bridgebeats.link";
        IOpenGraphCardService service = new OpenGraphCardService( baseUrl, 1, 500 );
        MediaLinkResult result = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResult> {
                {
                    SupportedProviders.AppleMusic, new MusicLookupResult {
                        Title = "Test Track",
                        Artist = "Test Artist",
                        URL = "https://music.apple.com/us/album/test/123",
                        IsAlbum = false,
                        IsPrimary = true
                    }
                }
            }
        };

        // Act
        string cardUrl = service.StoreResult( result );

        // Assert
        Assert.StartsWith( $"https://{baseUrl}/card/", cardUrl, "Card URL should start with base URL" );
    }
}
