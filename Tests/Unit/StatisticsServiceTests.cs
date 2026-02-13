using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Domain.Services;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Unit;

[TestClass]
public class StatisticsServiceTests {

    private Mock<IATProtoStorageService> _atProtoStorageMock = null!;
    private Mock<IConnectionMultiplexer> _redisMock = null!;
    private Mock<IDatabase> _redisDatabaseMock = null!;
    private Mock<ILogger<StatisticsService>> _loggerMock = null!;
    private StatisticsSettings _settings = null!;

    private static readonly Uri s_testPdsUri = new( "https://pds.test.example" );
    private const string TestUserDid = "did:plc:testuser123";

    [TestInitialize]
    public void Initialize( ) {
        _atProtoStorageMock = new Mock<IATProtoStorageService>( );
        _redisMock = new Mock<IConnectionMultiplexer>( );
        _redisDatabaseMock = new Mock<IDatabase>( );
        _ = _redisMock.Setup( x => x.GetDatabase( It.IsAny<int>( ), It.IsAny<object>( ) ) ).Returns( _redisDatabaseMock.Object );
        _ = _redisDatabaseMock.Setup( x => x.StringGetAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) ).ReturnsAsync( RedisValue.Null );
        _loggerMock = new Mock<ILogger<StatisticsService>>( );
        _settings = new StatisticsSettings(
            s_testPdsUri,
            TestUserDid,
            TimeSpan.FromHours( 6 ),
            TimeSpan.FromSeconds( 30 )
        );
    }

    [TestMethod]
    public void GetCachedStatistics_WhenNoCachedData_ReturnsNull( ) {
        StatisticsService service = CreateService( );

        LookupStatistics? result = service.GetCachedStatistics();

        Assert.IsNull( result );
    }

    [TestMethod]
    public async Task GetCachedStatistics_AfterRefresh_ReturnsCachedData( ) {
        SetupEmptyRecordList( );
        StatisticsService service = CreateService( );

        _ = await service.RefreshStatisticsAsync( CancellationToken.None );
        LookupStatistics? result = service.GetCachedStatistics();

        Assert.IsNotNull( result );
        Assert.AreEqual( 0, result.TotalRecords );
    }

    [TestMethod]
    public async Task GetStatisticsAsync_WhenNoCache_ReturnsEmptyStatsWithoutComputation( ) {
        StatisticsService service = CreateService( );

        LookupStatistics result = await service.GetStatisticsAsync(CancellationToken.None);

        Assert.AreEqual( 0, result.TotalRecords );
        Assert.AreEqual( DateTimeOffset.MinValue, result.GeneratedAt );
        _atProtoStorageMock.Verify(
            x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never( )
        );
    }

    [TestMethod]
    public async Task GetStatisticsAsync_ReturnsCachedData_NeverTriggersComputation( ) {
        SetupRecordList( [
            CreateTrackRecord( "at://did:plc:test/link.bridgebeats.lookup/track:ISRC1", "Artist1", "Track1", DateTimeOffset.UtcNow )
        ] );
        StatisticsService service = CreateService( );
        _ = await service.RefreshStatisticsAsync( CancellationToken.None );

        LookupStatistics result = await service.GetStatisticsAsync(CancellationToken.None);

        Assert.AreEqual( 1, result.TotalRecords );
        _atProtoStorageMock.Verify(
            x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Once( )
        );
    }

    [TestMethod]
    public void TriggerRefresh_WhenNotRefreshing_StartsRefresh( ) {
        StatisticsService service = CreateService( );

        bool result = service.TriggerRefresh();

        Assert.IsTrue( result );
    }

    [TestMethod]
    public async Task TriggerRefresh_WhenAlreadyRefreshing_ReturnsFalse( ) {
        TaskCompletionSource<bool> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( BlockingSequence( gate.Task ) );

        StatisticsService service = CreateService( );

        Task<LookupStatistics> refreshTask = service.RefreshStatisticsAsync(CancellationToken.None);

        await WaitUntilAsync( ( ) => service.IsRefreshing, TimeSpan.FromSeconds( 2 ) );
        bool triggerResult = service.TriggerRefresh();

        Assert.IsFalse( triggerResult );
        _ = gate.TrySetResult( true );
        _ = await refreshTask;
    }

    [TestMethod]
    public async Task IsRefreshing_AfterRefresh_ReturnsFalse( ) {
        SetupEmptyRecordList( );
        StatisticsService service = CreateService( );

        _ = await service.RefreshStatisticsAsync( CancellationToken.None );

        Assert.IsFalse( service.IsRefreshing );
    }

    [TestMethod]
    public async Task RefreshStatisticsAsync_SkipsComputeWhenCacheFresh( ) {
        SetupEmptyRecordList( );
        StatisticsService service = CreateService( );

        _ = await service.RefreshStatisticsAsync( CancellationToken.None );
        _ = await service.RefreshStatisticsAsync( CancellationToken.None );

        _atProtoStorageMock.Verify(
            x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Once( )
        );
    }

    [TestMethod]
    public async Task RefreshStatisticsAsync_ForceRefresh_BypassesFreshCache( ) {
        SetupEmptyRecordList( );
        StatisticsService service = CreateService( );

        _ = await service.RefreshStatisticsAsync( CancellationToken.None );
        _ = await service.RefreshStatisticsAsync( true, CancellationToken.None );

        _atProtoStorageMock.Verify(
            x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Exactly( 2 )
        );
    }

    [TestMethod]
    public async Task RefreshStatisticsAsync_UpdatesCachedStats( ) {
        SetupRecordList( [
            CreateTrackRecord( "at://did:plc:test/link.bridgebeats.lookup/track:ISRC1", "Artist1", "Track1", DateTimeOffset.UtcNow ),
            CreateAlbumRecord( "at://did:plc:test/link.bridgebeats.lookup/album:UPC1", "Artist2", "Album1", DateTimeOffset.UtcNow )
        ] );
        StatisticsService service = CreateService( );

        _ = await service.RefreshStatisticsAsync( CancellationToken.None );
        LookupStatistics? cached = service.GetCachedStatistics();

        Assert.IsNotNull( cached );
        Assert.AreEqual( 2, cached.TotalRecords );
        Assert.AreEqual( 1, cached.AlbumCount );
        Assert.AreEqual( 1, cached.TrackCount );
    }

    private StatisticsService CreateService( ) {
        return new StatisticsService(
            _atProtoStorageMock.Object,
            _redisMock.Object,
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

    private static async IAsyncEnumerable<(string AtUri, MediaLinkResult Result)> BlockingSequence( Task releaseTask ) {
        await releaseTask;
        yield break;
    }

    private static async Task WaitUntilAsync( Func<bool> condition, TimeSpan timeout ) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline) {
            if (condition( )) {
                return;
            }

            await Task.Delay( 10 );
        }

        Assert.Fail( "Condition not met before timeout." );
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
}

#pragma warning restore CS1591
