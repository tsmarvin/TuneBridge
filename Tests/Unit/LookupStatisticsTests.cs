using BridgeBeats.Contracts.DTOs;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="LookupStatistics"/> and related DTOs to verify
/// property initialization and default values.
/// </summary>
[TestClass]
public class LookupStatisticsTests {

    #region LookupStatistics Tests

    [TestMethod]
    public void LookupStatistics_DefaultValues_ShouldBeInitialized( ) {
        // Act
        LookupStatistics stats = new( );

        // Assert
        Assert.AreEqual( 0, stats.TotalRecords );
        Assert.AreEqual( 0, stats.AlbumCount );
        Assert.AreEqual( 0, stats.TrackCount );
        Assert.IsNotNull( stats.ProviderCounts );
        Assert.IsEmpty( stats.ProviderCounts );
        Assert.IsNotNull( stats.RecentEntries );
        Assert.IsEmpty( stats.RecentEntries );
        Assert.IsNull( stats.EarliestLookup );
        Assert.IsNull( stats.LatestLookup );
    }

    [TestMethod]
    public void LookupStatistics_WithInitializer_ShouldSetAllProperties( ) {
        // Arrange
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        LookupStatistics stats = new( ) {
            TotalRecords = 1000,
            AlbumCount = 250,
            TrackCount = 750,
            ProviderCounts = new Dictionary<string, int> {
                ["Spotify"] = 900,
                ["AppleMusic"] = 850
            },
            RecentEntries = [
                new RecentLookupEntry { Title = "Test" }
            ],
            EarliestLookup = now.AddDays( -30 ),
            LatestLookup = now,
            GeneratedAt = now
        };

        // Assert
        Assert.AreEqual( 1000, stats.TotalRecords );
        Assert.AreEqual( 250, stats.AlbumCount );
        Assert.AreEqual( 750, stats.TrackCount );
        Assert.HasCount( 2, stats.ProviderCounts );
        Assert.AreEqual( 900, stats.ProviderCounts["Spotify"] );
        Assert.HasCount( 1, stats.RecentEntries );
        Assert.AreEqual( now.AddDays( -30 ), stats.EarliestLookup );
        Assert.AreEqual( now, stats.LatestLookup );
        Assert.AreEqual( now, stats.GeneratedAt );
    }

    #endregion

    #region RecentLookupEntry Tests

    [TestMethod]
    public void RecentLookupEntry_DefaultValues_ShouldBeInitialized( ) {
        // Act
        RecentLookupEntry entry = new( );

        // Assert
        Assert.AreEqual( string.Empty, entry.AtUri );
        Assert.IsFalse( entry.IsAlbum );
        Assert.AreEqual( string.Empty, entry.Artist );
        Assert.AreEqual( string.Empty, entry.Title );
        Assert.IsNull( entry.LookedUpAt );
        Assert.IsNull( entry.CardId );
    }

    [TestMethod]
    public void RecentLookupEntry_WithInitializer_ShouldSetAllProperties( ) {
        // Arrange
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        RecentLookupEntry entry = new( ) {
            AtUri = "at://did:plc:test/link.bridgebeats.lookup/track:ISRC123",
            IsAlbum = true,
            Artist = "Test Artist",
            Title = "Test Album",
            LookedUpAt = now,
            CardId = "card123"
        };

        // Assert
        Assert.AreEqual( "at://did:plc:test/link.bridgebeats.lookup/track:ISRC123", entry.AtUri );
        Assert.IsTrue( entry.IsAlbum );
        Assert.AreEqual( "Test Artist", entry.Artist );
        Assert.AreEqual( "Test Album", entry.Title );
        Assert.AreEqual( now, entry.LookedUpAt );
        Assert.AreEqual( "card123", entry.CardId );
    }

    [TestMethod]
    public void RecentLookupEntry_IsAlbumFalse_ShouldIndicateTrack( ) {
        // Act
        RecentLookupEntry entry = new( ) { IsAlbum = false };

        // Assert
        Assert.IsFalse( entry.IsAlbum );
    }

    [TestMethod]
    public void RecentLookupEntry_IsAlbumTrue_ShouldIndicateAlbum( ) {
        // Act
        RecentLookupEntry entry = new( ) { IsAlbum = true };

        // Assert
        Assert.IsTrue( entry.IsAlbum );
    }

    #endregion
}
