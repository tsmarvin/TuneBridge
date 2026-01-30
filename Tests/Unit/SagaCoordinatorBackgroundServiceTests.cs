using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Domain.Services.Queue;
using BridgeBeats.Worker.SagaCoordinator;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="SagaCoordinatorBackgroundService"/> to verify
/// saga completion handling, partial result writing, final result writing, and polling.
/// </summary>
[TestClass]
public class SagaCoordinatorBackgroundServiceTests {
    private Mock<IConnectionMultiplexer> _redisMock = null!;
    private Mock<ISubscriber> _subscriberMock = null!;
    private Mock<ISagaStateManager> _sagaManagerMock = null!;
    private Mock<IATProtoStorageService> _atProtoStorageMock = null!;
    private Mock<IMediaLinkCacheRepository> _cacheRepositoryMock = null!;
    private Mock<IRequestDeduplicator> _deduplicatorMock = null!;
    private Mock<ILogger<SagaResultCombiner>> _combinerLoggerMock = null!;
    private SagaResultCombiner _resultCombiner = null!;
    private Mock<IProviderQueueResolver<QueuedLookupRequest>> _queueResolverMock = null!;
    private HashSet<SupportedProviders> _enabledProviders = null!;
    private Mock<ILogger<SagaCoordinatorBackgroundService>> _loggerMock = null!;

    private const string TestSagaId = "test-saga-id-12345678";
    private const string TestLookupKey = "isrc:USRC12345678";
    private const string TestRecordUri = "at://did:plc:test/com.bridgebeats.medialink/abc123";

    /// <summary>
    /// Gets or sets the test context for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Initializes mocks before each test.
    /// </summary>
    [TestInitialize]
    public void Initialize( ) {
        _redisMock = new Mock<IConnectionMultiplexer>( );
        _subscriberMock = new Mock<ISubscriber>( );
        _sagaManagerMock = new Mock<ISagaStateManager>( );
        _atProtoStorageMock = new Mock<IATProtoStorageService>( );
        _cacheRepositoryMock = new Mock<IMediaLinkCacheRepository>( );
        _deduplicatorMock = new Mock<IRequestDeduplicator>( );
        _combinerLoggerMock = new Mock<ILogger<SagaResultCombiner>>( );
        _resultCombiner = new SagaResultCombiner( _combinerLoggerMock.Object );
        _queueResolverMock = new Mock<IProviderQueueResolver<QueuedLookupRequest>>( );
        _enabledProviders = [SupportedProviders.Spotify, SupportedProviders.AppleMusic, SupportedProviders.Tidal];
        _loggerMock = new Mock<ILogger<SagaCoordinatorBackgroundService>>( );

        _ = _redisMock.Setup( r => r.GetSubscriber( It.IsAny<object>( ) ) ).Returns( _subscriberMock.Object );
    }

    #region Constructor Tests

    /// <summary>
    /// Verifies that the constructor creates a valid instance with valid dependencies.
    /// </summary>
    [TestMethod]
    public void Constructor_WithValidDependencies_ShouldCreateInstance( ) {
        // Act
        SagaCoordinatorBackgroundService service = CreateService( );

        // Assert
        Assert.IsNotNull( service );
    }

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when Redis is null.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullRedis_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new SagaCoordinatorBackgroundService(
                null!,
                _sagaManagerMock.Object,
                _atProtoStorageMock.Object,
                _cacheRepositoryMock.Object,
                _deduplicatorMock.Object,
                _resultCombiner,
                _queueResolverMock.Object,
                _enabledProviders,
                _loggerMock.Object
            )
        );
    }

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when saga manager is null.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullSagaManager_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new SagaCoordinatorBackgroundService(
                _redisMock.Object,
                null!,
                _atProtoStorageMock.Object,
                _cacheRepositoryMock.Object,
                _deduplicatorMock.Object,
                _resultCombiner,
                _queueResolverMock.Object,
                _enabledProviders,
                _loggerMock.Object
            )
        );
    }

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when ATProto storage is null.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullAtProtoStorage_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new SagaCoordinatorBackgroundService(
                _redisMock.Object,
                _sagaManagerMock.Object,
                null!,
                _cacheRepositoryMock.Object,
                _deduplicatorMock.Object,
                _resultCombiner,
                _queueResolverMock.Object,
                _enabledProviders,
                _loggerMock.Object
            )
        );
    }

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when cache repository is null.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullCacheRepository_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new SagaCoordinatorBackgroundService(
                _redisMock.Object,
                _sagaManagerMock.Object,
                _atProtoStorageMock.Object,
                null!,
                _deduplicatorMock.Object,
                _resultCombiner,
                _queueResolverMock.Object,
                _enabledProviders,
                _loggerMock.Object
            )
        );
    }

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when deduplicator is null.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullDeduplicator_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new SagaCoordinatorBackgroundService(
                _redisMock.Object,
                _sagaManagerMock.Object,
                _atProtoStorageMock.Object,
                _cacheRepositoryMock.Object,
                null!,
                _resultCombiner,
                _queueResolverMock.Object,
                _enabledProviders,
                _loggerMock.Object
            )
        );
    }

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when result combiner is null.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullResultCombiner_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new SagaCoordinatorBackgroundService(
                _redisMock.Object,
                _sagaManagerMock.Object,
                _atProtoStorageMock.Object,
                _cacheRepositoryMock.Object,
                _deduplicatorMock.Object,
                null!,
                _queueResolverMock.Object,
                _enabledProviders,
                _loggerMock.Object
            )
        );
    }

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when logger is null.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new SagaCoordinatorBackgroundService(
                _redisMock.Object,
                _sagaManagerMock.Object,
                _atProtoStorageMock.Object,
                _cacheRepositoryMock.Object,
                _deduplicatorMock.Object,
                _resultCombiner,
                _queueResolverMock.Object,
                _enabledProviders,
                null!
            )
        );
    }

    #endregion

    #region Polling Tests

    /// <summary>
    /// Verifies that the service processes unfinalized sagas during polling.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task Polling_WhenUnfinalizedSagasExist_ShouldProcessThem( ) {
        // Arrange
        SagaCoordinatorBackgroundService service = CreateService( );
        LookupSagaState completeSaga = CreateCompleteSaga( );

        _ = _sagaManagerMock.Setup( s => s.GetCompletedButUnfinalizedAsync(
                It.IsAny<TimeSpan>( ),
                It.IsAny<int>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( [completeSaga] );

        _ = _atProtoStorageMock.Setup( a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ) ) )
            .ReturnsAsync( TestRecordUri );

        _ = _cacheRepositoryMock.Setup( c => c.CacheResultAsync( It.IsAny<MediaLinkResult>( ) ) )
            .ReturnsAsync( TestRecordUri );

        // Act - Start and quickly cancel after one poll cycle
        using CancellationTokenSource cts = new( );
        _ = service.StartAsync( cts.Token );
        await Task.Delay( 100, TestContext.CancellationToken ); // Give time for initial poll
        await cts.CancelAsync( );

        try {
            await service.StopAsync( CancellationToken.None );
        } catch (OperationCanceledException) {
            // Expected
        }

        // Assert - Should have queried for unfinalized sagas
        _sagaManagerMock.Verify(
            s => s.GetCompletedButUnfinalizedAsync(
                It.IsAny<TimeSpan>( ),
                It.IsAny<int>( ),
                It.IsAny<CancellationToken>( )
            ),
            Times.AtLeastOnce
        );
    }

    /// <summary>
    /// Verifies that the service does not write anything when no unfinalized sagas exist.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task Polling_WhenNoUnfinalizedSagas_ShouldNotWriteAnything( ) {
        // Arrange
        SagaCoordinatorBackgroundService service = CreateService( );

        _ = _sagaManagerMock.Setup( s => s.GetCompletedButUnfinalizedAsync(
                It.IsAny<TimeSpan>( ),
                It.IsAny<int>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( [] );

        // Act
        using CancellationTokenSource cts = new( );
        _ = service.StartAsync( cts.Token );
        await Task.Delay( 100, TestContext.CancellationToken );
        await cts.CancelAsync( );

        try {
            await service.StopAsync( CancellationToken.None );
        } catch (OperationCanceledException) {
            // Expected
        }

        // Assert - Should not have written anything
        _atProtoStorageMock.Verify(
            a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ) ),
            Times.Never
        );
    }

    /// <summary>
    /// Verifies that the service writes final results when sagas are complete.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task Polling_WhenSagaIsComplete_ShouldWriteFinalResult( ) {
        // Arrange
        SagaCoordinatorBackgroundService service = CreateService( );
        LookupSagaState completeSaga = CreateCompleteSaga( );

        _ = _sagaManagerMock.Setup( s => s.GetCompletedButUnfinalizedAsync(
                It.IsAny<TimeSpan>( ),
                It.IsAny<int>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( [completeSaga] );

        _ = _atProtoStorageMock.Setup( a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ) ) )
            .ReturnsAsync( TestRecordUri );

        _ = _cacheRepositoryMock.Setup( c => c.CacheResultAsync( It.IsAny<MediaLinkResult>( ) ) )
            .ReturnsAsync( TestRecordUri );

        // Act
        using CancellationTokenSource cts = new( );
        _ = service.StartAsync( cts.Token );
        await Task.Delay( 100, TestContext.CancellationToken );
        await cts.CancelAsync( );

        try {
            await service.StopAsync( CancellationToken.None );
        } catch (OperationCanceledException) {
            // Expected
        }

        // Assert - Should have written to ATProto
        _atProtoStorageMock.Verify(
            a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ) ),
            Times.Once
        );

        // Assert - Should have set final result URI
        _sagaManagerMock.Verify(
            s => s.SetFinalResultUriAsync( completeSaga.SagaId, TestRecordUri, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );

        // Assert - Should have cached the result
        _cacheRepositoryMock.Verify(
            c => c.CacheResultAsync( It.IsAny<MediaLinkResult>( ) ),
            Times.Once
        );

        // Assert - Should have released the deduplication lock
        _deduplicatorMock.Verify(
            d => d.ReleaseAsync( completeSaga.LookupKey, TestRecordUri, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
    }

    /// <summary>
    /// Verifies that the service writes partial results first when sagas are partial.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task Polling_WhenSagaIsPartialWithNoPartialUri_ShouldWritePartialFirst( ) {
        // Arrange
        SagaCoordinatorBackgroundService service = CreateService( );
        LookupSagaState partialSaga = CreateCompleteSaga( ) with {
            IsPartial = true,
            PartialResultUri = null
        };

        _ = _sagaManagerMock.Setup( s => s.GetCompletedButUnfinalizedAsync(
                It.IsAny<TimeSpan>( ),
                It.IsAny<int>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( [partialSaga] );

        _ = _atProtoStorageMock.Setup( a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ) ) )
            .ReturnsAsync( TestRecordUri );

        _ = _cacheRepositoryMock.Setup( c => c.CacheResultAsync( It.IsAny<MediaLinkResult>( ) ) )
            .ReturnsAsync( TestRecordUri );

        // Act
        using CancellationTokenSource cts = new( );
        _ = service.StartAsync( cts.Token );
        await Task.Delay( 100, TestContext.CancellationToken );
        await cts.CancelAsync( );

        try {
            await service.StopAsync( CancellationToken.None );
        } catch (OperationCanceledException) {
            // Expected
        }

        // Assert - Should have written partial result first
        _sagaManagerMock.Verify(
            s => s.SetPartialResultUriAsync( partialSaga.SagaId, TestRecordUri, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );

        // Assert - Should also have written final result
        _sagaManagerMock.Verify(
            s => s.SetFinalResultUriAsync( partialSaga.SagaId, TestRecordUri, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
    }

    /// <summary>
    /// Verifies that the service continues processing when a write fails.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task Polling_WhenWriteFails_ShouldContinueToNextSaga( ) {
        // Arrange
        SagaCoordinatorBackgroundService service = CreateService( );
        LookupSagaState saga1 = CreateCompleteSaga( ) with { SagaId = "saga-1" };
        LookupSagaState saga2 = CreateCompleteSaga( ) with { SagaId = "saga-2" };

        _ = _sagaManagerMock.Setup( s => s.GetCompletedButUnfinalizedAsync(
                It.IsAny<TimeSpan>( ),
                It.IsAny<int>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( [saga1, saga2] );

        // First saga write fails, second succeeds
        int callCount = 0;
        _ = _atProtoStorageMock.Setup( a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ) ) )
            .ReturnsAsync( ( ) => {
                callCount++;
                return callCount == 1 ? throw new Exception( "Simulated write failure" ) : TestRecordUri;
            } );

        _ = _cacheRepositoryMock.Setup( c => c.CacheResultAsync( It.IsAny<MediaLinkResult>( ) ) )
            .ReturnsAsync( TestRecordUri );

        // Act
        using CancellationTokenSource cts = new( );
        _ = service.StartAsync( cts.Token );
        await Task.Delay( 100, TestContext.CancellationToken );
        await cts.CancelAsync( );

        try {
            await service.StopAsync( CancellationToken.None );
        } catch (OperationCanceledException) {
            // Expected
        }

        // Assert - Both sagas should have been attempted (first fails, second succeeds)
        Assert.AreEqual( 2, callCount, "Both sagas should have been attempted" );
    }

    #endregion

    #region Final Result Writing Tests

    /// <summary>
    /// Verifies that failed sagas release the lock with null and delete the saga.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task WriteFinalResult_WhenNoSuccessfulResults_ShouldReleaseWithNullAndDeleteSaga( ) {
        // Arrange
        SagaCoordinatorBackgroundService service = CreateService( );
        LookupSagaState failedSaga = CreateCompleteSaga( ) with {
            ProviderStates = new Dictionary<SupportedProviders, ProviderLookupState> {
                [SupportedProviders.Spotify] = new(
                    Provider: SupportedProviders.Spotify,
                    IsComplete: true,
                    IsSuccess: false,
                    ResultJson: null,
                    CompletedAt: DateTimeOffset.UtcNow,
                    ErrorMessage: "Rate limited"
                )
            }
        };

        _ = _sagaManagerMock.Setup( s => s.GetCompletedButUnfinalizedAsync(
                It.IsAny<TimeSpan>( ),
                It.IsAny<int>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( [failedSaga] );

        _ = _sagaManagerMock.Setup( s => s.DeleteAsync( It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( true );

        // Act
        using CancellationTokenSource cts = new( );
        _ = service.StartAsync( cts.Token );
        await Task.Delay( 100, TestContext.CancellationToken );
        await cts.CancelAsync( );

        try {
            await service.StopAsync( CancellationToken.None );
        } catch (OperationCanceledException) {
            // Expected
        }

        // Assert - Should release with null
        _deduplicatorMock.Verify(
            d => d.ReleaseAsync( failedSaga.LookupKey, null, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );

        // Assert - Should delete the saga
        _sagaManagerMock.Verify(
            s => s.DeleteAsync( failedSaga.SagaId, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a new <see cref="SagaCoordinatorBackgroundService"/> instance with the configured mocks.
    /// </summary>
    /// <returns>A new <see cref="SagaCoordinatorBackgroundService"/> instance.</returns>
    private SagaCoordinatorBackgroundService CreateService( ) {
        return new SagaCoordinatorBackgroundService(
            _redisMock.Object,
            _sagaManagerMock.Object,
            _atProtoStorageMock.Object,
            _cacheRepositoryMock.Object,
            _deduplicatorMock.Object,
            _resultCombiner,
            _queueResolverMock.Object,
            _enabledProviders,
            _loggerMock.Object
        );
    }

    /// <summary>
    /// Creates a test <see cref="LookupSagaState"/> representing a completed saga with one successful provider.
    /// </summary>
    /// <returns>A new <see cref="LookupSagaState"/> instance with complete status.</returns>
    private static LookupSagaState CreateCompleteSaga( ) {
        string resultJson = """
        {
            "isrc": "USRC12345678",
            "trackName": "Test Song",
            "artistName": "Test Artist",
            "albumName": "Test Album",
            "url": "https://open.spotify.com/track/abc123"
        }
        """;

        return new LookupSagaState {
            SagaId = TestSagaId,
            LookupKey = TestLookupKey,
            LookupType = LookupRequestType.IsrcLookup,
            LookupValue = "USRC12345678",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes( -5 ),
            ProviderStates = new Dictionary<SupportedProviders, ProviderLookupState> {
                [SupportedProviders.Spotify] = new(
                    Provider: SupportedProviders.Spotify,
                    IsComplete: true,
                    IsSuccess: true,
                    ResultJson: resultJson,
                    CompletedAt: DateTimeOffset.UtcNow,
                    ErrorMessage: null
                )
            },
            PartialResultUri = null,
            FinalResultUri = null,
            IsPartial = false,
            InitialProvider = SupportedProviders.Spotify,
            RateLimitInfo = null
        };
    }

    #endregion
}
