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

    /// <summary>
    /// Initializes test dependencies before each test method.
    /// </summary>
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

    /// <summary>
    /// Verifies that the constructor creates a valid instance when provided with valid dependencies.
    /// </summary>
    [TestMethod]
    public void Constructor_WithValidDependencies_ShouldCreateInstance( ) {
        // Act
        StatisticsService service = CreateService( );

        // Assert
        Assert.IsNotNull( service );
    }

    #endregion

    #region GetStatisticsAsync Tests

    /// <summary>
    /// Verifies that <see cref="StatisticsService.GetStatisticsAsync"/> returns zero counts and empty
    /// collections when the ATProto storage contains no records.
    /// </summary>
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

    /// <summary>
    /// Verifies that <see cref="StatisticsService.GetStatisticsAsync"/> correctly computes statistics
    /// for a mix of track and album records, including correct total, album, and track counts.
    /// </summary>
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

    /// <summary>
    /// Verifies that <see cref="StatisticsService.GetStatisticsAsync"/> correctly counts the number
    /// of records per music provider.
    /// </summary>
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

    /// <summary>
    /// Verifies that <see cref="StatisticsService.GetStatisticsAsync"/> correctly identifies the
    /// earliest and latest lookup timestamps from the records.
    /// </summary>
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

    /// <summary>
    /// Verifies that <see cref="StatisticsService.GetStatisticsAsync"/> returns only the five most
    /// recent entries in the correct order (newest first).
    /// </summary>
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

    /// <summary>
    /// Verifies that <see cref="StatisticsService.GetStatisticsAsync"/> generates a card ID
    /// for each recent entry, extracted from the ATProto URI.
    /// </summary>
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

    /// <summary>
    /// Verifies that <see cref="StatisticsService.GetStatisticsAsync"/> caches results to avoid
    /// repeated calls to the ATProto storage service.
    /// </summary>
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

    /// <summary>
    /// Verifies that <see cref="StatisticsService.RefreshStatisticsAsync"/> bypasses the cache
    /// and fetches fresh data from the ATProto storage service.
    /// </summary>
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

    /// <summary>
    /// Verifies that <see cref="StatisticsService.GetStatisticsAsync"/> re-fetches data from
    /// the ATProto storage service after the cache expires.
    /// </summary>
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

    /// <summary>
    /// Verifies that <see cref="StatisticsService.GetStatisticsAsync"/> throws
    /// <see cref="OperationCanceledException"/> when the cancellation token is cancelled.
    /// </summary>
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

    /// <summary>
    /// Creates a <see cref="StatisticsService"/> instance with the mocked dependencies.
    /// </summary>
    /// <returns>A configured <see cref="StatisticsService"/> instance.</returns>
    private StatisticsService CreateService( ) {
        return new StatisticsService(
            _atProtoStorageMock.Object,
            _settings,
            _loggerMock.Object
        );
    }

    /// <summary>
    /// Configures the ATProto storage mock to return an empty list of records.
    /// </summary>
    private void SetupEmptyRecordList( ) {
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( AsyncEnumerable.Empty<(string, MediaLinkResult)>( ) );
    }

    /// <summary>
    /// Configures the ATProto storage mock to return the specified list of records.
    /// </summary>
    /// <param name="records">The records to return from the mock.</param>
    private void SetupRecordList( List<(string AtUri, MediaLinkResult Result)> records ) {
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( records.ToAsyncEnumerable( ) );
    }

    /// <summary>
    /// Creates a track record with the specified metadata for testing.
    /// </summary>
    /// <param name="atUri">The ATProto URI for the record.</param>
    /// <param name="artist">The artist name.</param>
    /// <param name="title">The track title.</param>
    /// <param name="lookedUpAt">The timestamp when the lookup occurred.</param>
    /// <returns>A tuple containing the AT URI and the <see cref="MediaLinkResult"/>.</returns>
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

    /// <summary>
    /// Creates an album record with the specified metadata for testing.
    /// </summary>
    /// <param name="atUri">The ATProto URI for the record.</param>
    /// <param name="artist">The artist name.</param>
    /// <param name="title">The album title.</param>
    /// <param name="lookedUpAt">The timestamp when the lookup occurred.</param>
    /// <returns>A tuple containing the AT URI and the <see cref="MediaLinkResult"/>.</returns>
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

    /// <summary>
    /// Creates a track record with the specified providers for testing provider counting.
    /// </summary>
    /// <param name="atUri">The ATProto URI for the record.</param>
    /// <param name="providers">The providers to include in the result.</param>
    /// <returns>A tuple containing the AT URI and the <see cref="MediaLinkResult"/>.</returns>
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
