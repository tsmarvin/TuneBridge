using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Worker.Discord;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for Discord worker service extensions and configuration.
/// </summary>
[TestClass]
public class DiscordWorkerTests {
    [TestMethod]
    public void DiscordNodeConfig_Constructor_InitializesCorrectly( ) {
        // Arrange
        Mock<IMediaLinkService> mockLinkService = new( );
        int nodeNumber = 5;

        // Act
        DiscordNodeConfig config = new( mockLinkService.Object, nodeNumber );

        // Assert
        Assert.AreEqual( mockLinkService.Object, config.LinkLookupService );
        Assert.AreEqual( nodeNumber, config.NodeNumber );
    }

    [TestMethod]
    public void DiscordNodeConfig_WithZeroNodeNumber_IsValid( ) {
        // Arrange
        Mock<IMediaLinkService> mockLinkService = new( );
        int nodeNumber = 0;

        // Act
        DiscordNodeConfig config = new( mockLinkService.Object, nodeNumber );

        // Assert
        Assert.AreEqual( nodeNumber, config.NodeNumber );
    }

    [TestMethod]
    public void DiscordServiceExtensions_AddDiscordServices_RegistersServices( ) {
        // Arrange
        ServiceCollection services = new( );
        Mock<IMediaLinkService> mockLinkService = new( );
        _ = services.AddSingleton( mockLinkService.Object );
        
        string discordToken = "test_discord_token_here_1234567890";
        int nodeNumber = 3;

        // Act
        _ = services.AddDiscordServices( discordToken, nodeNumber );

        // Assert
        ServiceProvider provider = services.BuildServiceProvider( );
        DiscordNodeConfig? config = provider.GetService<DiscordNodeConfig>( );
        
        Assert.IsNotNull( config );
        Assert.AreEqual( nodeNumber, config.NodeNumber );
        Assert.AreEqual( mockLinkService.Object, config.LinkLookupService );
    }

    [TestMethod]
    public void MessageCreateGatewayHandler_CombinedInputLinksRegexEscaped_EscapesSpecialCharacters( ) {
        // Arrange
        List<string> inputLinks = [
            "https://open.spotify.com/track/abc123",
            "https://music.apple.com/us/album/test/123?i=456"
        ];

        // Act - Using reflection to call private method for testing
        System.Reflection.MethodInfo? method = typeof( MessageCreateGatewayHandler ).GetMethod(
            "CombinedInputLinksRegexEscaped",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        );
        
        string? result = method?.Invoke( null, new object[] { inputLinks } ) as string;

        // Assert
        Assert.IsNotNull( result );
        Assert.IsTrue( result.Contains( @"https://open\.spotify\.com/track/abc123" ) );
        Assert.IsTrue( result.Contains( @"\s*" ) );
    }
}
