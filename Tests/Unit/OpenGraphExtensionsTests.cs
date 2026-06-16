using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Core.Domain.Extensions;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests the <c>ToOpenGraphMetadata</c> extension, which builds the OpenGraph meta-tag dictionary from a
/// <see cref="MediaLinkResult"/>. Covers labeling the primary result's external id as ISRC for tracks and UPC for
/// albums (and omitting it when absent), the per-provider <c>theme-color</c> chosen from the primary provider, and
/// that provider links/URLs are kept out of the description.
/// </summary>
[TestClass]
public class OpenGraphExtensionsTests {
    /// <summary>
    /// Verifies a track's external id is labeled <c>ISRC:</c> in the description, alongside the artist.
    /// </summary>
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

    /// <summary>
    /// Verifies an album's external id is labeled <c>UPC:</c> in the description.
    /// </summary>
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

    /// <summary>
    /// Verifies an Apple Music primary result yields the Apple Music red <c>theme-color</c> (<c>#D60017</c>).
    /// </summary>
    [TestMethod]
    public void ToOpenGraphMetadata_WithAppleMusicPrimary_HasRedThemeColor( ) {
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

    /// <summary>
    /// Verifies a Spotify primary result yields the Spotify green <c>theme-color</c> (<c>#1ED760</c>).
    /// </summary>
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

    /// <summary>
    /// Verifies that when the primary result has no external id, the description omits both <c>ISRC:</c> and
    /// <c>UPC:</c> but still includes the artist.
    /// </summary>
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

    /// <summary>
    /// Verifies the description never lists provider links: with multiple providers present it contains no
    /// "Available on:" section and no URLs.
    /// </summary>
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
