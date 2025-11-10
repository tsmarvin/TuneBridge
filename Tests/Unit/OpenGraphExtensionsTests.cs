using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Implementations.Extensions;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Tests.Unit;

[TestClass]
public class OpenGraphExtensionsTests {

    [TestMethod]
    public void ToOpenGraphMetadata_WithTrackAndISRC_IncludesISRCInDescription( ) {
        // Arrange
        MediaLinkResult result = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResultDto> {
                {
                    SupportedProviders.AppleMusic, new MusicLookupResultDto {
                        Title = "Chiron",
                        Artist = "Shades, Alix Perez & Eprom",
                        ExternalId = "US25X1087647",
                        ArtUrl = "https://example.com/art.jpg",
                        URL = "https://music.apple.com/us/album/chiron/1695231829",
                        IsAlbum = false,
                        IsPrimary = true
                    }
                }
            }
        };

        // Act
        Dictionary<string, string> metadata = result.ToOpenGraphMetadata( );

        // Assert
        Assert.IsNotNull( metadata );
        Assert.IsTrue( metadata.ContainsKey( "og:description" ) );
        Assert.IsTrue( metadata["og:description"].Contains( "ISRC: US25X1087647" ), "Description should contain ISRC" );
        Assert.IsTrue( metadata["og:description"].Contains( "Artist: Shades, Alix Perez & Eprom" ), "Description should contain artist" );
    }

    [TestMethod]
    public void ToOpenGraphMetadata_WithAlbumAndUPC_IncludesUPCInDescription( ) {
        // Arrange
        MediaLinkResult result = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResultDto> {
                {
                    SupportedProviders.Spotify, new MusicLookupResultDto {
                        Title = "Album Title",
                        Artist = "Test Artist",
                        ExternalId = "123456789012",
                        ArtUrl = "https://example.com/art.jpg",
                        URL = "https://open.spotify.com/album/abc123",
                        IsAlbum = true,
                        IsPrimary = true
                    }
                }
            }
        };

        // Act
        Dictionary<string, string> metadata = result.ToOpenGraphMetadata( );

        // Assert
        Assert.IsNotNull( metadata );
        Assert.IsTrue( metadata.ContainsKey( "og:description" ) );
        Assert.IsTrue( metadata["og:description"].Contains( "UPC: 123456789012" ), "Description should contain UPC for album" );
    }

    [TestMethod]
    public void ToOpenGraphMetadata_WithAppleMusicPrimary_HasBlueThemeColor( ) {
        // Arrange
        MediaLinkResult result = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResultDto> {
                {
                    SupportedProviders.AppleMusic, new MusicLookupResultDto {
                        Title = "Test Track",
                        Artist = "Test Artist",
                        ExternalId = "TESTISRC123",
                        ArtUrl = "https://example.com/art.jpg",
                        URL = "https://music.apple.com/us/album/test/123",
                        IsAlbum = false,
                        IsPrimary = true
                    }
                }
            }
        };

        // Act
        Dictionary<string, string> metadata = result.ToOpenGraphMetadata( );

        // Assert
        Assert.IsNotNull( metadata );
        Assert.IsTrue( metadata.ContainsKey( "theme-color" ) );
        Assert.AreEqual( "#0000FF", metadata["theme-color"], "Apple Music should have blue theme color" );
    }

    [TestMethod]
    public void ToOpenGraphMetadata_WithSpotifyPrimary_HasGreenThemeColor( ) {
        // Arrange
        MediaLinkResult result = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResultDto> {
                {
                    SupportedProviders.Spotify, new MusicLookupResultDto {
                        Title = "Test Track",
                        Artist = "Test Artist",
                        ExternalId = "TESTISRC123",
                        ArtUrl = "https://example.com/art.jpg",
                        URL = "https://open.spotify.com/track/abc123",
                        IsAlbum = false,
                        IsPrimary = true
                    }
                }
            }
        };

        // Act
        Dictionary<string, string> metadata = result.ToOpenGraphMetadata( );

        // Assert
        Assert.IsNotNull( metadata );
        Assert.IsTrue( metadata.ContainsKey( "theme-color" ) );
        Assert.AreEqual( "#1DB954", metadata["theme-color"], "Spotify should have green theme color" );
    }

    [TestMethod]
    public void ToOpenGraphMetadata_WithoutExternalId_DoesNotIncludeISRCOrUPC( ) {
        // Arrange
        MediaLinkResult result = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResultDto> {
                {
                    SupportedProviders.AppleMusic, new MusicLookupResultDto {
                        Title = "Test Track",
                        Artist = "Test Artist",
                        ExternalId = string.Empty,
                        ArtUrl = "https://example.com/art.jpg",
                        URL = "https://music.apple.com/us/album/test/123",
                        IsAlbum = false,
                        IsPrimary = true
                    }
                }
            }
        };

        // Act
        Dictionary<string, string> metadata = result.ToOpenGraphMetadata( );

        // Assert
        Assert.IsNotNull( metadata );
        Assert.IsTrue( metadata.ContainsKey( "og:description" ) );
        Assert.IsFalse( metadata["og:description"].Contains( "ISRC:" ), "Description should not contain ISRC when not available" );
        Assert.IsFalse( metadata["og:description"].Contains( "UPC:" ), "Description should not contain UPC when not available" );
        Assert.IsTrue( metadata["og:description"].Contains( "Artist: Test Artist" ), "Description should still contain artist" );
    }

    [TestMethod]
    public void ToOpenGraphMetadata_DoesNotIncludeProviderLinksInDescription( ) {
        // Arrange
        MediaLinkResult result = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResultDto> {
                {
                    SupportedProviders.AppleMusic, new MusicLookupResultDto {
                        Title = "Test Track",
                        Artist = "Test Artist",
                        ExternalId = "TESTISRC123",
                        ArtUrl = "https://example.com/art.jpg",
                        URL = "https://music.apple.com/us/album/test/123",
                        IsAlbum = false,
                        IsPrimary = true
                    }
                },
                {
                    SupportedProviders.Spotify, new MusicLookupResultDto {
                        Title = "Test Track",
                        Artist = "Test Artist",
                        ExternalId = "TESTISRC123",
                        ArtUrl = "https://example.com/art.jpg",
                        URL = "https://open.spotify.com/track/abc123",
                        IsAlbum = false,
                        IsPrimary = false
                    }
                }
            }
        };

        // Act
        Dictionary<string, string> metadata = result.ToOpenGraphMetadata( );

        // Assert
        Assert.IsNotNull( metadata );
        Assert.IsTrue( metadata.ContainsKey( "og:description" ) );
        Assert.IsFalse( metadata["og:description"].Contains( "Available on:" ), "Description should not contain provider links section" );
        Assert.IsFalse( metadata["og:description"].Contains( "https://" ), "Description should not contain URLs" );
    }
}
