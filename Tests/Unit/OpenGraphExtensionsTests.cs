using BridgeBeats.Domain.Contracts.DTOs;
using BridgeBeats.Domain.Implementations.Extensions;
using BridgeBeats.Domain.Types.Enums;

namespace BridgeBeats.Tests.Unit;

[TestClass]
public class OpenGraphExtensionsTests {

    [TestMethod]
    public void ToOpenGraphMetadata_WithTrackAndISRC_IncludesISRCInDescription( ) {
        // Arrange
        MediaLinkResult result = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResult> {
                {
                    SupportedProviders.AppleMusic, new MusicLookupResult {
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
        Assert.Contains( "ISRC: US25X1087647", metadata["og:description"], "Description should contain ISRC" );
        Assert.Contains( "Artist: Shades, Alix Perez & Eprom", metadata["og:description"], "Description should contain artist" );
    }

    [TestMethod]
    public void ToOpenGraphMetadata_WithAlbumAndUPC_IncludesUPCInDescription( ) {
        // Arrange
        MediaLinkResult result = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResult> {
                {
                    SupportedProviders.Spotify, new MusicLookupResult {
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
        Assert.Contains( "UPC: 123456789012", metadata["og:description"], "Description should contain UPC for album" );
    }

    [TestMethod]
    public void ToOpenGraphMetadata_WithAppleMusicPrimary_HasBlueThemeColor( ) {
        // Arrange
        MediaLinkResult result = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResult> {
                {
                    SupportedProviders.AppleMusic, new MusicLookupResult {
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
        Assert.AreEqual( "#D60017", metadata["theme-color"], "Apple Music should have red theme color" );
    }

    [TestMethod]
    public void ToOpenGraphMetadata_WithSpotifyPrimary_HasGreenThemeColor( ) {
        // Arrange
        MediaLinkResult result = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResult> {
                {
                    SupportedProviders.Spotify, new MusicLookupResult {
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
        Assert.AreEqual( "#1ED760", metadata["theme-color"], "Spotify should have green theme color" );
    }

    [TestMethod]
    public void ToOpenGraphMetadata_WithoutExternalId_DoesNotIncludeISRCOrUPC( ) {
        // Arrange
        MediaLinkResult result = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResult> {
                {
                    SupportedProviders.AppleMusic, new MusicLookupResult {
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
        Assert.DoesNotContain( "ISRC:", metadata["og:description"], "Description should not contain ISRC when not available" );
        Assert.DoesNotContain( "UPC:", metadata["og:description"], "Description should not contain UPC when not available" );
        Assert.Contains( "Artist: Test Artist", metadata["og:description"], "Description should still contain artist" );
    }

    [TestMethod]
    public void ToOpenGraphMetadata_DoesNotIncludeProviderLinksInDescription( ) {
        // Arrange
        MediaLinkResult result = new( ) {
            Results = new Dictionary<SupportedProviders, MusicLookupResult> {
                {
                    SupportedProviders.AppleMusic, new MusicLookupResult {
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
                    SupportedProviders.Spotify, new MusicLookupResult {
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
        Assert.DoesNotContain( "Available on:", metadata["og:description"], "Description should not contain provider links section" );
        Assert.DoesNotContain( "https://", metadata["og:description"], "Description should not contain URLs" );
    }
}
