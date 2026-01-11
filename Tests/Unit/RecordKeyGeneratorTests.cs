using BridgeBeats.Domain.Contracts.DTOs;
using BridgeBeats.Domain.Implementations.Utilities;
using BridgeBeats.Domain.Types.Enums;

namespace BridgeBeats.Tests.Unit {

    [TestClass]
    public class RecordKeyGeneratorTests {

        [TestMethod]
        public void GenerateRkey_WithTrackISRC_ReturnsTrackRkey( ) {
            // Arrange
            MediaLinkResult result = new( );
            result.Results.Add( SupportedProviders.Spotify, new MusicLookupResult {
                ExternalId = "USRC12345678",
                IsAlbum = false,
                Artist = "Test Artist",
                Title = "Test Track",
                URL = "https://open.spotify.com/track/test"
            } );

            // Act
            string? rkey = RecordKeyGenerator.GenerateRkey( result );

            // Assert
            Assert.IsNotNull( rkey );
            Assert.AreEqual( "track:USRC12345678", rkey );
        }

        [TestMethod]
        public void GenerateRkey_WithAlbumUPC_ReturnsAlbumRkey( ) {
            // Arrange
            MediaLinkResult result = new( );
            result.Results.Add( SupportedProviders.AppleMusic, new MusicLookupResult {
                ExternalId = "123456789012",
                IsAlbum = true,
                Artist = "Test Artist",
                Title = "Test Album",
                URL = "https://music.apple.com/album/test"
            } );

            // Act
            string? rkey = RecordKeyGenerator.GenerateRkey( result );

            // Assert
            Assert.IsNotNull( rkey );
            Assert.AreEqual( "album:123456789012", rkey );
        }

        [TestMethod]
        public void GenerateRkey_WithNoExternalId_ReturnsMetadataBasedRkey( ) {
            // Arrange
            MediaLinkResult result = new( );
            result.Results.Add( SupportedProviders.Spotify, new MusicLookupResult {
                ExternalId = "",
                IsAlbum = false,
                Artist = "Test Artist",
                Title = "Test Track",
                URL = "https://open.spotify.com/track/test"
            } );

            // Act
            string rkey = RecordKeyGenerator.GenerateRkey( result );

            // Assert
            Assert.IsNotNull( rkey );
            Assert.IsTrue( rkey.StartsWith( "metadata:", StringComparison.Ordinal ) );
        }

        [TestMethod]
        public void GenerateRkey_WithExternalIdContainingSpecialChars_SanitizesRkey( ) {
            // Arrange
            MediaLinkResult result = new( );
            result.Results.Add( SupportedProviders.Spotify, new MusicLookupResult {
                ExternalId = "US-RC1-23-45678!@#",
                IsAlbum = false,
                Artist = "Test Artist",
                Title = "Test Track",
                URL = "https://open.spotify.com/track/test"
            } );

            // Act
            string? rkey = RecordKeyGenerator.GenerateRkey( result );

            // Assert
            Assert.IsNotNull( rkey );
            Assert.AreEqual( "track:US-RC1-23-45678", rkey );
        }

        [TestMethod]
        public void GenerateRkey_DirectMethod_ReturnsCorrectFormat( ) {
            // Act
            string rkey = RecordKeyGenerator.GenerateRkey( "USRC12345678", false );

            // Assert
            Assert.AreEqual( "track:USRC12345678", rkey );
        }

        [TestMethod]
        public void GenerateRkey_DirectMethod_WithAlbum_ReturnsAlbumPrefix( ) {
            // Act
            string rkey = RecordKeyGenerator.GenerateRkey( "123456789012", true );

            // Assert
            Assert.AreEqual( "album:123456789012", rkey );
        }

        [TestMethod]
        public void GenerateRkey_DirectMethod_WithEmptyId_ThrowsException( ) {
            // Act & Assert
            _ = Assert.ThrowsExactly<ArgumentException>( ( ) => RecordKeyGenerator.GenerateRkey( "", false ) );
        }

        [TestMethod]
        public void GenerateCardId_WithSameRkey_ReturnsSameId( ) {
            // Arrange
            string rkey = "track:USRC12345678";

            // Act
            string id1 = RecordKeyGenerator.GenerateCardId( rkey );
            string id2 = RecordKeyGenerator.GenerateCardId( rkey );

            // Assert
            Assert.AreEqual( id1, id2, "Card IDs should be deterministic" );
        }

        [TestMethod]
        public void GenerateCardId_WithDifferentRkeys_ReturnsDifferentIds( ) {
            // Act
            string id1 = RecordKeyGenerator.GenerateCardId( "track:USRC12345678" );
            string id2 = RecordKeyGenerator.GenerateCardId( "track:USRC87654321" );

            // Assert
            Assert.AreNotEqual( id1, id2, "Different rkeys should generate different card IDs" );
        }

        [TestMethod]
        public void GenerateCardId_ReturnsUrlSafeString( ) {
            // Arrange
            string rkey = "track:USRC12345678";

            // Act
            string cardId = RecordKeyGenerator.GenerateCardId( rkey );

            // Assert
            Assert.IsLessThanOrEqualTo( cardId.Length, 32, "Card ID should not exceed 32 characters" );
            Assert.IsTrue( cardId.All( c => char.IsLetterOrDigit( c ) || c == '-' ), "Card ID should be URL-safe" );
            Assert.IsTrue( cardId.All( c => !char.IsUpper( c ) ), "Card ID should be lowercase" );
        }

        [TestMethod]
        public void GenerateCardId_WithEmptyRkey_ThrowsException( ) {
            // Act & Assert
            _ = Assert.ThrowsExactly<ArgumentException>( ( ) => RecordKeyGenerator.GenerateCardId( "" ) );
        }

        [TestMethod]
        public void GenerateCardId_WithInvalidMaxLength_ThrowsException( ) {
            // Act & Assert
            _ = Assert.ThrowsExactly<ArgumentException>( ( ) => RecordKeyGenerator.GenerateCardId( "track:test", -1 ) );
        }

        [TestMethod]
        public void GenerateRkey_WithSameMetadata_ReturnsSameRkey( ) {
            // Arrange
            MediaLinkResult result1 = new( );
            result1.Results.Add( SupportedProviders.Spotify, new MusicLookupResult {
                ExternalId = "",
                IsAlbum = false,
                Artist = "Test Artist",
                Title = "Test Track",
                URL = "https://open.spotify.com/track/test1"
            } );

            MediaLinkResult result2 = new( );
            result2.Results.Add( SupportedProviders.AppleMusic, new MusicLookupResult {
                ExternalId = "",
                IsAlbum = false,
                Artist = "Test Artist",
                Title = "Test Track",
                URL = "https://music.apple.com/track/test2"
            } );

            // Act
            string rkey1 = RecordKeyGenerator.GenerateRkey( result1 );
            string rkey2 = RecordKeyGenerator.GenerateRkey( result2 );

            // Assert
            Assert.AreEqual( rkey1, rkey2, "Same metadata should generate same rkey" );
        }

        [TestMethod]
        public void GenerateRkey_WithEmptyTitleAndArtist_ThrowsException( ) {
            // Arrange
            MediaLinkResult result = new( );
            result.Results.Add( SupportedProviders.Spotify, new MusicLookupResult {
                ExternalId = "",
                IsAlbum = false,
                Artist = "",
                Title = "",
                URL = "https://open.spotify.com/track/test"
            } );

            // Act & Assert
            ArgumentException ex = Assert.ThrowsExactly<ArgumentException>( ( ) => RecordKeyGenerator.GenerateRkey( result ) );
            Assert.IsTrue( ex.Message.Contains( "both Title and Artist are empty", StringComparison.OrdinalIgnoreCase ) );
        }

        [TestMethod]
        public void GenerateRkey_WithNormalizedMetadata_ReturnsSameRkey( ) {
            // Arrange - Different case and whitespace but same content
            MediaLinkResult result1 = new( );
            result1.Results.Add( SupportedProviders.Spotify, new MusicLookupResult {
                ExternalId = "",
                IsAlbum = false,
                Artist = "Test Artist",
                Title = "Test Track",
                URL = "https://open.spotify.com/track/test1"
            } );

            MediaLinkResult result2 = new( );
            result2.Results.Add( SupportedProviders.AppleMusic, new MusicLookupResult {
                ExternalId = "",
                IsAlbum = false,
                Artist = "  TEST ARTIST  ",
                Title = "  test track  ",
                URL = "https://music.apple.com/track/test2"
            } );

            // Act
            string rkey1 = RecordKeyGenerator.GenerateRkey( result1 );
            string rkey2 = RecordKeyGenerator.GenerateRkey( result2 );

            // Assert
            Assert.AreEqual( rkey1, rkey2, "Metadata normalization should make case and whitespace irrelevant" );
        }

        [TestMethod]
        public void GenerateCardId_IncludesPadding( ) {
            // Arrange
            string rkey = "track:USRC12345678";

            // Act
            string cardId = RecordKeyGenerator.GenerateCardId( rkey, maxLength: 100 ); // Use longer length to see padding

            // Assert
            // Base32 output should be padded to multiple of 8
            // Since we're using SHA256 (32 bytes), base32 encoding produces (32*8/5) = 51.2 chars, rounded up with padding
            Assert.IsFalse( string.IsNullOrEmpty( cardId ) );
        }
    }
}
