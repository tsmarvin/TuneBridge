using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Interfaces;
using TuneBridge.Domain.Types.Enums;
using TuneBridge.Web.Controllers;

namespace TuneBridge.Tests.Unit;

/// <summary>
/// Unit tests for the OEmbedController.
/// </summary>
[TestClass]
public class OEmbedControllerTests {

    private Mock<IOpenGraphCardService> _mockCardService = null!;
    private OEmbedController _controller = null!;

    [TestInitialize]
    public void TestInitialize( ) {
        _mockCardService = new Mock<IOpenGraphCardService>( );
        _mockCardService.Setup( s => s.BaseUrl ).Returns( "tunebridge.media" );
        _controller = new OEmbedController( _mockCardService.Object );
    }

    [TestMethod]
    public void GetOEmbed_WithEmptyUrl_ReturnsBadRequest( ) {
        // Act
        IActionResult result = _controller.GetOEmbed( url: "" );

        // Assert
        Assert.IsInstanceOfType<BadRequestObjectResult>( result );
    }

    [TestMethod]
    public void GetOEmbed_WithNullUrl_ReturnsBadRequest( ) {
        // Act
        IActionResult result = _controller.GetOEmbed( url: null! );

        // Assert
        Assert.IsInstanceOfType<BadRequestObjectResult>( result );
    }

    [TestMethod]
    public void GetOEmbed_WithXmlFormat_ReturnsNotImplemented( ) {
        // Act
        IActionResult result = _controller.GetOEmbed(
            url: "https://tunebridge.media/card/abc123",
            format: "xml"
        );

        // Assert
        ObjectResult? objectResult = result as ObjectResult;
        Assert.IsNotNull( objectResult );
        Assert.AreEqual( StatusCodes.Status501NotImplemented, objectResult.StatusCode );
    }

    [TestMethod]
    public void GetOEmbed_WithInvalidCardUrl_ReturnsBadRequest( ) {
        // Act
        IActionResult result = _controller.GetOEmbed(
            url: "https://tunebridge.media/invalid/path"
        );

        // Assert
        Assert.IsInstanceOfType<BadRequestObjectResult>( result );
    }

    [TestMethod]
    public void GetOEmbed_WithNonExistentCard_ReturnsNotFound( ) {
        // Arrange
        _mockCardService
            .Setup( s => s.GetResult( It.IsAny<string>( ) ) )
            .Returns( (MediaLinkResult?)null );

        // Act
        IActionResult result = _controller.GetOEmbed(
            url: "https://tunebridge.media/card/nonexistent123"
        );

        // Assert
        Assert.IsInstanceOfType<NotFoundObjectResult>( result );
    }

    [TestMethod]
    public void GetOEmbed_WithValidCardUrl_ReturnsOEmbedResponse( ) {
        // Arrange
        MediaLinkResult mediaResult = CreateSampleMediaLinkResult( );
        _mockCardService
            .Setup( s => s.GetResult( "abc123" ) )
            .Returns( mediaResult );

        // Act
        IActionResult result = _controller.GetOEmbed(
            url: "https://tunebridge.media/card/abc123"
        );

        // Assert
        OkObjectResult? okResult = result as OkObjectResult;
        Assert.IsNotNull( okResult );

        OEmbedResponse? response = okResult.Value as OEmbedResponse;
        Assert.IsNotNull( response );
        Assert.AreEqual( "rich", response.Type );
        Assert.AreEqual( "1.0", response.Version );
        Assert.AreEqual( "TuneBridge", response.ProviderName );
        Assert.AreEqual( "https://tunebridge.media", response.ProviderUrl );
        Assert.AreEqual( "Test Track", response.Title );
        Assert.AreEqual( "Test Artist", response.AuthorName );
    }

    [TestMethod]
    public void GetOEmbed_WithEmbedUrlPath_ExtractsCardIdCorrectly( ) {
        // Arrange
        MediaLinkResult mediaResult = CreateSampleMediaLinkResult( );
        _mockCardService
            .Setup( s => s.GetResult( "xyz789" ) )
            .Returns( mediaResult );

        // Act
        IActionResult result = _controller.GetOEmbed(
            url: "https://tunebridge.media/card/xyz789/embed"
        );

        // Assert
        OkObjectResult? okResult = result as OkObjectResult;
        Assert.IsNotNull( okResult );
        Assert.IsNotNull( okResult.Value as OEmbedResponse );
    }

    [TestMethod]
    public void GetOEmbed_WithMaxDimensions_RespectsMaxWidth( ) {
        // Arrange
        MediaLinkResult mediaResult = CreateSampleMediaLinkResult( );
        _mockCardService
            .Setup( s => s.GetResult( "abc123" ) )
            .Returns( mediaResult );

        // Act
        IActionResult result = _controller.GetOEmbed(
            url: "https://tunebridge.media/card/abc123",
            maxwidth: 300,
            maxheight: 200
        );

        // Assert
        OkObjectResult? okResult = result as OkObjectResult;
        Assert.IsNotNull( okResult );

        OEmbedResponse? response = okResult.Value as OEmbedResponse;
        Assert.IsNotNull( response );
        Assert.AreEqual( 300, response.Width );
        Assert.AreEqual( 200, response.Height );
    }

    [TestMethod]
    public void GetOEmbed_WithExcessiveMaxDimensions_CapsAtMaximum( ) {
        // Arrange
        MediaLinkResult mediaResult = CreateSampleMediaLinkResult( );
        _mockCardService
            .Setup( s => s.GetResult( "abc123" ) )
            .Returns( mediaResult );

        // Act
        IActionResult result = _controller.GetOEmbed(
            url: "https://tunebridge.media/card/abc123",
            maxwidth: 2000,
            maxheight: 1500
        );

        // Assert
        OkObjectResult? okResult = result as OkObjectResult;
        Assert.IsNotNull( okResult );

        OEmbedResponse? response = okResult.Value as OEmbedResponse;
        Assert.IsNotNull( response );
        // Should be capped at maximum values (800x600)
        Assert.AreEqual( 800, response.Width );
        Assert.AreEqual( 600, response.Height );
    }

    [TestMethod]
    public void GetOEmbed_ResponseContainsIframeHtml( ) {
        // Arrange
        MediaLinkResult mediaResult = CreateSampleMediaLinkResult( );
        _mockCardService
            .Setup( s => s.GetResult( "abc123" ) )
            .Returns( mediaResult );

        // Act
        IActionResult result = _controller.GetOEmbed(
            url: "https://tunebridge.media/card/abc123"
        );

        // Assert
        OkObjectResult? okResult = result as OkObjectResult;
        Assert.IsNotNull( okResult );

        OEmbedResponse? response = okResult.Value as OEmbedResponse;
        Assert.IsNotNull( response );
        Assert.IsNotNull( response.Html );
        Assert.Contains( "<iframe", response.Html, "HTML should contain an iframe tag" );
        Assert.Contains( "abc123/embed", response.Html, "HTML should reference the embed URL" );
    }

    [TestMethod]
    public void GetOEmbed_WithUrlEncodedCardUrl_ExtractsCardIdCorrectly( ) {
        // Arrange
        MediaLinkResult mediaResult = CreateSampleMediaLinkResult( );
        _mockCardService
            .Setup( s => s.GetResult( "abc123" ) )
            .Returns( mediaResult );

        // URL-encoded version of https://tunebridge.media/card/abc123
        string encodedUrl = "https%3A%2F%2Ftunebridge.media%2Fcard%2Fabc123";

        // Act
        IActionResult result = _controller.GetOEmbed( url: encodedUrl );

        // Assert
        OkObjectResult? okResult = result as OkObjectResult;
        Assert.IsNotNull( okResult );
        Assert.IsNotNull( okResult.Value as OEmbedResponse );
    }

    [TestMethod]
    public void GetOEmbed_ResponseIncludesThumbnailWhenArtworkAvailable( ) {
        // Arrange
        MediaLinkResult mediaResult = CreateSampleMediaLinkResult( );
        _mockCardService
            .Setup( s => s.GetResult( "abc123" ) )
            .Returns( mediaResult );

        // Act
        IActionResult result = _controller.GetOEmbed(
            url: "https://tunebridge.media/card/abc123"
        );

        // Assert
        OkObjectResult? okResult = result as OkObjectResult;
        Assert.IsNotNull( okResult );

        OEmbedResponse? response = okResult.Value as OEmbedResponse;
        Assert.IsNotNull( response );
        Assert.AreEqual( "https://example.com/artwork.jpg", response.ThumbnailUrl );
        Assert.AreEqual( 640, response.ThumbnailWidth );
        Assert.AreEqual( 640, response.ThumbnailHeight );
    }

    [TestMethod]
    public void GetOEmbed_ResponseIncludesCacheAge( ) {
        // Arrange
        MediaLinkResult mediaResult = CreateSampleMediaLinkResult( );
        _mockCardService
            .Setup( s => s.GetResult( "abc123" ) )
            .Returns( mediaResult );

        // Act
        IActionResult result = _controller.GetOEmbed(
            url: "https://tunebridge.media/card/abc123"
        );

        // Assert
        OkObjectResult? okResult = result as OkObjectResult;
        Assert.IsNotNull( okResult );

        OEmbedResponse? response = okResult.Value as OEmbedResponse;
        Assert.IsNotNull( response );
        // Cache age should be 6 days in seconds (518400)
        Assert.AreEqual( 518400, response.CacheAge );
    }

    /// <summary>
    /// Helper method to create a sample MediaLinkResult for testing.
    /// </summary>
    private static MediaLinkResult CreateSampleMediaLinkResult( ) {
        return new MediaLinkResult {
            Results = new Dictionary<SupportedProviders, MusicLookupResultDto> {
                {
                    SupportedProviders.Spotify, new MusicLookupResultDto {
                        Title = "Test Track",
                        Artist = "Test Artist",
                        URL = "https://open.spotify.com/track/123",
                        ArtUrl = "https://example.com/artwork.jpg",
                        ExternalId = "USRC17607839",
                        IsAlbum = false,
                        IsPrimary = true
                    }
                }
            }
        };
    }
}
