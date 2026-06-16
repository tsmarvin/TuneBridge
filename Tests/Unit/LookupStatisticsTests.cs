using BridgeBeats.Contracts.DTOs;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests the statistics projection DTOs <see cref="LookupStatistics"/> and <see cref="RecentLookupEntry"/>:
/// their default values, object-initializer assignment, and the <c>WithBootstrapStatus</c> copy that swaps in a
/// new cache-bootstrap status while carrying every other property forward.
/// </summary>
[TestClass]
public class LookupStatisticsTests {

    #region LookupStatistics Tests

    /// <summary>
    /// Verifies a default <see cref="LookupStatistics"/> has zeroed counts, empty (non-null) collections, and null
    /// earliest/latest timestamps.
    /// </summary>
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

    /// <summary>
    /// Verifies an object initializer sets every <see cref="LookupStatistics"/> property, including the provider-count
    /// dictionary, recent entries, and the earliest/latest/generated timestamps.
    /// </summary>
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

    /// <summary>
    /// Verifies <see cref="LookupStatistics.WithBootstrapStatus"/> returns a copy carrying every property forward
    /// unchanged except <c>CacheBootstrapStatus</c>, which becomes the supplied new status instance. Uses reflection
    /// to compare all public properties so a newly added property cannot silently be dropped.
    /// </summary>
    [TestMethod]
    public void WithBootstrapStatus_CarriesForwardAllProperties_ReflectionRoundTrip( ) {
        // Arrange: build a source with every property set to a non-default value
        DateTimeOffset t = new( 2024, 1, 1, 0, 0, 0, TimeSpan.Zero );
        LookupStatistics source = new( ) {
            TotalRecords = 42,
            AlbumCount = 7,
            TrackCount = 35,
            ProviderCounts = new Dictionary<string, int> { ["Spotify"] = 10, ["AppleMusic"] = 5 },
            RecentEntries = [new RecentLookupEntry { Title = "T" }],
            EarliestLookup = t,
            LatestLookup = t.AddDays( 1 ),
            GeneratedAt = t.AddDays( 2 ),
            CacheBootstrapStatus = new CacheBootstrapStatus { IsRunning = true, LastSuccessCount = 1 }
        };

        CacheBootstrapStatus? newStatus = new( ) { IsRunning = false, LastSuccessCount = 99 };

        // Act
        LookupStatistics result = source.WithBootstrapStatus( newStatus );

        // Assert: every property except CacheBootstrapStatus matches the source
        System.Reflection.PropertyInfo[] props = typeof( LookupStatistics ).GetProperties(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance );

        foreach (System.Reflection.PropertyInfo prop in props) {
            object? sourceVal = prop.GetValue( source );
            object? resultVal = prop.GetValue( result );

            if (prop.Name == nameof( LookupStatistics.CacheBootstrapStatus )) {
                // Must be replaced with newStatus
                Assert.AreSame( newStatus, resultVal,
                    $"Property {prop.Name} was expected to be the new status object." );
            } else {
                // Must be carried forward unchanged
                Assert.AreEqual( sourceVal, resultVal,
                    $"Property {prop.Name} was not carried forward by WithBootstrapStatus." );
            }
        }
    }

    #region RecentLookupEntry Tests

    /// <summary>
    /// Verifies a default <see cref="RecentLookupEntry"/> has empty strings for AT-URI/artist/title, <c>IsAlbum</c>
    /// false, and null timestamp and card id.
    /// </summary>
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

    /// <summary>
    /// Verifies an object initializer sets every <see cref="RecentLookupEntry"/> property.
    /// </summary>
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

    /// <summary>
    /// Verifies <c>IsAlbum = false</c> marks the entry as a track.
    /// </summary>
    [TestMethod]
    public void RecentLookupEntry_IsAlbumFalse_ShouldIndicateTrack( ) {
        // Act
        RecentLookupEntry entry = new( ) { IsAlbum = false };

        // Assert
        Assert.IsFalse( entry.IsAlbum );
    }

    /// <summary>
    /// Verifies <c>IsAlbum = true</c> marks the entry as an album.
    /// </summary>
    [TestMethod]
    public void RecentLookupEntry_IsAlbumTrue_ShouldIndicateAlbum( ) {
        // Act
        RecentLookupEntry entry = new( ) { IsAlbum = true };

        // Assert
        Assert.IsTrue( entry.IsAlbum );
    }

    #endregion
}
