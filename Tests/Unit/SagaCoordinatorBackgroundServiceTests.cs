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
/// Unit tests for <see cref="SagaCoordinatorBackgroundService"/>, the hosted service that finalizes
/// completed lookup sagas. Covers constructor dependency validation; the polling loop that picks up
/// completed-but-unfinalized sagas and writes their final result, releases the deduplicator, and
/// clears the partial flag; the write-failure path that continues to the next saga; the no-success
/// path that releases with a null URI and deletes the saga; and the secondary-lookup orchestration
/// driven by the <c>saga:completed</c> Redis subscription — marking partial, materializing cached
/// provider results to finalize early, queuing remaining providers at the priority implied by the
/// saga's origin, deferring finalization until secondaries return, and the
/// initialize-then-mark-then-claim ordering that guards the secondaries marker.
/// </summary>
[TestClass]
public class SagaCoordinatorBackgroundServiceTests {
    /// <summary>Mock Redis multiplexer supplying the subscriber for saga-completion events.</summary>
    private Mock<IConnectionMultiplexer> _redisMock = null!;
    /// <summary>Mock Redis subscriber whose registered handlers the tests invoke directly.</summary>
    private Mock<ISubscriber> _subscriberMock = null!;
    /// <summary>Mock saga state manager the service reads and updates.</summary>
    private Mock<ISagaStateManager> _sagaManagerMock = null!;
    /// <summary>Mock AT Protocol storage used to assert final and partial record writes.</summary>
    private Mock<IATProtoStorageService> _atProtoStorageMock = null!;
    /// <summary>Mock cache repository for result caching and cached-by-ISRC lookups.</summary>
    private Mock<IMediaLinkCacheRepository> _cacheRepositoryMock = null!;
    /// <summary>Mock request deduplicator the service releases on finalization.</summary>
    private Mock<IRequestDeduplicator> _deduplicatorMock = null!;
    /// <summary>Mock logger for the result combiner.</summary>
    private Mock<ILogger<SagaResultCombiner>> _combinerLoggerMock = null!;
    /// <summary>Real result combiner the service uses to merge provider results.</summary>
    private SagaResultCombiner _resultCombiner = null!;
    /// <summary>Mock provider-queue resolver used to assert secondary-lookup enqueues.</summary>
    private Mock<IProviderQueueResolver<QueuedLookupRequest>> _queueResolverMock = null!;
    /// <summary>The set of enabled providers (Spotify, Apple Music, Tidal) the service considers.</summary>
    private HashSet<SupportedProviders> _enabledProviders = null!;
    /// <summary>Mock logger for the background service.</summary>
    private Mock<ILogger<SagaCoordinatorBackgroundService>> _loggerMock = null!;

    /// <summary>Fixed saga id used across the tests.</summary>
    private const string TestSagaId = "test-saga-id-12345678";
    /// <summary>Fixed lookup key for the complete-saga fixtures.</summary>
    private const string TestLookupKey = "isrc:USRC12345678";
    /// <summary>Fixed record URI the storage mock returns for written results.</summary>
    private const string TestRecordUri = "at://did:plc:test/com.bridgebeats.medialink/abc123";

    /// <summary>MSTest-injected context; its cancellation token bounds the in-test delays.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates fresh mocks before each test, wires the subscriber, builds a real combiner, seeds the
    /// enabled-providers set, and defaults the secondaries-queued marker claim to succeed.
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

        // By default the coordinator wins the finalize claim (no concurrent handler)
        _ = _sagaManagerMock
            .Setup( s => s.TryClaimFinalizeAsync( It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( true );
    }

    #region Constructor Tests

    /// <summary>
    /// Verifies that valid dependencies produce a usable service instance.
    /// </summary>
    [TestMethod]
    public void Constructor_WithValidDependencies_ShouldCreateInstance( ) {
        // Act
        SagaCoordinatorBackgroundService service = CreateService( );

        // Assert
        Assert.IsNotNull( service );
    }

    /// <summary>
    /// Verifies that a null Redis multiplexer throws <see cref="ArgumentNullException"/>.
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
    /// Verifies that a null saga state manager throws <see cref="ArgumentNullException"/>.
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
    /// Verifies that a null AT Protocol storage service throws <see cref="ArgumentNullException"/>.
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
    /// Verifies that a null cache repository throws <see cref="ArgumentNullException"/>.
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
    /// Verifies that a null request deduplicator throws <see cref="ArgumentNullException"/>.
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
    /// Verifies that a null result combiner throws <see cref="ArgumentNullException"/>.
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
    /// Verifies that a null logger throws <see cref="ArgumentNullException"/>.
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
    /// Verifies that when completed-but-unfinalized sagas exist, the polling loop queries for them
    /// at least once and processes them.
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
    /// Verifies that when no unfinalized sagas are found, the polling loop writes nothing to storage.
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
    /// Verifies the full finalization of a complete saga: the final result is written to storage
    /// once, the saga's final result URI is set, the result is cached, and the deduplicator is
    /// released with the resulting record URI.
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
    /// Verifies that a partial saga with no existing partial URI has its partial result written
    /// first (partial URI set), then is finalized (final URI set), confirming the partial-then-final
    /// write order.
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
    /// Verifies the failure-isolation contract: when writing the first saga's result throws, the
    /// loop still attempts the second saga, so one bad saga does not block the rest of the batch.
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
    /// Verifies that a saga whose only provider failed (no successful results to combine) releases
    /// the deduplicator with a null URI and deletes the saga, rather than writing an empty result.
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
    /// Verifies that finalizing a saga clears its partial flag (sets <c>IsPartial</c> false), so a
    /// finalized saga is no longer treated as partial.
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
    /// Verifies the secondary-lookup path triggered by the <c>saga:completed</c> event: when the
    /// primary provider has resolved but no cached results exist for the others, the saga is marked
    /// partial, the two remaining providers are queued at interactive priority, a partial result is
    /// written to storage, and the deduplicator is released with the partial record URI.
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
    /// Verifies the cache-hit short circuit: when the remaining providers already have cached
    /// results, the service materializes those provider states (complete and successful) instead of
    /// queuing lookups, writes a complete three-provider non-partial result, and finalizes the saga
    /// without enqueuing anything.
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
    /// Verifies the mixed cache path: when one of two remaining providers is cached, the service
    /// materializes the cached provider state, queues only the uncached provider (Tidal) at
    /// interactive priority, writes a two-provider partial result, and does not finalize the saga.
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
    /// Verifies that secondaries inherit a priority derived from the saga's origin: a saga that
    /// originated from a bulk request queues its two secondary lookups at background priority (with
    /// the bulk origin preserved) and never at interactive priority.
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
    /// Verifies the marker-contention path: when the secondaries-queued marker is already taken by a
    /// concurrent worker (the claim returns false), this worker does not enqueue any lookups but
    /// still initializes provider states, marks the saga partial, writes a partial result, and
    /// defers finalization, so the marker holder remains responsible for completion.
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

        // Assert - The idempotent state/partial writes still run (they happen before the
        // marker claim so a published partial can never be mistaken for a final result)
        _sagaManagerMock.Verify(
            s => s.InitializeProviderStatesAsync( TestSagaId, It.IsAny<IEnumerable<SupportedProviders>>( ), It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
        _sagaManagerMock.Verify(
            s => s.SetIsPartialAsync( TestSagaId, true, It.IsAny<CancellationToken>( ) ),
            Times.Once
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
    /// Verifies the ordering invariant on the secondaries path: provider states are initialized and
    /// the saga is marked partial before the secondaries-queued marker is claimed, so a concurrent
    /// worker that loses the marker race still observes initialized state.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task SagaCompletion_ShouldInitializeStatesAndMarkPartialBeforeClaimingMarker( ) {
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

        // Record the order of the saga-manager calls that close the partial-vs-final race
        List<string> callOrder = [];
        _ = _sagaManagerMock
            .Setup( s => s.InitializeProviderStatesAsync( TestSagaId, It.IsAny<IEnumerable<SupportedProviders>>( ), It.IsAny<CancellationToken>( ) ) )
            .Callback( ( ) => callOrder.Add( "InitializeProviderStates" ) )
            .Returns( Task.CompletedTask );
        _ = _sagaManagerMock
            .Setup( s => s.SetIsPartialAsync( TestSagaId, true, It.IsAny<CancellationToken>( ) ) )
            .Callback( ( ) => callOrder.Add( "SetIsPartial" ) )
            .Returns( Task.CompletedTask );
        _ = _sagaManagerMock
            .Setup( s => s.TryMarkSecondariesQueuedAsync( TestSagaId, It.IsAny<CancellationToken>( ) ) )
            .Callback( ( ) => callOrder.Add( "TryMarkSecondariesQueued" ) )
            .ReturnsAsync( true );

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

        // Assert - The idempotent writes both precede the marker claim
        CollectionAssert.AreEqual(
            new List<string> { "InitializeProviderStates", "SetIsPartial", "TryMarkSecondariesQueued" },
            callOrder
        );
    }

    /// <summary>
    /// Verifies the terminal-state contract when every secondary enqueue throws: both enqueues are
    /// attempted, the saga is not finalized (the one-provider result is not promoted to final), the
    /// partial result is written for waiting callers, and the saga is immediately removed from the
    /// reconciliation pending index so it does not strand to TTL expiry.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task SagaCompletion_WhenEveryEnqueueFails_ReachesTerminalStateAndWritesPartial( ) {
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

        // Every secondary enqueue fails (e.g. the queue backend is unavailable)
        Mock<IRequestQueue<QueuedLookupRequest>> queueMock = new( );
        _ = queueMock
            .Setup( q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ) )
            .ThrowsAsync( new InvalidOperationException( "queue unavailable" ) );
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

        // Assert - All enqueues were attempted (AppleMusic + Tidal) and failed
        queueMock.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ),
            Times.Exactly( 2 )
        );

        // Assert - The saga is NOT finalized: the one-provider result must not be promoted to
        // final while secondary providers are still unresolved
        _sagaManagerMock.Verify(
            s => s.SetFinalResultUriAsync( It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );

        // Assert - The partial result is written for waiting callers instead
        _atProtoStorageMock.Verify(
            a => a.StoreMediaLinkResultAsync( It.Is<MediaLinkResult>( r => r.IsPartial ), It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
        _sagaManagerMock.Verify(
            s => s.SetIsPartialAsync( TestSagaId, true, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );

        // Assert - The saga is removed from the pending index so it does not strand to TTL expiry
        _sagaManagerMock.Verify(
            s => s.RemoveFromPendingIndexAsync( TestSagaId, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
    }

    /// <summary>
    /// Negative control: a saga with at least one successful secondary enqueue stays partial and in
    /// the pending index, awaiting its secondary providers. It must NOT reach the terminal state
    /// reserved for the all-enqueues-failed path.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task SagaCompletion_WhenSomeEnqueueSucceeds_StaysPartialAwaitingSecondaries( ) {
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

        // One provider enqueues successfully; the other fails
        Mock<IRequestQueue<QueuedLookupRequest>> successQueueMock = new( );
        _ = successQueueMock
            .Setup( q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );

        Mock<IRequestQueue<QueuedLookupRequest>> failQueueMock = new( );
        _ = failQueueMock
            .Setup( q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ) )
            .ThrowsAsync( new InvalidOperationException( "queue unavailable" ) );

        // AppleMusic succeeds, Tidal fails
        _ = _queueResolverMock
            .Setup( r => r.GetQueue( SupportedProviders.AppleMusic ) )
            .Returns( successQueueMock.Object );
        _ = _queueResolverMock
            .Setup( r => r.GetQueue( SupportedProviders.Tidal ) )
            .Returns( failQueueMock.Object );

        _ = _atProtoStorageMock.Setup( a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        _ = _cacheRepositoryMock.Setup( c => c.CacheResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        // Act
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

        // Assert - Not finalized: secondaries are still pending
        _sagaManagerMock.Verify(
            s => s.SetFinalResultUriAsync( It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );

        // Assert - The partial result is written so waiting callers receive a response
        _atProtoStorageMock.Verify(
            a => a.StoreMediaLinkResultAsync( It.Is<MediaLinkResult>( r => r.IsPartial ), It.IsAny<CancellationToken>( ) ),
            Times.Once
        );

        // Assert - The pending index is NOT cleared: at least one provider is in flight
        _sagaManagerMock.Verify(
            s => s.RemoveFromPendingIndexAsync( TestSagaId, It.IsAny<CancellationToken>( ) ),
            Times.Never
        );
    }

    /// <summary>
    /// Verifies that the same secondary-lookup behavior is reached through the polling path (not just
    /// the subscription): a URI saga picked up by polling that still needs secondaries queues both at
    /// interactive priority and writes a partial result instead of finalizing.
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

    #region Finalize Claim Tests

    /// <summary>
    /// Verifies that a handler that loses the finalize claim performs no PDS write and does not
    /// call SetFinalResultUriAsync. The loser must be completely silent.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task Finalize_WhenClaimLost_DoesNotWrite( ) {
        // Arrange
        SagaCoordinatorBackgroundService service = CreateService( );
        LookupSagaState completeSaga = CreateCompleteSaga( );

        _ = _sagaManagerMock.Setup( s => s.GetCompletedButUnfinalizedAsync(
                It.IsAny<TimeSpan>( ),
                It.IsAny<int>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( [completeSaga] );

        // The claim is already held by another handler
        _ = _sagaManagerMock
            .Setup( s => s.TryClaimFinalizeAsync( It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( false );

        // Act
        using CancellationTokenSource cts = new( );
        _ = service.StartAsync( cts.Token );
        await Task.Delay( 250, TestContext.CancellationToken );
        await cts.CancelAsync( );

        try {
            await service.StopAsync( CancellationToken.None );
        } catch (OperationCanceledException) {
            // Expected
        }

        // Assert - No PDS write (the loser must be completely silent)
        _atProtoStorageMock.Verify(
            a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );

        // Assert - No final URI recorded
        _sagaManagerMock.Verify(
            s => s.SetFinalResultUriAsync( It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );

        // Assert - No dedup release (the loser must not publish on complete:{key})
        _deduplicatorMock.Verify(
            d => d.ReleaseAsync( It.IsAny<string>( ), It.IsAny<string?>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );
    }

    /// <summary>
    /// Verifies that a handler that wins the finalize claim writes to the PDS exactly once and
    /// records the final URI. This is the companion positive control for the loser test above.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task Finalize_WhenClaimWon_WritesOnce( ) {
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
        await Task.Delay( 250, TestContext.CancellationToken );
        await cts.CancelAsync( );

        try {
            await service.StopAsync( CancellationToken.None );
        } catch (OperationCanceledException) {
            // Expected
        }

        // Assert - Exactly one PDS write
        _atProtoStorageMock.Verify(
            a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ),
            Times.Once
        );

        // Assert - Final URI recorded
        _sagaManagerMock.Verify(
            s => s.SetFinalResultUriAsync( completeSaga.SagaId, TestRecordUri, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );

        // Assert - The claim is NOT released on the success path
        _sagaManagerMock.Verify(
            s => s.ReleaseFinalizeClaimAsync( It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );
    }

    /// <summary>
    /// Verifies that a failed PDS write releases the finalize claim so the next poll cycle can
    /// retry. The dedup lock is released with null to unblock waiters.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task Finalize_WhenPdsWriteFails_ReleasesClaimAndDedup( ) {
        // Arrange
        SagaCoordinatorBackgroundService service = CreateService( );
        LookupSagaState completeSaga = CreateCompleteSaga( );

        _ = _sagaManagerMock.Setup( s => s.GetCompletedButUnfinalizedAsync(
                It.IsAny<TimeSpan>( ),
                It.IsAny<int>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( [completeSaga] );

        // PDS write fails
        _ = _atProtoStorageMock
            .Setup( a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ThrowsAsync( new InvalidOperationException( "PDS unavailable" ) );

        // Act
        using CancellationTokenSource cts = new( );
        _ = service.StartAsync( cts.Token );
        await Task.Delay( 250, TestContext.CancellationToken );
        await cts.CancelAsync( );

        try {
            await service.StopAsync( CancellationToken.None );
        } catch (OperationCanceledException) {
            // Expected
        }

        // Assert - Claim released so the next poll can retry
        _sagaManagerMock.Verify(
            s => s.ReleaseFinalizeClaimAsync( completeSaga.SagaId, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );

        // Assert - Dedup lock released with null to unblock waiters
        _deduplicatorMock.Verify(
            d => d.ReleaseAsync( completeSaga.LookupKey, null, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
    }

    /// <summary>
    /// Verifies that a re-delivery of a saga whose finalization already succeeded is a no-op:
    /// the existing final result URI short-circuits before the claim is even attempted.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task Completion_WhenSagaAlreadyFinalized_IsNoOp( ) {
        // Arrange
        Dictionary<string, Action<RedisChannel, RedisValue>> handlers = [];
        _ = _subscriberMock
            .Setup( s => s.SubscribeAsync( It.IsAny<RedisChannel>( ), It.IsAny<Action<RedisChannel, RedisValue>>( ), It.IsAny<CommandFlags>( ) ) )
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>(
                ( channel, handler, _ ) => handlers[channel.ToString( )] = handler )
            .Returns( Task.CompletedTask );

        SagaCoordinatorBackgroundService service = CreateService( );

        // Saga already has a final result URI (already finalized)
        LookupSagaState alreadyFinalizedSaga = CreateCompleteSaga( ) with {
            FinalResultUri = TestRecordUri
        };

        _ = _sagaManagerMock.Setup( s => s.GetAsync( TestSagaId, It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( alreadyFinalizedSaga );

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

        // Simulate re-delivery of the saga:completed event
        Assert.IsTrue( handlers.ContainsKey( "saga:completed" ), "Service should have subscribed to saga:completed" );
        handlers["saga:completed"]( RedisChannel.Literal( "saga:completed" ), TestSagaId );
        await Task.Delay( 250, TestContext.CancellationToken );

        await cts.CancelAsync( );
        try {
            await service.StopAsync( CancellationToken.None );
        } catch (OperationCanceledException) {
            // Expected
        }

        // Assert - No second PDS write
        _atProtoStorageMock.Verify(
            a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );

        // Assert - No second final URI write
        _sagaManagerMock.Verify(
            s => s.SetFinalResultUriAsync( It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );

        // Assert - No second dedup release
        _deduplicatorMock.Verify(
            d => d.ReleaseAsync( It.IsAny<string>( ), It.IsAny<string?>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );
    }

    /// <summary>
    /// Negative control for <see cref="Completion_WhenSagaAlreadyFinalized_IsNoOp"/>: verifies
    /// that the first delivery DOES finalize once, proving the no-op path is not over-suppressing.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task FirstDelivery_OfCompletedSaga_FinalizesOnce( ) {
        // Arrange
        Dictionary<string, Action<RedisChannel, RedisValue>> handlers = [];
        _ = _subscriberMock
            .Setup( s => s.SubscribeAsync( It.IsAny<RedisChannel>( ), It.IsAny<Action<RedisChannel, RedisValue>>( ), It.IsAny<CommandFlags>( ) ) )
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>(
                ( channel, handler, _ ) => handlers[channel.ToString( )] = handler )
            .Returns( Task.CompletedTask );

        SagaCoordinatorBackgroundService service = CreateService( );
        LookupSagaState completeSaga = CreateCompleteSaga( );

        _ = _sagaManagerMock.Setup( s => s.GetAsync( TestSagaId, It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( completeSaga );

        _ = _sagaManagerMock.Setup( s => s.GetCompletedButUnfinalizedAsync(
                It.IsAny<TimeSpan>( ),
                It.IsAny<int>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( [] );

        _ = _atProtoStorageMock.Setup( a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        _ = _cacheRepositoryMock.Setup( c => c.CacheResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        // Act
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

        // Assert - Exactly one PDS write on first delivery
        _atProtoStorageMock.Verify(
            a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
    }

    /// <summary>
    /// Verifies that a failure occurring after the final result URI has been durably recorded does
    /// NOT release the finalize claim. Retaining the claim prevents a re-entrant PDS write against
    /// a URI that was already written. Pairs with
    /// <see cref="Finalize_WhenPdsWriteFails_ReleasesClaimAndDedup"/> (pre-URI failure) as a
    /// discriminator: together they prove the release is conditioned on the durability line, not
    /// unconditional.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task Finalize_WhenFailureAfterUriRecorded_DoesNotReleaseClaim( ) {
        // Arrange
        SagaCoordinatorBackgroundService service = CreateService( );
        LookupSagaState completeSaga = CreateCompleteSaga( );

        _ = _sagaManagerMock.Setup( s => s.GetCompletedButUnfinalizedAsync(
                It.IsAny<TimeSpan>( ),
                It.IsAny<int>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( [completeSaga] );

        // PDS write succeeds and returns a URI
        _ = _atProtoStorageMock
            .Setup( a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        // SetFinalResultUriAsync succeeds (URI is now durably recorded)
        _ = _sagaManagerMock
            .Setup( s => s.SetFinalResultUriAsync( It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );

        // The first post-URI step throws — simulates a failure after the durability line
        _ = _sagaManagerMock
            .Setup( s => s.SetIsPartialAsync( It.IsAny<string>( ), It.IsAny<bool>( ), It.IsAny<CancellationToken>( ) ) )
            .ThrowsAsync( new InvalidOperationException( "post-URI step failed" ) );

        // Act
        using CancellationTokenSource cts = new( );
        _ = service.StartAsync( cts.Token );
        await Task.Delay( 250, TestContext.CancellationToken );
        await cts.CancelAsync( );

        try {
            await service.StopAsync( CancellationToken.None );
        } catch (OperationCanceledException) {
            // Expected
        }

        // Assert - A failure after the URI is recorded must retain the claim (no release)
        _sagaManagerMock.Verify(
            s => s.ReleaseFinalizeClaimAsync( It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );
    }

    /// <summary>
    /// Verifies that a cancellation mid-finalize (before the PDS write returns) releases the
    /// finalize claim so the next host restart can re-finalize, but does NOT release the dedup lock
    /// with a null URI (which would hand waiters a stale empty completion for a healthy saga) and
    /// does NOT delete the saga.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task Finalize_WhenCancelledMidFinalize_ReleasesClaimButNotDedup( ) {
        // Arrange
        SagaCoordinatorBackgroundService service = CreateService( );
        LookupSagaState completeSaga = CreateCompleteSaga( );

        _ = _sagaManagerMock.Setup( s => s.GetCompletedButUnfinalizedAsync(
                It.IsAny<TimeSpan>( ),
                It.IsAny<int>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( [completeSaga] );

        // Cancellation fires before the PDS write completes (before the durability line)
        _ = _atProtoStorageMock
            .Setup( a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ThrowsAsync( new OperationCanceledException( ) );

        _ = _sagaManagerMock
            .Setup( s => s.ReleaseFinalizeClaimAsync( It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );

        // Act
        using CancellationTokenSource cts = new( );
        _ = service.StartAsync( cts.Token );
        await Task.Delay( 250, TestContext.CancellationToken );
        await cts.CancelAsync( );

        try {
            await service.StopAsync( CancellationToken.None );
        } catch (OperationCanceledException) {
            // Expected
        }

        // Assert - Claim must be released so the next host can re-finalize
        _sagaManagerMock.Verify(
            s => s.ReleaseFinalizeClaimAsync( It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Once
        );

        // Assert - Dedup lock must NOT be released with null (no premature empty completion)
        _deduplicatorMock.Verify(
            d => d.ReleaseAsync( It.IsAny<string>( ), null, It.IsAny<CancellationToken>( ) ),
            Times.Never
        );

        // Assert - Saga must not be deleted
        _sagaManagerMock.Verify(
            s => s.DeleteAsync( It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );
    }

    /// <summary>
    /// Verifies that a cancellation arriving after the final result URI is durably recorded does NOT
    /// release the finalize claim. Retaining the claim prevents a re-entrant PDS write against an
    /// already-recorded URI. The dedup lock is also never released here. Pairs with
    /// <see cref="Finalize_WhenCancelledMidFinalize_ReleasesClaimButNotDedup"/> (pre-URI cancellation
    /// → claim released) to prove the cancellation catch is conditioned on the durability line.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task Finalize_WhenCancelledAfterUriRecorded_DoesNotReleaseClaim( ) {
        // Arrange
        SagaCoordinatorBackgroundService service = CreateService( );
        LookupSagaState completeSaga = CreateCompleteSaga( );

        _ = _sagaManagerMock.Setup( s => s.GetCompletedButUnfinalizedAsync(
                It.IsAny<TimeSpan>( ),
                It.IsAny<int>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( [completeSaga] );

        // PDS write succeeds — URI returned and durably recorded
        _ = _atProtoStorageMock
            .Setup( a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        // SetFinalResultUriAsync completes — durability line crossed
        _ = _sagaManagerMock
            .Setup( s => s.SetFinalResultUriAsync( It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );

        // The first post-URI step throws OperationCanceledException — cancellation after the durability line
        _ = _sagaManagerMock
            .Setup( s => s.SetIsPartialAsync( It.IsAny<string>( ), It.IsAny<bool>( ), It.IsAny<CancellationToken>( ) ) )
            .ThrowsAsync( new OperationCanceledException( ) );

        // Act
        using CancellationTokenSource cts = new( );
        _ = service.StartAsync( cts.Token );
        await Task.Delay( 250, TestContext.CancellationToken );
        await cts.CancelAsync( );

        try {
            await service.StopAsync( CancellationToken.None );
        } catch (OperationCanceledException) {
            // Expected
        }

        // Assert - Claim must NOT be released; releasing would allow a re-entrant PDS write against
        // a URI that is already recorded
        _sagaManagerMock.Verify(
            s => s.ReleaseFinalizeClaimAsync( It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );

        // Assert - Dedup lock must NOT be released with null (saga succeeded; no premature empty completion)
        _deduplicatorMock.Verify(
            d => d.ReleaseAsync( It.IsAny<string>( ), null, It.IsAny<CancellationToken>( ) ),
            Times.Never
        );

        // Assert - Saga must not be deleted
        _sagaManagerMock.Verify(
            s => s.DeleteAsync( It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );
    }

    /// <summary>
    /// Verifies two distinct sagas each finalize exactly once. This proves the single-winner
    /// guard keys on saga identity and does not suppress legitimate distinct finalizations.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task Finalize_TwoDistinctSagas_EachWriteOnce( ) {
        // Arrange
        const string SagaId2 = "test-saga-id-87654321";

        SagaCoordinatorBackgroundService service = CreateService( );

        LookupSagaState saga1 = CreateCompleteSaga( );
        LookupSagaState saga2 = CreateCompleteSaga( ) with { SagaId = SagaId2 };

        _ = _sagaManagerMock.Setup( s => s.GetCompletedButUnfinalizedAsync(
                It.IsAny<TimeSpan>( ),
                It.IsAny<int>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( [saga1, saga2] );

        _ = _atProtoStorageMock.Setup( a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        _ = _cacheRepositoryMock.Setup( c => c.CacheResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( TestRecordUri );

        // Act - polling processes both sagas
        using CancellationTokenSource cts = new( );
        _ = service.StartAsync( cts.Token );
        await Task.Delay( 250, TestContext.CancellationToken );
        await cts.CancelAsync( );

        try {
            await service.StopAsync( CancellationToken.None );
        } catch (OperationCanceledException) {
            // Expected
        }

        // Assert - Each saga produces exactly one PDS write (two total)
        _atProtoStorageMock.Verify(
            a => a.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ), It.IsAny<CancellationToken>( ) ),
            Times.Exactly( 2 )
        );
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Builds a service wired to all the mocks, the real combiner, and the enabled-providers set.
    /// </summary>
    /// <returns>A service under test.</returns>
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
    /// Builds a URI-lookup saga whose only resolved provider is Spotify (with a sample result JSON),
    /// used for secondary-lookup tests. The origin priority is parameterized so tests can assert the
    /// priority secondaries inherit.
    /// </summary>
    /// <param name="originPriority">The saga's origin priority; defaults to interactive.</param>
    /// <returns>A URI-lookup saga with one resolved primary provider.</returns>
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
    /// Builds a cached <c>MediaLinkResult</c> carrying a sample result for each of the given
    /// providers, used to simulate cache hits for secondary providers in the materialize-from-cache
    /// tests.
    /// </summary>
    /// <param name="providers">The providers to include in the cached result.</param>
    /// <returns>A cached result populated for each requested provider.</returns>
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
    /// Builds a complete ISRC-lookup saga with a single successful Spotify provider state (carrying
    /// a sample result JSON), used as the baseline fixture for the polling and finalization tests;
    /// individual tests adjust it via <c>with</c> expressions.
    /// </summary>
    /// <returns>A complete, finalizable saga.</returns>
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
