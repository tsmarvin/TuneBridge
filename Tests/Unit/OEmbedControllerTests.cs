using System.Text.Json;
using BridgeBeats.Domain.Contracts.DTOs;
using BridgeBeats.Domain.Interfaces;
using BridgeBeats.Domain.Types.Enums;
using BridgeBeats.Web.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for OEmbedController.
/// </summary>
[TestClass]
public class OEmbedControllerTests {

    private Mock<IOpenGraphCardService> _mockCardService = null!;
    private Mock<ILogger<OEmbedController>> _mockLogger = null!;
    private OEmbedController _controller = null!;

    [TestInitialize]
    public void TestInitialize( ) {
        _mockCardService = new Mock<IOpenGraphCardService>( );
        _mockLogger = new Mock<ILogger<OEmbedController>>( );

        // Setup default base URL
        _ = _mockCardService.Setup( s => s.BaseUrl ).Returns( "bridgebeats.link" );

        _controller = new OEmbedController( _mockCardService.Object, _mockLogger.Object );
    }

    [TestMethod]
    public void GetOEmbed_WithValidCardUrl_ReturnsOkWithOEmbedResponse( ) {
        // Arrange
        const string cardId = "test-card-123";
        const string cardUrl = "https://bridgebeats.link/card/test-card-123";

        MediaLinkResult mockResult = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResultDto> {
                {
                    SupportedProviders.Spotify,
                    new MusicLookupResultDto {
                        Title = "Test Song",
                        Artist = "Test Artist",
                        URL = "https://open.spotify.com/track/123",
                        ArtUrl = "https://example.com/art.jpg",
                        ExternalId = "USUM71234567",
                        IsAlbum = false
                    }
                }
            }
        };

        _ = _mockCardService.Setup( s => s.GetResult( cardId ) ).Returns( mockResult );

        // Act
        IActionResult result = _controller.GetOEmbed( cardUrl, null, null, null );

        // Assert
        result.Should( ).BeOfType<OkObjectResult>( );
        OkObjectResult okResult = (OkObjectResult)result;
        okResult.Value.Should( ).BeOfType<OEmbedResponse>( );

        OEmbedResponse? oembedResponse = okResult.Value as OEmbedResponse;
        oembedResponse.Should( ).NotBeNull( );
        oembedResponse!.Version.Should( ).Be( "1.0" );
        oembedResponse.Type.Should( ).Be( "rich" );
        oembedResponse.Title.Should( ).Be( "Test Song" );
        oembedResponse.AuthorName.Should( ).Be( "Test Artist" );
        oembedResponse.ProviderName.Should( ).Be( "BridgeBeats" );
        oembedResponse.ProviderUrl.Should( ).Be( "https://bridgebeats.link" );
        oembedResponse.Html.Should( ).NotBeNullOrEmpty( );
        oembedResponse.Width.Should( ).Be( 550 ); // Default width
        oembedResponse.Height.Should( ).Be( 250 ); // Default height
        oembedResponse.ThumbnailUrl.Should( ).Be( "https://example.com/art.jpg" );
    }

    [TestMethod]
    public void GetOEmbed_WithRelativeCardUrl_ReturnsOkWithOEmbedResponse( ) {
        // Arrange
        const string cardId = "test-card-123";
        const string cardUrl = "/card/test-card-123";

        MediaLinkResult mockResult = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResultDto> {
                {
                    SupportedProviders.AppleMusic,
                    new MusicLookupResultDto {
                        Title = "Test Album",
                        Artist = "Test Artist",
                        URL = "https://music.apple.com/us/album/123",
                        ArtUrl = "https://example.com/album-art.jpg",
                        ExternalId = "00123456789012",
                        IsAlbum = true
                    }
                }
            }
        };

        _ = _mockCardService.Setup( s => s.GetResult( cardId ) ).Returns( mockResult );

        // Act
        IActionResult result = _controller.GetOEmbed( cardUrl, null, null, null );

        // Assert
        result.Should( ).BeOfType<OkObjectResult>( );
        OkObjectResult okResult = (OkObjectResult)result;
        okResult.Value.Should( ).BeOfType<OEmbedResponse>( );

        OEmbedResponse? oembedResponse = okResult.Value as OEmbedResponse;
        oembedResponse.Should( ).NotBeNull( );
        oembedResponse!.Title.Should( ).Be( "Test Album" );
        oembedResponse.Html.Should( ).Contain( "iframe" );
        oembedResponse.Html.Should( ).Contain( $"https://bridgebeats.link/card/{cardId}/embed" );
    }

    [TestMethod]
    public void GetOEmbed_WithMaxDimensions_RespectsMaxWidthAndHeight( ) {
        // Arrange
        const string cardId = "test-card-123";
        const string cardUrl = "https://bridgebeats.link/card/test-card-123";
        const int maxWidth = 400;
        const int maxHeight = 300;

        MediaLinkResult mockResult = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResultDto> {
                {
                    SupportedProviders.Spotify,
                    new MusicLookupResultDto {
                        Title = "Test Song",
                        Artist = "Test Artist",
                        URL = "https://open.spotify.com/track/123",
                        ArtUrl = "https://example.com/art.jpg"
                    }
                }
            }
        };

        _ = _mockCardService.Setup( s => s.GetResult( cardId ) ).Returns( mockResult );

        // Act
        IActionResult result = _controller.GetOEmbed( cardUrl, maxWidth, maxHeight, null );

        // Assert
        result.Should( ).BeOfType<OkObjectResult>( );
        OkObjectResult okResult = (OkObjectResult)result;

        OEmbedResponse? oembedResponse = okResult.Value as OEmbedResponse;
        oembedResponse.Should( ).NotBeNull( );
        oembedResponse!.Width.Should( ).Be( maxWidth );
        oembedResponse.Height.Should( ).Be( maxHeight );
        oembedResponse.Html.Should( ).Contain( $"width=\"{maxWidth}\"" );
        oembedResponse.Html.Should( ).Contain( $"height=\"{maxHeight}\"" );
    }

    [TestMethod]
    public void GetOEmbed_WithMissingUrl_ReturnsBadRequest( ) {
        // Act
        IActionResult result = _controller.GetOEmbed( string.Empty, null, null, null );

        // Assert
        result.Should( ).BeOfType<BadRequestObjectResult>( );
        BadRequestObjectResult badRequestResult = (BadRequestObjectResult)result;
        badRequestResult.Value.Should( ).NotBeNull( );

        // Check that error message exists
        string? json = JsonSerializer.Serialize( badRequestResult.Value );
        json.Should( ).Contain( "error" );
        json.Should( ).Contain( "URL parameter is required" );
    }

    [TestMethod]
    public void GetOEmbed_WithInvalidUrlFormat_ReturnsBadRequest( ) {
        // Act
        IActionResult result = _controller.GetOEmbed( "https://example.com/not-a-card", null, null, null );

        // Assert
        result.Should( ).BeOfType<BadRequestObjectResult>( );
        BadRequestObjectResult badRequestResult = (BadRequestObjectResult)result;
        badRequestResult.Value.Should( ).NotBeNull( );

        string? json = JsonSerializer.Serialize( badRequestResult.Value );
        json.Should( ).Contain( "error" );
        json.Should( ).Contain( "Invalid card URL format" );
    }

    [TestMethod]
    public void GetOEmbed_WithExpiredCard_ReturnsNotFound( ) {
        // Arrange
        const string cardId = "expired-card-456";
        const string cardUrl = "https://bridgebeats.link/card/expired-card-456";

        _ = _mockCardService.Setup( s => s.GetResult( cardId ) ).Returns( (MediaLinkResult?)null );

        // Act
        IActionResult result = _controller.GetOEmbed( cardUrl, null, null, null );

        // Assert
        result.Should( ).BeOfType<NotFoundObjectResult>( );
        NotFoundObjectResult notFoundResult = (NotFoundObjectResult)result;
        notFoundResult.Value.Should( ).NotBeNull( );

        string? json = JsonSerializer.Serialize( notFoundResult.Value );
        json.Should( ).Contain( "error" );
        json.Should( ).Contain( "Card not found or expired" );
    }

    [TestMethod]
    public void GetOEmbed_WithUnsupportedFormat_ReturnsBadRequest( ) {
        // Arrange
        const string cardUrl = "https://bridgebeats.link/card/test-card-123";

        // Act
        IActionResult result = _controller.GetOEmbed( cardUrl, null, null, "xml" );

        // Assert
        result.Should( ).BeOfType<BadRequestObjectResult>( );
        BadRequestObjectResult badRequestResult = (BadRequestObjectResult)result;
        badRequestResult.Value.Should( ).NotBeNull( );

        string? json = JsonSerializer.Serialize( badRequestResult.Value );
        json.Should( ).Contain( "error" );
        json.Should( ).Contain( "Only JSON format is supported" );
    }

    [TestMethod]
    public void GetOEmbed_WithJsonFormat_ReturnsOk( ) {
        // Arrange
        const string cardId = "test-card-123";
        const string cardUrl = "https://bridgebeats.link/card/test-card-123";

        MediaLinkResult mockResult = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResultDto> {
                {
                    SupportedProviders.Spotify,
                    new MusicLookupResultDto {
                        Title = "Test Song",
                        Artist = "Test Artist",
                        URL = "https://open.spotify.com/track/123",
                        ArtUrl = "https://example.com/art.jpg"
                    }
                }
            }
        };

        _ = _mockCardService.Setup( s => s.GetResult( cardId ) ).Returns( mockResult );

        // Act
        IActionResult result = _controller.GetOEmbed( cardUrl, null, null, "json" );

        // Assert
        result.Should( ).BeOfType<OkObjectResult>( );
    }

    [TestMethod]
    public void GetOEmbed_WithEmbedUrlInCardUrl_ExtractsCorrectCardId( ) {
        // Arrange
        const string cardId = "test-card-123";
        const string cardUrl = "https://bridgebeats.link/card/test-card-123/embed";

        MediaLinkResult mockResult = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResultDto> {
                {
                    SupportedProviders.Spotify,
                    new MusicLookupResultDto {
                        Title = "Test Song",
                        Artist = "Test Artist",
                        URL = "https://open.spotify.com/track/123",
                        ArtUrl = "https://example.com/art.jpg"
                    }
                }
            }
        };

        _ = _mockCardService.Setup( s => s.GetResult( cardId ) ).Returns( mockResult );

        // Act
        IActionResult result = _controller.GetOEmbed( cardUrl, null, null, null );

        // Assert
        result.Should( ).BeOfType<OkObjectResult>( );
        _mockCardService.Verify( s => s.GetResult( cardId ), Times.Once );
    }

    [TestMethod]
    public void GetOEmbed_GeneratesProperlyEscapedHtml( ) {
        // Arrange
        const string cardId = "test-card-123";
        const string cardUrl = "https://bridgebeats.link/card/test-card-123";

        MediaLinkResult mockResult = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResultDto> {
                {
                    SupportedProviders.Spotify,
                    new MusicLookupResultDto {
                        Title = "Test \"Song\" with <special> & chars",
                        Artist = "Test Artist",
                        URL = "https://open.spotify.com/track/123",
                        ArtUrl = "https://example.com/art.jpg"
                    }
                }
            }
        };

        _ = _mockCardService.Setup( s => s.GetResult( cardId ) ).Returns( mockResult );

        // Act
        IActionResult result = _controller.GetOEmbed( cardUrl, null, null, null );

        // Assert
        result.Should( ).BeOfType<OkObjectResult>( );
        OkObjectResult okResult = (OkObjectResult)result;

        OEmbedResponse? oembedResponse = okResult.Value as OEmbedResponse;
        oembedResponse.Should( ).NotBeNull( );
        oembedResponse!.Html.Should( ).NotContain( "\"Song\"" ); // Should be escaped
        oembedResponse.Html.Should( ).NotContain( "<special>" ); // Should be escaped
        // Verify that special characters are not present in their unescaped form
        string html = oembedResponse.Html;
        Assert.IsFalse( html.Contains( "<special>", StringComparison.Ordinal ), "HTML should not contain unescaped angle brackets" );
        Assert.IsFalse( html.Contains( "\"Song\"", StringComparison.Ordinal ), "HTML should not contain unescaped quotes" );
        Assert.IsFalse( html.Contains( "&", StringComparison.Ordinal ) && !html.Contains( "&quot;", StringComparison.Ordinal ) && !html.Contains( "&lt;", StringComparison.Ordinal ) && !html.Contains( "&gt;", StringComparison.Ordinal ) && !html.Contains( "&amp;", StringComparison.Ordinal ), "HTML should have proper entity encoding" );
    }

    [TestMethod]
    public void GetOEmbed_WithExtremelyLargeDimensions_ClampsToMaximum( ) {
        // Arrange
        const string cardId = "test-card-123";
        const string cardUrl = "https://bridgebeats.link/card/test-card-123";
        const int extremeWidth = 5000;
        const int extremeHeight = 5000;

        MediaLinkResult mockResult = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResultDto> {
                {
                    SupportedProviders.Spotify,
                    new MusicLookupResultDto {
                        Title = "Test Song",
                        Artist = "Test Artist",
                        URL = "https://open.spotify.com/track/123",
                        ArtUrl = "https://example.com/art.jpg"
                    }
                }
            }
        };

        _ = _mockCardService.Setup( s => s.GetResult( cardId ) ).Returns( mockResult );

        // Act
        IActionResult result = _controller.GetOEmbed( cardUrl, extremeWidth, extremeHeight, null );

        // Assert
        result.Should( ).BeOfType<OkObjectResult>( );
        OkObjectResult okResult = (OkObjectResult)result;

        OEmbedResponse? oembedResponse = okResult.Value as OEmbedResponse;
        oembedResponse.Should( ).NotBeNull( );
        Assert.IsTrue( oembedResponse!.Width <= 1000 );
        Assert.IsTrue( oembedResponse.Height <= 600 );
    }

    [TestMethod]
    public void GetOEmbed_WithTooSmallDimensions_ClampsToMinimum( ) {
        // Arrange
        const string cardId = "test-card-123";
        const string cardUrl = "https://bridgebeats.link/card/test-card-123";
        const int tinyWidth = 50;
        const int tinyHeight = 50;

        MediaLinkResult mockResult = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResultDto> {
                {
                    SupportedProviders.Spotify,
                    new MusicLookupResultDto {
                        Title = "Test Song",
                        Artist = "Test Artist",
                        URL = "https://open.spotify.com/track/123",
                        ArtUrl = "https://example.com/art.jpg"
                    }
                }
            }
        };

        _ = _mockCardService.Setup( s => s.GetResult( cardId ) ).Returns( mockResult );

        // Act
        IActionResult result = _controller.GetOEmbed( cardUrl, tinyWidth, tinyHeight, null );

        // Assert
        result.Should( ).BeOfType<OkObjectResult>( );
        OkObjectResult okResult = (OkObjectResult)result;

        OEmbedResponse? oembedResponse = okResult.Value as OEmbedResponse;
        oembedResponse.Should( ).NotBeNull( );
        Assert.IsTrue( oembedResponse!.Width >= 200 );
        Assert.IsTrue( oembedResponse.Height >= 150 );
    }
}
