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

        // By default the coordinator wins the secondaries-queued marker (no concurrent handler)
        _ = _sagaManagerMock
            .Setup( s => s.TryMarkSecondariesQueuedAsync( It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( true );
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

        _ = _atProtoStorageMock.Setup( a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        _ = _cacheRepositoryMock.Setup( c => c.CacheResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
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
            a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ),
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

        _ = _atProtoStorageMock.Setup( a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        _ = _cacheRepositoryMock.Setup( c => c.CacheResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
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
            a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ),
            Times.Once
        );

        // Assert - Should have set final result URI
        _sagaManagerMock.Verify(
            s => s.SetFinalResultUriAsync( completeSaga.SagaId, TestRecordUri, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );

        // Assert - Should have cached the result
        _cacheRepositoryMock.Verify(
            c => c.CacheResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ),
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

        _ = _atProtoStorageMock.Setup( a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        _ = _cacheRepositoryMock.Setup( c => c.CacheResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
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
        _ = _atProtoStorageMock.Setup( a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => {
                callCount++;
                return callCount == 1 ? throw new InvalidOperationException( "Simulated write failure" ) : TestRecordUri;
            } );

        _ = _cacheRepositoryMock.Setup( c => c.CacheResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
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

    /// <summary>
    /// Verifies that finalizing a saga clears its partial flag so callers no longer treat it as partial.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task WriteFinalResult_WhenSagaIsFinalized_ShouldClearIsPartialFlag( ) {
        // Arrange
        SagaCoordinatorBackgroundService service = CreateService( );
        LookupSagaState completeSaga = CreateCompleteSaga( );

        _ = _sagaManagerMock.Setup( s => s.GetCompletedButUnfinalizedAsync(
                It.IsAny<TimeSpan>( ),
                It.IsAny<int>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( [completeSaga] );

        _ = _atProtoStorageMock.Setup( a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        _ = _cacheRepositoryMock.Setup( c => c.CacheResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
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

        // Assert - Should have cleared the partial flag after setting the final result URI
        _sagaManagerMock.Verify(
            s => s.SetIsPartialAsync( completeSaga.SagaId, false, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
    }

    #endregion

    #region Secondary Lookup Tests

    /// <summary>
    /// Verifies that when secondary lookups are queued for a completed initial lookup, the saga
    /// is marked partial and the secondary requests are enqueued at Interactive priority so a
    /// waiting caller receives the complete result as fast as possible.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task SagaCompletion_WhenSecondaryLookupsQueued_ShouldMarkPartialAndQueueInteractive( ) {
        // Arrange
        Dictionary<string, Action<RedisChannel, RedisValue>> handlers = [];
        _ = _subscriberMock
            .Setup( s => s.SubscribeAsync( It.IsAny<RedisChannel>( ), It.IsAny<Action<RedisChannel, RedisValue>>( ), It.IsAny<CommandFlags>( ) ) )
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>(
                ( channel, handler, _ ) => handlers[channel.ToString( )] = handler )
            .Returns( Task.CompletedTask );

        SagaCoordinatorBackgroundService service = CreateService( );
        LookupSagaState uriSaga = CreateUriLookupSaga( );

        _ = _sagaManagerMock.Setup( s => s.GetAsync( TestSagaId, It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( uriSaga );

        _ = _sagaManagerMock.Setup( s => s.GetCompletedButUnfinalizedAsync(
                It.IsAny<TimeSpan>( ),
                It.IsAny<int>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( [] );

        // No cached data for the external ID - all other providers need secondary lookups
        _ = _cacheRepositoryMock
            .Setup( c => c.TryGetCachedResultByISRCAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( ((MediaLinkResult result, string recordUri, bool isStale)?)null );

        Mock<IRequestQueue<QueuedLookupRequest>> queueMock = new( );
        _ = _queueResolverMock
            .Setup( r => r.GetQueue( It.IsAny<SupportedProviders>( ) ) )
            .Returns( queueMock.Object );

        _ = _atProtoStorageMock.Setup( a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        _ = _cacheRepositoryMock.Setup( c => c.CacheResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        // Act - start the service, then simulate a saga completion event via Pub/Sub
        using CancellationTokenSource cts = new( );
        _ = service.StartAsync( cts.Token );
        await Task.Delay( 100, TestContext.CancellationToken );

        Assert.IsTrue( handlers.ContainsKey( "saga:completed" ), "Service should have subscribed to saga:completed" );
        handlers["saga:completed"]( RedisChannel.Literal( "saga:completed" ), TestSagaId );
        await Task.Delay( 250, TestContext.CancellationToken );

        await cts.CancelAsync( );
        try {
            await service.StopAsync( CancellationToken.None );
        } catch (OperationCanceledException) {
            // Expected
        }

        // Assert - Saga should be marked partial when secondaries are queued
        _sagaManagerMock.Verify(
            s => s.SetIsPartialAsync( TestSagaId, true, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );

        // Assert - Secondary lookups queued at Interactive priority (AppleMusic + Tidal)
        queueMock.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), QueuePriority.Interactive, It.IsAny<CancellationToken>( ) ),
            Times.Exactly( 2 )
        );

        // Assert - Partial result written with the IsPartial flag set on the stored record
        _atProtoStorageMock.Verify(
            a => a.StoreMediaLinkResultAsync( It.Is<MediaLinkResult>( r => r.IsPartial ), It.IsAny<CancellationToken>( ) ),
            Times.Once
        );

        // Assert - Waiters notified with the partial result URI
        _deduplicatorMock.Verify(
            d => d.ReleaseAsync( uriSaga.LookupKey, TestRecordUri, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
    }

    /// <summary>
    /// Verifies that cached provider results found via the external ID are materialized into the
    /// saga as completed provider states so the final result includes them, instead of a
    /// one-provider final overwriting the richer existing record.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task SagaCompletion_WhenAllOtherProvidersCached_ShouldMaterializeCachedResultsAndFinalize( ) {
        // Arrange
        Dictionary<string, Action<RedisChannel, RedisValue>> handlers = [];
        _ = _subscriberMock
            .Setup( s => s.SubscribeAsync( It.IsAny<RedisChannel>( ), It.IsAny<Action<RedisChannel, RedisValue>>( ), It.IsAny<CommandFlags>( ) ) )
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>(
                ( channel, handler, _ ) => handlers[channel.ToString( )] = handler )
            .Returns( Task.CompletedTask );

        SagaCoordinatorBackgroundService service = CreateService( );
        LookupSagaState uriSaga = CreateUriLookupSaga( );

        _ = _sagaManagerMock.Setup( s => s.GetAsync( TestSagaId, It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( uriSaga );

        _ = _sagaManagerMock.Setup( s => s.GetCompletedButUnfinalizedAsync(
                It.IsAny<TimeSpan>( ),
                It.IsAny<int>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( [] );

        // Cache has fresh data for both remaining providers - no secondary lookups needed
        MediaLinkResult cachedResult = CreateCachedExternalIdResult( SupportedProviders.AppleMusic, SupportedProviders.Tidal );
        _ = _cacheRepositoryMock
            .Setup( c => c.TryGetCachedResultByISRCAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( (cachedResult, TestRecordUri, false) );

        _ = _atProtoStorageMock.Setup( a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        _ = _cacheRepositoryMock.Setup( c => c.CacheResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        // Act - start the service, then simulate a saga completion event via Pub/Sub
        using CancellationTokenSource cts = new( );
        _ = service.StartAsync( cts.Token );
        await Task.Delay( 100, TestContext.CancellationToken );

        Assert.IsTrue( handlers.ContainsKey( "saga:completed" ), "Service should have subscribed to saga:completed" );
        handlers["saga:completed"]( RedisChannel.Literal( "saga:completed" ), TestSagaId );
        await Task.Delay( 250, TestContext.CancellationToken );

        await cts.CancelAsync( );
        try {
            await service.StopAsync( CancellationToken.None );
        } catch (OperationCanceledException) {
            // Expected
        }

        // Assert - Cached provider results materialized into the saga as completed states
        _sagaManagerMock.Verify(
            s => s.UpdateProviderStateAsync(
                TestSagaId,
                It.Is<ProviderLookupState>( p =>
                    p.Provider == SupportedProviders.AppleMusic &&
                    p.IsComplete &&
                    p.IsSuccess &&
                    p.ResultJson != null &&
                    p.CompletedAt != null
                ),
                It.IsAny<CancellationToken>( )
            ),
            Times.Once
        );
        _sagaManagerMock.Verify(
            s => s.UpdateProviderStateAsync(
                TestSagaId,
                It.Is<ProviderLookupState>( p =>
                    p.Provider == SupportedProviders.Tidal &&
                    p.IsComplete &&
                    p.IsSuccess &&
                    p.ResultJson != null
                ),
                It.IsAny<CancellationToken>( )
            ),
            Times.Once
        );

        // Assert - No secondary lookups queued (everything came from cache)
        _queueResolverMock.Verify( r => r.GetQueue( It.IsAny<SupportedProviders>( ) ), Times.Never );

        // Assert - Final result stored with ALL three providers' results, not marked partial
        _atProtoStorageMock.Verify(
            a => a.StoreMediaLinkResultAsync(
                It.Is<MediaLinkResult>( r => r.Results.Count == 3 && !r.IsPartial ),
                It.IsAny<CancellationToken>( )
            ),
            Times.Once
        );

        // Assert - Saga finalized
        _sagaManagerMock.Verify(
            s => s.SetFinalResultUriAsync( TestSagaId, TestRecordUri, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
    }

    /// <summary>
    /// Verifies the mixed case: providers found in cache are materialized into the saga while
    /// the remaining providers are queued for secondary lookups, so cached data is never dropped
    /// from the partial (or eventual final) result.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task SagaCompletion_WhenSomeProvidersCached_ShouldMaterializeCachedAndQueueRemaining( ) {
        // Arrange
        Dictionary<string, Action<RedisChannel, RedisValue>> handlers = [];
        _ = _subscriberMock
            .Setup( s => s.SubscribeAsync( It.IsAny<RedisChannel>( ), It.IsAny<Action<RedisChannel, RedisValue>>( ), It.IsAny<CommandFlags>( ) ) )
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>(
                ( channel, handler, _ ) => handlers[channel.ToString( )] = handler )
            .Returns( Task.CompletedTask );

        SagaCoordinatorBackgroundService service = CreateService( );
        LookupSagaState uriSaga = CreateUriLookupSaga( );

        _ = _sagaManagerMock.Setup( s => s.GetAsync( TestSagaId, It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( uriSaga );

        _ = _sagaManagerMock.Setup( s => s.GetCompletedButUnfinalizedAsync(
                It.IsAny<TimeSpan>( ),
                It.IsAny<int>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( [] );

        // Cache has fresh data for AppleMusic only - Tidal still needs a secondary lookup
        MediaLinkResult cachedResult = CreateCachedExternalIdResult( SupportedProviders.AppleMusic );
        _ = _cacheRepositoryMock
            .Setup( c => c.TryGetCachedResultByISRCAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( (cachedResult, TestRecordUri, false) );

        Mock<IRequestQueue<QueuedLookupRequest>> queueMock = new( );
        _ = _queueResolverMock
            .Setup( r => r.GetQueue( It.IsAny<SupportedProviders>( ) ) )
            .Returns( queueMock.Object );

        _ = _atProtoStorageMock.Setup( a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        _ = _cacheRepositoryMock.Setup( c => c.CacheResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        // Act - start the service, then simulate a saga completion event via Pub/Sub
        using CancellationTokenSource cts = new( );
        _ = service.StartAsync( cts.Token );
        await Task.Delay( 100, TestContext.CancellationToken );

        Assert.IsTrue( handlers.ContainsKey( "saga:completed" ), "Service should have subscribed to saga:completed" );
        handlers["saga:completed"]( RedisChannel.Literal( "saga:completed" ), TestSagaId );
        await Task.Delay( 250, TestContext.CancellationToken );

        await cts.CancelAsync( );
        try {
            await service.StopAsync( CancellationToken.None );
        } catch (OperationCanceledException) {
            // Expected
        }

        // Assert - Cached AppleMusic result materialized into the saga
        _sagaManagerMock.Verify(
            s => s.UpdateProviderStateAsync(
                TestSagaId,
                It.Is<ProviderLookupState>( p =>
                    p.Provider == SupportedProviders.AppleMusic &&
                    p.IsComplete &&
                    p.IsSuccess &&
                    p.ResultJson != null
                ),
                It.IsAny<CancellationToken>( )
            ),
            Times.Once
        );

        // Assert - Only the uncached provider (Tidal) queued for a secondary lookup
        queueMock.Verify(
            q => q.EnqueueAsync(
                It.Is<QueuedLookupRequest>( r => r.Provider == SupportedProviders.Tidal ),
                QueuePriority.Interactive,
                It.IsAny<CancellationToken>( )
            ),
            Times.Once
        );
        queueMock.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ),
            Times.Once
        );

        // Assert - Partial result includes the cached provider's data (Spotify + AppleMusic)
        _atProtoStorageMock.Verify(
            a => a.StoreMediaLinkResultAsync(
                It.Is<MediaLinkResult>( r => r.Results.Count == 2 && r.IsPartial ),
                It.IsAny<CancellationToken>( )
            ),
            Times.Once
        );

        // Assert - Saga not finalized while the secondary lookup is pending
        _sagaManagerMock.Verify(
            s => s.SetFinalResultUriAsync( It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );
    }

    /// <summary>
    /// Verifies that secondary lookups for a bulk-origin saga (e.g. JetStream firehose) are
    /// queued at Background priority instead of competing with interactive lookups.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task SagaCompletion_WhenSagaOriginatedFromBulk_ShouldQueueSecondariesAtBackground( ) {
        // Arrange
        Dictionary<string, Action<RedisChannel, RedisValue>> handlers = [];
        _ = _subscriberMock
            .Setup( s => s.SubscribeAsync( It.IsAny<RedisChannel>( ), It.IsAny<Action<RedisChannel, RedisValue>>( ), It.IsAny<CommandFlags>( ) ) )
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>(
                ( channel, handler, _ ) => handlers[channel.ToString( )] = handler )
            .Returns( Task.CompletedTask );

        SagaCoordinatorBackgroundService service = CreateService( );
        LookupSagaState bulkSaga = CreateUriLookupSaga( QueuePriority.Bulk );

        _ = _sagaManagerMock.Setup( s => s.GetAsync( TestSagaId, It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( bulkSaga );

        _ = _sagaManagerMock.Setup( s => s.GetCompletedButUnfinalizedAsync(
                It.IsAny<TimeSpan>( ),
                It.IsAny<int>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( [] );

        _ = _cacheRepositoryMock
            .Setup( c => c.TryGetCachedResultByISRCAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( ((MediaLinkResult result, string recordUri, bool isStale)?)null );

        Mock<IRequestQueue<QueuedLookupRequest>> queueMock = new( );
        _ = _queueResolverMock
            .Setup( r => r.GetQueue( It.IsAny<SupportedProviders>( ) ) )
            .Returns( queueMock.Object );

        _ = _atProtoStorageMock.Setup( a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        _ = _cacheRepositoryMock.Setup( c => c.CacheResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        // Act - start the service, then simulate a saga completion event via Pub/Sub
        using CancellationTokenSource cts = new( );
        _ = service.StartAsync( cts.Token );
        await Task.Delay( 100, TestContext.CancellationToken );

        Assert.IsTrue( handlers.ContainsKey( "saga:completed" ), "Service should have subscribed to saga:completed" );
        handlers["saga:completed"]( RedisChannel.Literal( "saga:completed" ), TestSagaId );
        await Task.Delay( 250, TestContext.CancellationToken );

        await cts.CancelAsync( );
        try {
            await service.StopAsync( CancellationToken.None );
        } catch (OperationCanceledException) {
            // Expected
        }

        // Assert - Secondaries queued at Background priority, carrying the bulk origin
        queueMock.Verify(
            q => q.EnqueueAsync(
                It.Is<QueuedLookupRequest>( r => r.OriginPriority == QueuePriority.Bulk ),
                QueuePriority.Background,
                It.IsAny<CancellationToken>( )
            ),
            Times.Exactly( 2 )
        );

        // Assert - Nothing entered the interactive lane
        queueMock.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), QueuePriority.Interactive, It.IsAny<CancellationToken>( ) ),
            Times.Never
        );
    }

    /// <summary>
    /// Verifies that when a concurrent handler already claimed the secondaries-queued marker,
    /// the losing handler queues nothing but still defers finalization (writes the partial).
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task SagaCompletion_WhenSecondariesMarkerAlreadyTaken_ShouldNotEnqueueButStillDeferFinalization( ) {
        // Arrange
        Dictionary<string, Action<RedisChannel, RedisValue>> handlers = [];
        _ = _subscriberMock
            .Setup( s => s.SubscribeAsync( It.IsAny<RedisChannel>( ), It.IsAny<Action<RedisChannel, RedisValue>>( ), It.IsAny<CommandFlags>( ) ) )
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>(
                ( channel, handler, _ ) => handlers[channel.ToString( )] = handler )
            .Returns( Task.CompletedTask );

        SagaCoordinatorBackgroundService service = CreateService( );
        LookupSagaState uriSaga = CreateUriLookupSaga( );

        _ = _sagaManagerMock.Setup( s => s.GetAsync( TestSagaId, It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( uriSaga );

        _ = _sagaManagerMock.Setup( s => s.GetCompletedButUnfinalizedAsync(
                It.IsAny<TimeSpan>( ),
                It.IsAny<int>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( [] );

        _ = _cacheRepositoryMock
            .Setup( c => c.TryGetCachedResultByISRCAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( ((MediaLinkResult result, string recordUri, bool isStale)?)null );

        // Another handler already won the race to queue secondaries
        _ = _sagaManagerMock
            .Setup( s => s.TryMarkSecondariesQueuedAsync( TestSagaId, It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( false );

        Mock<IRequestQueue<QueuedLookupRequest>> queueMock = new( );
        _ = _queueResolverMock
            .Setup( r => r.GetQueue( It.IsAny<SupportedProviders>( ) ) )
            .Returns( queueMock.Object );

        _ = _atProtoStorageMock.Setup( a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        _ = _cacheRepositoryMock.Setup( c => c.CacheResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        // Act - start the service, then simulate a saga completion event via Pub/Sub
        using CancellationTokenSource cts = new( );
        _ = service.StartAsync( cts.Token );
        await Task.Delay( 100, TestContext.CancellationToken );

        Assert.IsTrue( handlers.ContainsKey( "saga:completed" ), "Service should have subscribed to saga:completed" );
        handlers["saga:completed"]( RedisChannel.Literal( "saga:completed" ), TestSagaId );
        await Task.Delay( 250, TestContext.CancellationToken );

        await cts.CancelAsync( );
        try {
            await service.StopAsync( CancellationToken.None );
        } catch (OperationCanceledException) {
            // Expected
        }

        // Assert - The losing handler must not queue duplicate secondary lookups
        queueMock.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );
        _sagaManagerMock.Verify(
            s => s.InitializeProviderStatesAsync( TestSagaId, It.IsAny<IEnumerable<SupportedProviders>>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );

        // Assert - It still defers finalization (secondaries pending elsewhere)
        _sagaManagerMock.Verify(
            s => s.SetFinalResultUriAsync( It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );

        // Assert - The partial result is still written for waiting callers
        _atProtoStorageMock.Verify(
            a => a.StoreMediaLinkResultAsync( It.Is<MediaLinkResult>( r => r.IsPartial ), It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
    }

    /// <summary>
    /// Verifies that the polling fallback runs the same secondary-lookup check as the Pub/Sub
    /// handlers: a saga discovered via polling must not be finalized as complete while other
    /// providers still need secondary lookups.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task Polling_WhenUriSagaNeedsSecondaryLookups_ShouldQueueThemAndWritePartialInsteadOfFinalizing( ) {
        // Arrange
        SagaCoordinatorBackgroundService service = CreateService( );
        LookupSagaState uriSaga = CreateUriLookupSaga( );

        _ = _sagaManagerMock.Setup( s => s.GetCompletedButUnfinalizedAsync(
                It.IsAny<TimeSpan>( ),
                It.IsAny<int>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( [uriSaga] );

        // No cached data for the external ID - all other providers need secondary lookups
        _ = _cacheRepositoryMock
            .Setup( c => c.TryGetCachedResultByISRCAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( ((MediaLinkResult result, string recordUri, bool isStale)?)null );

        Mock<IRequestQueue<QueuedLookupRequest>> queueMock = new( );
        _ = _queueResolverMock
            .Setup( r => r.GetQueue( It.IsAny<SupportedProviders>( ) ) )
            .Returns( queueMock.Object );

        _ = _atProtoStorageMock.Setup( a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        _ = _cacheRepositoryMock.Setup( c => c.CacheResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        // Act - polling runs immediately on startup
        using CancellationTokenSource cts = new( );
        _ = service.StartAsync( cts.Token );
        await Task.Delay( 250, TestContext.CancellationToken );
        await cts.CancelAsync( );

        try {
            await service.StopAsync( CancellationToken.None );
        } catch (OperationCanceledException) {
            // Expected
        }

        // Assert - Secondary lookups queued for the missing providers (AppleMusic + Tidal)
        queueMock.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), QueuePriority.Interactive, It.IsAny<CancellationToken>( ) ),
            Times.Exactly( 2 )
        );

        // Assert - Partial result written instead of a premature final
        _atProtoStorageMock.Verify(
            a => a.StoreMediaLinkResultAsync( It.Is<MediaLinkResult>( r => r.IsPartial ), It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
        _sagaManagerMock.Verify(
            s => s.SetFinalResultUriAsync( It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
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
    /// Creates a test <see cref="LookupSagaState"/> for a completed URL lookup whose result
    /// carries an external ID, making it eligible for secondary provider lookups.
    /// </summary>
    /// <param name="originPriority">The origin priority recorded on the saga.</param>
    /// <returns>A new <see cref="LookupSagaState"/> instance for a URI lookup.</returns>
    private static LookupSagaState CreateUriLookupSaga( QueuePriority originPriority = QueuePriority.Interactive ) {
        string resultJson = """
        {
            "artist": "Test Artist",
            "title": "Test Song",
            "externalId": "USRC12345678",
            "url": "https://open.spotify.com/track/abc123",
            "isAlbum": false
        }
        """;

        return new LookupSagaState {
            SagaId = TestSagaId,
            LookupKey = "urilookup:testhash123",
            LookupType = LookupRequestType.UriLookup,
            LookupValue = "https://open.spotify.com/track/abc123",
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds( -30 ),
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
            RateLimitInfo = null,
            OriginPriority = originPriority
        };
    }

    /// <summary>
    /// Creates a test <see cref="MediaLinkResult"/> representing cached external-ID data
    /// containing results for the specified providers.
    /// </summary>
    /// <param name="providers">The providers with cached results.</param>
    /// <returns>A new <see cref="MediaLinkResult"/> instance.</returns>
    private static MediaLinkResult CreateCachedExternalIdResult( params SupportedProviders[] providers ) {
        MediaLinkResult cachedResult = new( );

        foreach (SupportedProviders provider in providers) {
            cachedResult.Results[provider] = new MusicLookupResult {
                Artist = "Test Artist",
                Title = "Test Song",
                ExternalId = "USRC12345678",
                URL = $"https://{provider}.example.com/track/abc123",
                IsAlbum = false
            };
        }

        return cachedResult;
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
