using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Implementations.Services;
using TuneBridge.Domain.Interfaces;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Tests.Unit;

[TestClass]
public class OpenGraphCardServiceTests {

    [TestMethod]
    public void BaseUrl_WhenSet_ReturnsBaseUrl( ) {
        // Arrange
        string baseUrl = "tunebridge.media";
        IOpenGraphCardService service = new OpenGraphCardService( baseUrl );

        // Act
        string result = service.BaseUrl;

        // Assert
        Assert.AreEqual( baseUrl, result );
    }

    [TestMethod]
    public void IsEnabled_WhenBaseUrlIsNotEmpty_ReturnsTrue( ) {
        // Arrange
        string baseUrl = "tunebridge.media";
        IOpenGraphCardService service = new OpenGraphCardService( baseUrl );

        // Act
        bool result = service.IsEnabled;

        // Assert
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void IsEnabled_WhenBaseUrlIsEmpty_ReturnsFalse( ) {
        // Arrange
        string baseUrl = string.Empty;
        IOpenGraphCardService service = new OpenGraphCardService( baseUrl );

        // Act
        bool result = service.IsEnabled;

        // Assert
        Assert.IsFalse( result );
    }

    [TestMethod]
    public void StoreResult_WhenCalled_ReturnsUrlWithBaseUrl( ) {
        // Arrange
        string baseUrl = "tunebridge.media";
        IOpenGraphCardService service = new OpenGraphCardService( baseUrl );
        MediaLinkResult result = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResultDto> {
                {
                    SupportedProviders.AppleMusic, new MusicLookupResultDto {
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

    [TestMethod]
    public void StoreMultipleResults_WhenCalled_ReturnsMultiCardUrl( ) {
        // Arrange
        string baseUrl = "tunebridge.media";
        IOpenGraphCardService service = new OpenGraphCardService( baseUrl );
        List<MediaLinkResult> results = [
            new MediaLinkResult {
                Results = new Dictionary<SupportedProviders, MusicLookupResultDto> {
                    {
                        SupportedProviders.AppleMusic, new MusicLookupResultDto {
                            Title = "Test Track 1",
                            Artist = "Test Artist",
                            URL = "https://music.apple.com/us/album/test/123",
                            IsAlbum = false,
                            IsPrimary = true
                        }
                    }
                }
            },
            new MediaLinkResult {
                Results = new Dictionary<SupportedProviders, MusicLookupResultDto> {
                    {
                        SupportedProviders.Spotify, new MusicLookupResultDto {
                            Title = "Test Track 2",
                            Artist = "Test Artist",
                            URL = "https://open.spotify.com/track/456",
                            IsAlbum = false,
                            IsPrimary = true
                        }
                    }
                }
            }
        ];

        // Act
        string multiCardUrl = service.StoreMultipleResults( results );

        // Assert
        Assert.StartsWith( $"https://{baseUrl}/card/multi/", multiCardUrl, "Multi-card URL should start with base URL and include /multi/" );
    }

    [TestMethod]
    public void StoreMultipleResults_WhenEmptyCollection_ThrowsArgumentException( ) {
        // Arrange
        string baseUrl = "tunebridge.media";
        IOpenGraphCardService service = new OpenGraphCardService( baseUrl );
        List<MediaLinkResult> emptyResults = [];

        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentException>( ( ) => service.StoreMultipleResults( emptyResults ) );
    }

    [TestMethod]
    public void GetMultipleResults_WhenStoredAndNotExpired_ReturnsResults( ) {
        // Arrange
        string baseUrl = "tunebridge.media";
        IOpenGraphCardService service = new OpenGraphCardService( baseUrl );
        List<MediaLinkResult> results = [
            new MediaLinkResult {
                Results = new Dictionary<SupportedProviders, MusicLookupResultDto> {
                    {
                        SupportedProviders.AppleMusic, new MusicLookupResultDto {
                            Title = "Test Track 1",
                            Artist = "Test Artist",
                            URL = "https://music.apple.com/us/album/test/123",
                            IsAlbum = false,
                            IsPrimary = true
                        }
                    }
                }
            },
            new MediaLinkResult {
                Results = new Dictionary<SupportedProviders, MusicLookupResultDto> {
                    {
                        SupportedProviders.Spotify, new MusicLookupResultDto {
                            Title = "Test Track 2",
                            Artist = "Test Artist",
                            URL = "https://open.spotify.com/track/456",
                            IsAlbum = false,
                            IsPrimary = true
                        }
                    }
                }
            }
        ];

        // Act
        string multiCardUrl = service.StoreMultipleResults( results );
        string id = multiCardUrl.Split( '/' ).Last( );
        IReadOnlyList<MediaLinkResult>? retrievedResults = service.GetMultipleResults( id );

        // Assert
        Assert.IsNotNull( retrievedResults, "Retrieved results should not be null" );
        Assert.HasCount( 2, retrievedResults, "Should retrieve the same number of results" );
        Assert.AreEqual( "Test Track 1", retrievedResults[0].Results[SupportedProviders.AppleMusic].Title );
        Assert.AreEqual( "Test Track 2", retrievedResults[1].Results[SupportedProviders.Spotify].Title );
    }

    [TestMethod]
    public void GetMultipleResults_WhenNotFound_ReturnsNull( ) {
        // Arrange
        string baseUrl = "tunebridge.media";
        IOpenGraphCardService service = new OpenGraphCardService( baseUrl );
        string nonExistentId = "nonexistent-id";

        // Act
        IReadOnlyList<MediaLinkResult>? result = service.GetMultipleResults( nonExistentId );

        // Assert
        Assert.IsNull( result, "Should return null for non-existent ID" );
    }
}
