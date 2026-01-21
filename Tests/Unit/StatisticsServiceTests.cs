using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Services.Statistics;
using Microsoft.Extensions.Logging;
using Moq;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="StatisticsService"/> to verify statistics computation,
/// caching behavior, and refresh functionality.
/// </summary>
[TestClass]
public class StatisticsServiceTests {

    private Mock<IATProtoStorageService> _atProtoStorageMock = null!;
    private Mock<ILogger<StatisticsService>> _loggerMock = null!;
    private StatisticsSettings _settings = null!;

    private static readonly Uri s_testPdsUri = new( "https://pds.test.example" );
    private const string TestUserDid = "did:plc:testuser123";

    [TestInitialize]
    public void Initialize( ) {
        _atProtoStorageMock = new Mock<IATProtoStorageService>( );
        _loggerMock = new Mock<ILogger<StatisticsService>>( );
        _settings = new StatisticsSettings(
            s_testPdsUri,
            TestUserDid,
            TimeSpan.FromHours( 6 )
        );
    }

    #region Constructor Tests

    [TestMethod]
    public void Constructor_WithValidDependencies_ShouldCreateInstance( ) {
        // Act
        StatisticsService service = CreateService( );

        // Assert
        Assert.IsNotNull( service );
    }

    #endregion

    #region GetStatisticsAsync Tests

    [TestMethod]
    public async Task GetStatisticsAsync_WithEmptyCollection_ShouldReturnZeroStats( ) {
        // Arrange
        SetupEmptyRecordList( );
        StatisticsService service = CreateService( );

        // Act
        LookupStatistics stats = await service.GetStatisticsAsync( );

        // Assert
        Assert.AreEqual( 0, stats.TotalRecords );
        Assert.AreEqual( 0, stats.AlbumCount );
        Assert.AreEqual( 0, stats.TrackCount );
        Assert.IsEmpty( stats.ProviderCounts );
        Assert.IsEmpty( stats.RecentEntries );
        Assert.IsNull( stats.EarliestLookup );
        Assert.IsNull( stats.LatestLookup );
    }

    [TestMethod]
    public async Task GetStatisticsAsync_WithMixedRecords_ShouldComputeCorrectStats( ) {
        // Arrange
        List<(string AtUri, MediaLinkResult Result)> records = [
            CreateTrackRecord( "at://did:plc:test/link.bridgebeats.lookup/track:ISRC1", "Artist1", "Track1", DateTimeOffset.UtcNow.AddDays( -2 ) ),
            CreateTrackRecord( "at://did:plc:test/link.bridgebeats.lookup/track:ISRC2", "Artist2", "Track2", DateTimeOffset.UtcNow.AddDays( -1 ) ),
            CreateAlbumRecord( "at://did:plc:test/link.bridgebeats.lookup/album:UPC1", "Artist3", "Album1", DateTimeOffset.UtcNow )
        ];
        SetupRecordList( records );
        StatisticsService service = CreateService( );

        // Act
        LookupStatistics stats = await service.GetStatisticsAsync( );

        // Assert
        Assert.AreEqual( 3, stats.TotalRecords );
        Assert.AreEqual( 1, stats.AlbumCount );
        Assert.AreEqual( 2, stats.TrackCount );
    }

    [TestMethod]
    public async Task GetStatisticsAsync_ShouldCountProviders( ) {
        // Arrange
        List<(string AtUri, MediaLinkResult Result)> records = [
            CreateTrackRecordWithProviders( "at://test/1", [SupportedProviders.Spotify, SupportedProviders.AppleMusic] ),
            CreateTrackRecordWithProviders( "at://test/2", [SupportedProviders.Spotify, SupportedProviders.Tidal] ),
            CreateTrackRecordWithProviders( "at://test/3", [SupportedProviders.AppleMusic] )
        ];
        SetupRecordList( records );
        StatisticsService service = CreateService( );

        // Act
        LookupStatistics stats = await service.GetStatisticsAsync( );

        // Assert
        Assert.AreEqual( 2, stats.ProviderCounts["Spotify"] );
        Assert.AreEqual( 2, stats.ProviderCounts["AppleMusic"] );
        Assert.AreEqual( 1, stats.ProviderCounts["Tidal"] );
    }

    [TestMethod]
    public async Task GetStatisticsAsync_ShouldTrackDateRange( ) {
        // Arrange
        DateTimeOffset earliest = DateTimeOffset.UtcNow.AddDays( -30 );
        DateTimeOffset latest = DateTimeOffset.UtcNow;
        List<(string AtUri, MediaLinkResult Result)> records = [
            CreateTrackRecord( "at://test/1", "Artist1", "Track1", earliest ),
            CreateTrackRecord( "at://test/2", "Artist2", "Track2", DateTimeOffset.UtcNow.AddDays( -15 ) ),
            CreateTrackRecord( "at://test/3", "Artist3", "Track3", latest )
        ];
        SetupRecordList( records );
        StatisticsService service = CreateService( );

        // Act
        LookupStatistics stats = await service.GetStatisticsAsync( );

        // Assert
        Assert.IsNotNull( stats.EarliestLookup );
        Assert.IsNotNull( stats.LatestLookup );
        Assert.AreEqual( earliest, stats.EarliestLookup.Value );
        Assert.AreEqual( latest, stats.LatestLookup.Value );
    }

    [TestMethod]
    public async Task GetStatisticsAsync_ShouldReturnFiveMostRecentEntries( ) {
        // Arrange
        List<(string AtUri, MediaLinkResult Result)> records = [];
        for (int i = 0; i < 10; i++) {
            records.Add( CreateTrackRecord(
                $"at://test/{i}",
                $"Artist{i}",
                $"Track{i}",
                DateTimeOffset.UtcNow.AddHours( -i )
            ) );
        }
        SetupRecordList( records );
        StatisticsService service = CreateService( );

        // Act
        LookupStatistics stats = await service.GetStatisticsAsync( );

        // Assert
        Assert.HasCount( 5, stats.RecentEntries );
        Assert.AreEqual( "Track0", stats.RecentEntries[0].Title ); // Most recent
        Assert.AreEqual( "Track4", stats.RecentEntries[4].Title ); // 5th most recent
    }

    [TestMethod]
    public async Task GetStatisticsAsync_ShouldGenerateCardIdForRecentEntries( ) {
        // Arrange
        List<(string AtUri, MediaLinkResult Result)> records = [
            CreateTrackRecord( "at://did:plc:test/link.bridgebeats.lookup/track:ISRC12345", "Artist", "Track", DateTimeOffset.UtcNow )
        ];
        SetupRecordList( records );
        StatisticsService service = CreateService( );

        // Act
        LookupStatistics stats = await service.GetStatisticsAsync( );

        // Assert
        Assert.HasCount( 1, stats.RecentEntries );
        Assert.IsNotNull( stats.RecentEntries[0].CardId );
        Assert.IsNotEmpty( stats.RecentEntries[0].CardId! );
    }

    #endregion

    #region Caching Tests

    [TestMethod]
    public async Task GetStatisticsAsync_ShouldCacheResults( ) {
        // Arrange
        SetupEmptyRecordList( );
        StatisticsService service = CreateService( );

        // Act - Call twice
        _ = await service.GetStatisticsAsync( );
        _ = await service.GetStatisticsAsync( );

        // Assert - ListAllRecordsAsync should only be called once due to caching
        _atProtoStorageMock.Verify(
            x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Once( )
        );
    }

    [TestMethod]
    public async Task RefreshStatisticsAsync_ShouldBypassCache( ) {
        // Arrange
        SetupEmptyRecordList( );
        StatisticsService service = CreateService( );

        // Act - Get stats, then refresh
        _ = await service.GetStatisticsAsync( );
        _ = await service.RefreshStatisticsAsync( );

        // Assert - ListAllRecordsAsync should be called twice (initial + refresh)
        _atProtoStorageMock.Verify(
            x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Exactly( 2 )
        );
    }

    [TestMethod]
    public async Task GetStatisticsAsync_AfterCacheExpiry_ShouldRefetch( ) {
        // Arrange - Use very short cache duration
        StatisticsSettings shortCacheSettings = new(
            s_testPdsUri,
            TestUserDid,
            TimeSpan.FromMilliseconds( 1 )
        );
        SetupEmptyRecordList( );
        StatisticsService service = new(
            _atProtoStorageMock.Object,
            shortCacheSettings,
            _loggerMock.Object
        );

        // Act - Call, wait for expiry, call again
        _ = await service.GetStatisticsAsync( );
        await Task.Delay( 10 ); // Wait for cache to expire
        _ = await service.GetStatisticsAsync( );

        // Assert - Should be called twice due to cache expiry
        _atProtoStorageMock.Verify(
            x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Exactly( 2 )
        );
    }

    #endregion

    #region Cancellation Tests

    [TestMethod]
    public async Task GetStatisticsAsync_WhenCancelled_ShouldThrowOperationCanceledException( ) {
        // Arrange
        using CancellationTokenSource cts = new( );
        await cts.CancelAsync( );
        SetupEmptyRecordList( );
        StatisticsService service = CreateService( );

        // Act & Assert - TaskCanceledException is a subclass of OperationCanceledException
        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            async ( ) => await service.GetStatisticsAsync( cts.Token )
        );
    }

    #endregion

    #region Helper Methods

    private StatisticsService CreateService( ) {
        return new StatisticsService(
            _atProtoStorageMock.Object,
            _settings,
            _loggerMock.Object
        );
    }

    private void SetupEmptyRecordList( ) {
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( AsyncEnumerable.Empty<(string, MediaLinkResult)>( ) );
    }

    private void SetupRecordList( List<(string AtUri, MediaLinkResult Result)> records ) {
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( records.ToAsyncEnumerable( ) );
    }

    private static (string AtUri, MediaLinkResult Result) CreateTrackRecord(
        string atUri,
        string artist,
        string title,
        DateTimeOffset lookedUpAt
    ) {
        MediaLinkResult result = new( ) {
            LookedUpAt = lookedUpAt.UtcDateTime
        };
        result.Results.Add( SupportedProviders.Spotify, new MusicLookupResult {
            Artist = artist,
            Title = title,
            IsAlbum = false,
            ExternalId = "ISRC12345678",
            URL = "https://open.spotify.com/track/test"
        } );
        return (atUri, result);
    }

    private static (string AtUri, MediaLinkResult Result) CreateAlbumRecord(
        string atUri,
        string artist,
        string title,
        DateTimeOffset lookedUpAt
    ) {
        MediaLinkResult result = new( ) {
            LookedUpAt = lookedUpAt.UtcDateTime
        };
        result.Results.Add( SupportedProviders.Spotify, new MusicLookupResult {
            Artist = artist,
            Title = title,
            IsAlbum = true,
            ExternalId = "123456789012",
            URL = "https://open.spotify.com/album/test"
        } );
        return (atUri, result);
    }

    private static (string AtUri, MediaLinkResult Result) CreateTrackRecordWithProviders(
        string atUri,
        SupportedProviders[] providers
    ) {
        MediaLinkResult result = new( ) {
            LookedUpAt = DateTime.UtcNow
        };
        foreach (SupportedProviders provider in providers) {
            result.Results.Add( provider, new MusicLookupResult {
                Artist = "Test Artist",
                Title = "Test Track",
                IsAlbum = false,
                ExternalId = "ISRC12345678",
                URL = $"https://{provider}.example/track/test"
            } );
        }
        return (atUri, result);
    }

    #endregion
}
