using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Exceptions;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Domain.Services.Queue;
using BridgeBeats.Core.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="QueueProcessorBackgroundService"/>, the worker-side queue consumer
/// (one instance per provider). Drive the dequeue-process-ack loop against mocked queue, rate-limit
/// tracker, saga manager, lookup service, and Redis subscriber dependencies, and verify:
/// constructor null-guards; endpoint rate-limit pre-check and delayed requeue; <c>LookupRequestType</c>
/// routing onto the right <see cref="IMusicLookupService"/> method; saga-state update, ack, and
/// <c>saga:completed</c> publication on success; <see cref="RetryAfterExceededException"/> handling
/// (mark partial, merge rate-limit info, publish sentinel, re-enqueue at Background); the
/// interactive-to-background deferral log; and generic-exception retry-then-DLQ semantics.
/// </summary>
[TestClass]
public class QueueProcessorBackgroundServiceTests {
    /// <summary>Mocked Redis multiplexer; returns <see cref="_subscriberMock"/> for pub/sub.</summary>
    private Mock<IConnectionMultiplexer> _redisMock = null!;
    /// <summary>Mocked Redis subscriber used to verify completion-event publication.</summary>
    private Mock<ISubscriber> _subscriberMock = null!;
    /// <summary>Mocked request queue (dequeue, ack, requeue, enqueue, DLQ).</summary>
    private Mock<IRequestQueue<QueuedLookupRequest>> _queueMock = null!;
    /// <summary>Mocked rate-limit tracker driving the endpoint pre-check.</summary>
    private Mock<IRateLimitTracker> _rateLimitTrackerMock = null!;
    /// <summary>Mocked saga-state manager (get/create, provider-state update, rate-limit info).</summary>
    private Mock<ISagaStateManager> _sagaManagerMock = null!;
    /// <summary>Mocked per-provider lookup service whose method calls the routing tests assert.</summary>
    private Mock<IMusicLookupService> _lookupServiceMock = null!;
    /// <summary>Mocked logger used to assert the interactive-deferral log event.</summary>
    private Mock<ILogger<QueueProcessorBackgroundService>> _loggerMock = null!;

    /// <summary>The provider this service instance is bound to for all tests (<see cref="SupportedProviders.Spotify"/>).</summary>
    private const SupportedProviders TestProvider = SupportedProviders.Spotify;

    /// <summary>MSTest-injected context, used here for per-test cancellation tokens.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Shared camelCase serializer options matching the saga result-JSON wire format.</summary>
    private static readonly JsonSerializerOptions s_jsonOptions = new( ) {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>Builds fresh dependency mocks and wires the subscriber before each test.</summary>
    [TestInitialize]
    public void Initialize( ) {
        _redisMock = new Mock<IConnectionMultiplexer>( );
        _subscriberMock = new Mock<ISubscriber>( );
        _queueMock = new Mock<IRequestQueue<QueuedLookupRequest>>( );
        _rateLimitTrackerMock = new Mock<IRateLimitTracker>( );
        _sagaManagerMock = new Mock<ISagaStateManager>( );
        _lookupServiceMock = new Mock<IMusicLookupService>( );
        _loggerMock = new Mock<ILogger<QueueProcessorBackgroundService>>( );

        _ = _redisMock.Setup( r => r.GetSubscriber( It.IsAny<object>( ) ) ).Returns( _subscriberMock.Object );
    }

    #region Constructor Tests

    /// <summary>The constructor builds an instance when all dependencies are supplied.</summary>
    [TestMethod]
    public void Constructor_WithValidDependencies_ShouldCreateInstance( ) {
        // Act
        QueueProcessorBackgroundService service = CreateService( );

        // Assert
        Assert.IsNotNull( service );
    }

    /// <summary>A null Redis multiplexer throws <see cref="ArgumentNullException"/>.</summary>
    [TestMethod]
    public void Constructor_WithNullRedis_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new QueueProcessorBackgroundService(
                null!,
                _queueMock.Object,
                _rateLimitTrackerMock.Object,
                _sagaManagerMock.Object,
                _lookupServiceMock.Object,
                TestProvider,
                _loggerMock.Object
            )
        );
    }

    /// <summary>A null queue throws <see cref="ArgumentNullException"/>.</summary>
    [TestMethod]
    public void Constructor_WithNullQueue_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new QueueProcessorBackgroundService(
                _redisMock.Object,
                null!,
                _rateLimitTrackerMock.Object,
                _sagaManagerMock.Object,
                _lookupServiceMock.Object,
                TestProvider,
                _loggerMock.Object
            )
        );
    }

    /// <summary>A null rate-limit tracker throws <see cref="ArgumentNullException"/>.</summary>
    [TestMethod]
    public void Constructor_WithNullRateLimitTracker_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new QueueProcessorBackgroundService(
                _redisMock.Object,
                _queueMock.Object,
                null!,
                _sagaManagerMock.Object,
                _lookupServiceMock.Object,
                TestProvider,
                _loggerMock.Object
            )
        );
    }

    /// <summary>A null saga-state manager throws <see cref="ArgumentNullException"/>.</summary>
    [TestMethod]
    public void Constructor_WithNullSagaManager_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new QueueProcessorBackgroundService(
                _redisMock.Object,
                _queueMock.Object,
                _rateLimitTrackerMock.Object,
                null!,
                _lookupServiceMock.Object,
                TestProvider,
                _loggerMock.Object
            )
        );
    }

    /// <summary>A null lookup service throws <see cref="ArgumentNullException"/>.</summary>
    [TestMethod]
    public void Constructor_WithNullLookupService_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new QueueProcessorBackgroundService(
                _redisMock.Object,
                _queueMock.Object,
                _rateLimitTrackerMock.Object,
                _sagaManagerMock.Object,
                null!,
                TestProvider,
                _loggerMock.Object
            )
        );
    }

    /// <summary>A null logger throws <see cref="ArgumentNullException"/>.</summary>
    [TestMethod]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new QueueProcessorBackgroundService(
                _redisMock.Object,
                _queueMock.Object,
                _rateLimitTrackerMock.Object,
                _sagaManagerMock.Object,
                _lookupServiceMock.Object,
                TestProvider,
                null!
            )
        );
    }

    #endregion

    #region Message Processing Tests

    /// <summary>
    /// When the endpoint is not rate-limited, processing a dequeued message invokes the matching
    /// lookup-service method (here <c>GetInfoByISRCAsync</c>) exactly once.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WhenEndpointNotRateLimited_ShouldCallLookupService( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" );
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        SetupLookupSuccess( );
        SetupSagaNotComplete( request.SagaId );

        // Setup queue to return one message then null (to exit loop)
        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act - Start and quickly cancel
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken ); // Give time for processing
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _lookupServiceMock.Verify(
            l => l.GetInfoByISRCAsync( "US1234567890" ),
            Times.Once
        );
    }

    /// <summary>A missing native id falls back to the external id within the same provider leg.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_NativeIdNotFound_UsesExternalIdFallback( ) {
        QueuedLookupRequest request = CreateRequest( LookupRequestType.SongIdLookup, "stale-native-id" ) with {
            FallbackLookupType = LookupRequestType.IsrcLookup,
            FallbackLookupValue = "USRC12345678"
        };
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        SetupSagaNotComplete( request.SagaId );
        _ = _lookupServiceMock.Setup( service => service.GetInfoByIDAsync( "stale-native-id", false ) )
            .ReturnsAsync( (MusicLookupResult?)null );
        _ = _lookupServiceMock.Setup( service => service.GetInfoByISRCAsync( "USRC12345678" ) )
            .ReturnsAsync( CreateLookupResult( ) );

        int dequeueCount = 0;
        _ = _queueMock.Setup( queue => queue.DequeueAsync(
                It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => dequeueCount++ == 0 ? message : null );

        QueueProcessorBackgroundService service = CreateService( );
        using CancellationTokenSource cts = new( );
        _ = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        _lookupServiceMock.Verify( lookup => lookup.GetInfoByIDAsync( "stale-native-id", false ), Times.Once );
        _lookupServiceMock.Verify( lookup => lookup.GetInfoByISRCAsync( "USRC12345678" ), Times.Once );
        _sagaManagerMock.Verify( manager => manager.UpdateProviderStateAsync(
            request.SagaId,
            It.Is<ProviderLookupState>( state => state.IsComplete && state.IsSuccess ),
            It.IsAny<CancellationToken>( ) ), Times.Once );
    }

    /// <summary>The stored storefront is applied to both the native attempt and its fallback.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WithStorefront_UsesStorefrontCapabilityForBothAttempts( ) {
        Mock<IStorefrontMusicLookupService> storefrontLookup = new( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.SongIdLookup, "old-id" ) with {
            Storefront = "jp",
            FallbackLookupType = LookupRequestType.IsrcLookup,
            FallbackLookupValue = "JPABC1234567"
        };
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        SetupSagaNotComplete( request.SagaId );
        _ = storefrontLookup.Setup( lookup => lookup.GetInfoByIDAsync( "old-id", false, "jp" ) )
            .ReturnsAsync( (MusicLookupResult?)null );
        _ = storefrontLookup.Setup( lookup => lookup.GetInfoByISRCAsync( "JPABC1234567", "jp" ) )
            .ReturnsAsync( CreateLookupResult( ) );

        int dequeueCount = 0;
        _ = _queueMock.Setup( queue => queue.DequeueAsync(
                It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => dequeueCount++ == 0 ? message : null );

        QueueProcessorBackgroundService service = CreateService( storefrontLookup.Object );
        using CancellationTokenSource cts = new( );
        _ = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        storefrontLookup.Verify( lookup => lookup.GetInfoByIDAsync( "old-id", false, "jp" ), Times.Once );
        storefrontLookup.Verify( lookup => lookup.GetInfoByISRCAsync( "JPABC1234567", "jp" ), Times.Once );
        storefrontLookup.Verify( lookup => lookup.GetInfoByIDAsync( It.IsAny<string>( ), It.IsAny<bool>( ) ), Times.Never );
    }

    /// <summary>
    /// A request carrying <see cref="QueuePriority.Bulk"/> origin priority forwards that priority to
    /// <c>GetOrCreateAsync</c> so the saga records its origin lane.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WithBulkOriginPriority_ShouldPassOriginPriorityToSagaCreation( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" ) with {
            OriginPriority = QueuePriority.Bulk
        };
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        SetupLookupSuccess( );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _sagaManagerMock.Verify(
            s => s.GetOrCreateAsync(
                request.SagaId,
                It.IsAny<string>( ),
                request.LookupType,
                request.LookupValue,
                It.Is<QueuePriority?>( p => p == QueuePriority.Bulk ),
                It.IsAny<CancellationToken>( )
            ),
            Times.Once
        );
    }

    /// <summary>
    /// When the endpoint pre-check reports the provider rate-limited, the message is requeued with
    /// the remaining delay and the lookup service is never called.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WhenEndpointRateLimited_ShouldRequeueWithDelay( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" );
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        TimeSpan timeRemaining = TimeSpan.FromSeconds( 30 );
        SetupRateLimited( timeRemaining );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _queueMock.Verify(
            q => q.RequeueAsync( message.MessageId, timeRemaining, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
        // Should NOT call lookup service
        _lookupServiceMock.Verify(
            l => l.GetInfoByISRCAsync( It.IsAny<string>( ) ),
            Times.Never
        );
    }

    /// <summary>
    /// A successful lookup writes a complete, successful provider state into the saga via
    /// <c>UpdateProviderStateAsync</c>.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WithSuccessfulLookup_ShouldUpdateSagaState( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" );
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        SetupLookupSuccess( );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _sagaManagerMock.Verify(
            s => s.UpdateProviderStateAsync(
                request.SagaId,
                It.Is<ProviderLookupState>( state =>
                    state.Provider == TestProvider &&
                    state.IsComplete &&
                    state.IsSuccess
                ),
                It.IsAny<CancellationToken>( )
            ),
            Times.Once
        );
    }

    /// <summary>A successful lookup acknowledges the originating message exactly once.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WithSuccessfulLookup_ShouldAcknowledgeMessage( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" );
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        SetupLookupSuccess( );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _queueMock.Verify(
            q => q.AcknowledgeAsync( message.MessageId, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
    }

    /// <summary>
    /// When the lookup completes the whole saga, the service publishes the saga id on the
    /// <c>saga:completed</c> channel.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WhenSagaCompletes_ShouldPublishCompletionEvent( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" );
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        SetupLookupSuccess( );
        SetupSagaComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert - Should publish to saga:completed channel
        _subscriberMock.Verify(
            s => s.PublishAsync(
                It.Is<RedisChannel>( c => c.ToString( ) == "saga:completed" ),
                It.Is<RedisValue>( v => v.ToString( ) == request.SagaId ),
                It.IsAny<CommandFlags>( )
            ),
            Times.Once
        );
    }

    #endregion

    #region Lookup Type Routing Tests

    /// <summary>
    /// A <see cref="LookupRequestType.UriLookup"/> request routes to <c>GetInfoAsync(url)</c>.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WithUriLookup_ShouldCallGetInfoAsyncWithUri( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        string testUrl = "https://open.spotify.com/track/123";
        QueuedLookupRequest request = CreateRequest( LookupRequestType.UriLookup, testUrl );
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoAsync( testUrl ) )
            .ReturnsAsync( CreateLookupResult( ) );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _lookupServiceMock.Verify( l => l.GetInfoAsync( testUrl ), Times.Once );
    }

    /// <summary>
    /// A <see cref="LookupRequestType.UpcLookup"/> request routes to <c>GetInfoByUPCAsync</c>.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WithUpcLookup_ShouldCallGetInfoByUPCAsync( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        string testUpc = "123456789012";
        QueuedLookupRequest request = CreateRequest( LookupRequestType.UpcLookup, testUpc );
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoByUPCAsync( testUpc ) )
            .ReturnsAsync( CreateLookupResult( ) );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _lookupServiceMock.Verify( l => l.GetInfoByUPCAsync( testUpc ), Times.Once );
    }

    /// <summary>
    /// A <see cref="LookupRequestType.SongIdLookup"/> request routes to <c>GetInfoByIDAsync(id, false)</c>
    /// (<c>isAlbum: false</c>).
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WithSongIdLookup_ShouldCallGetInfoByIDAsync( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        string testId = "spotify-track-id";
        QueuedLookupRequest request = CreateRequest( LookupRequestType.SongIdLookup, testId );
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoByIDAsync( testId, false ) )
            .ReturnsAsync( CreateLookupResult( ) );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _lookupServiceMock.Verify( l => l.GetInfoByIDAsync( testId, false ), Times.Once );
    }

    /// <summary>
    /// A <see cref="LookupRequestType.AlbumIdLookup"/> request routes to <c>GetInfoByIDAsync(id, true)</c>
    /// (<c>isAlbum: true</c>).
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WithAlbumIdLookup_ShouldCallGetInfoByIDAsyncWithIsAlbumTrue( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        string testId = "spotify-album-id";
        QueuedLookupRequest request = CreateRequest( LookupRequestType.AlbumIdLookup, testId );
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoByIDAsync( testId, true ) )
            .ReturnsAsync( CreateLookupResult( ) );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _lookupServiceMock.Verify( l => l.GetInfoByIDAsync( testId, true ), Times.Once );
    }

    /// <summary>
    /// A <see cref="LookupRequestType.SongLookup"/> request routes to <c>GetInfoAsync(title, artist)</c>,
    /// drawing the title and artist from the request rather than its lookup value.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WithSongLookup_ShouldCallGetInfoAsyncWithTitleArtist( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.SongLookup, "ignored" ) with {
            Title = "Test Song",
            Artist = "Test Artist"
        };
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoAsync( "Test Song", "Test Artist" ) )
            .ReturnsAsync( CreateLookupResult( ) );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _lookupServiceMock.Verify( l => l.GetInfoAsync( "Test Song", "Test Artist" ), Times.Once );
    }

    /// <summary>
    /// A <see cref="LookupRequestType.ArtistLookup"/> request routes to <c>GetInfoAsync(title, artist)</c>,
    /// drawing the title and artist from the request.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task PerformLookup_WithArtistLookup_ShouldCallGetInfoAsyncWithTitleArtist( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.ArtistLookup, "ignored" ) with {
            Title = "Test Album",
            Artist = "Test Artist"
        };
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoAsync( "Test Album", "Test Artist" ) )
            .ReturnsAsync( CreateLookupResult( ) );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert: ArtistLookup arm routes to GetInfoAsync(title, artist)
        _lookupServiceMock.Verify( l => l.GetInfoAsync( "Test Album", "Test Artist" ), Times.Once );
        // Negative discriminator: URI lookup is not called
        _lookupServiceMock.Verify( l => l.GetInfoAsync( It.IsAny<string>( ) ), Times.Never );
    }

    /// <summary>
    /// A <see cref="LookupRequestType.ArtistAlbumLookup"/> request with title and artist present
    /// routes to <c>GetInfoAsync(title, artist)</c>, which performs the artist-to-album expansion
    /// internally. This verifies that the arm dispatches through the title+artist path and does not
    /// throw.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task PerformLookup_WithArtistAlbumLookupAndTitleArtist_ShouldCallGetInfoAsyncWithTitleArtist( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.ArtistAlbumLookup, "ignored" ) with {
            Title = "Test Album",
            Artist = "Test Artist"
        };
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoAsync( "Test Album", "Test Artist" ) )
            .ReturnsAsync( CreateLookupResult( ) );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act: test failed before implementation (threw NotImplementedException); passes after
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert: dispatches to GetInfoAsync(title, artist) — does not error-path
        _lookupServiceMock.Verify( l => l.GetInfoAsync( "Test Album", "Test Artist" ), Times.Once );
        // Discriminator: single-arg URI overload must not be called (only title+artist overload is used)
        _lookupServiceMock.Verify( l => l.GetInfoAsync( It.IsAny<string>( ) ), Times.Never );
        _sagaManagerMock.Verify(
            s => s.UpdateProviderStateAsync(
                request.SagaId,
                It.Is<ProviderLookupState>( state => state.IsSuccess ),
                It.IsAny<CancellationToken>( )
            ),
            Times.Once );
    }

    /// <summary>
    /// A <see cref="LookupRequestType.AlbumTrackLookup"/> request with title and artist present
    /// routes to <c>GetInfoAsync(title, artist)</c>, which performs the album-to-track expansion
    /// internally. This verifies that the arm dispatches through the title+artist path and does not
    /// throw.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task PerformLookup_WithAlbumTrackLookupAndTitleArtist_ShouldCallGetInfoAsyncWithTitleArtist( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.AlbumTrackLookup, "ignored" ) with {
            Title = "Test Track",
            Artist = "Test Artist"
        };
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoAsync( "Test Track", "Test Artist" ) )
            .ReturnsAsync( CreateLookupResult( ) );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act: test failed before implementation (threw NotImplementedException); passes after
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert: dispatches to GetInfoAsync(title, artist) — does not error-path
        _lookupServiceMock.Verify( l => l.GetInfoAsync( "Test Track", "Test Artist" ), Times.Once );
        // Discriminator: single-arg URI overload must not be called (only title+artist overload is used)
        _lookupServiceMock.Verify( l => l.GetInfoAsync( It.IsAny<string>( ) ), Times.Never );
        _sagaManagerMock.Verify(
            s => s.UpdateProviderStateAsync(
                request.SagaId,
                It.Is<ProviderLookupState>( state => state.IsSuccess ),
                It.IsAny<CancellationToken>( )
            ),
            Times.Once );
    }

    /// <summary>
    /// A <see cref="LookupRequestType.ArtistAlbumLookup"/> request missing title or artist falls
    /// through to the <see cref="InvalidOperationException"/> arm, records a failed provider state,
    /// and does not call any lookup method.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task PerformLookup_WithArtistAlbumLookupMissingTitleArtist_ShouldRecordFailedState( ) {
        // Arrange: no Title or Artist on the request
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.ArtistAlbumLookup, "some-value" ) with {
            AttemptCount = 1  // below max so it goes through HandleProcessingExceptionAsync
        };
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert: falls through to error handling — IsSuccess == false, ErrorMessage != null
        _sagaManagerMock.Verify(
            s => s.UpdateProviderStateAsync(
                request.SagaId,
                It.Is<ProviderLookupState>( state =>
                    !state.IsSuccess &&
                    state.ErrorMessage != null
                ),
                It.IsAny<CancellationToken>( )
            ),
            Times.Once );
        // The lookup service is never called
        _lookupServiceMock.Verify( l => l.GetInfoAsync( It.IsAny<string>( ), It.IsAny<string>( ) ), Times.Never );
    }

    /// <summary>
    /// A <see cref="LookupRequestType.AlbumTrackLookup"/> request missing title or artist falls
    /// through to the <see cref="InvalidOperationException"/> arm and records a failed provider state.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task PerformLookup_WithAlbumTrackLookupMissingTitleArtist_ShouldRecordFailedState( ) {
        // Arrange: no Title or Artist on the request
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.AlbumTrackLookup, "some-value" ) with {
            AttemptCount = 1
        };
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert: falls through to error handling
        _sagaManagerMock.Verify(
            s => s.UpdateProviderStateAsync(
                request.SagaId,
                It.Is<ProviderLookupState>( state =>
                    !state.IsSuccess &&
                    state.ErrorMessage != null
                ),
                It.IsAny<CancellationToken>( )
            ),
            Times.Once );
        _lookupServiceMock.Verify( l => l.GetInfoAsync( It.IsAny<string>( ), It.IsAny<string>( ) ), Times.Never );
    }

    #endregion

    #region Rate Limit Exception Handling Tests

    /// <summary>
    /// A <see cref="RetryAfterExceededException"/> from the lookup sets a rate-limit window in the
    /// tracker for this provider.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WhenRateLimitExceptionThrown_ShouldRecordRateLimit( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" );
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );
        TimeSpan retryAfter = TimeSpan.FromSeconds( 60 );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoByISRCAsync( It.IsAny<string>( ) ) )
            .ThrowsAsync( new RetryAfterExceededException(
                retryAfter,
                TimeSpan.FromSeconds( 30 ),
                new Uri( "https://api.spotify.com/v1/tracks" ),
                SupportedProviders.Spotify
            ) );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _rateLimitTrackerMock.Verify(
            r => r.SetRateLimitedAsync(
                TestProvider,
                It.IsAny<string>( ), // Uses LookupType.ToString() as endpoint
                It.IsAny<DateTimeOffset>( ),
                It.IsAny<CancellationToken>( )
            ),
            Times.Once
        );
    }

    /// <summary>
    /// A <see cref="RetryAfterExceededException"/> acknowledges the original message and re-enqueues
    /// the request at <see cref="QueuePriority.Background"/> with an incremented attempt count and the
    /// rate-limited endpoint recorded.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WhenRateLimitExceptionThrown_ShouldRequeueMessage( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" );
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );
        TimeSpan retryAfter = TimeSpan.FromSeconds( 60 );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoByISRCAsync( It.IsAny<string>( ) ) )
            .ThrowsAsync( new RetryAfterExceededException(
                retryAfter,
                TimeSpan.FromSeconds( 30 ),
                new Uri( "https://api.spotify.com/v1/tracks" ),
                SupportedProviders.Spotify
            ) );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert - Should acknowledge original and re-enqueue (rate-limit-aware dequeue will skip until limit expires)
        _queueMock.Verify(
            q => q.AcknowledgeAsync( message.MessageId, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
        _queueMock.Verify(
            q => q.EnqueueAsync(
                It.Is<QueuedLookupRequest>( r =>
                    r.LookupType == request.LookupType &&
                    r.LookupValue == request.LookupValue &&
                    r.AttemptCount == request.AttemptCount + 1 &&
                    r.RateLimitedEndpoint == request.LookupType.ToString( )
                ),
                QueuePriority.Background,
                It.IsAny<CancellationToken>( )
            ),
            Times.Once
        );
    }

    /// <summary>
    /// A <see cref="RetryAfterExceededException"/> marks the saga partial via <c>SetIsPartialAsync</c>.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WhenRateLimitExceptionThrown_ShouldMarkSagaAsPartial( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" );
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );
        TimeSpan retryAfter = TimeSpan.FromSeconds( 60 );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoByISRCAsync( It.IsAny<string>( ) ) )
            .ThrowsAsync( new RetryAfterExceededException(
                retryAfter,
                TimeSpan.FromSeconds( 30 ),
                new Uri( "https://api.spotify.com/v1/tracks" ),
                SupportedProviders.Spotify
            ) );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert - Should mark the saga as partial
        _sagaManagerMock.Verify(
            s => s.SetIsPartialAsync( request.SagaId, true, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
    }

    /// <summary>
    /// A <see cref="RetryAfterExceededException"/> records a single <see cref="ProviderRateLimitInfo"/>
    /// (this provider, with an endpoint) into the saga via <c>SetRateLimitInfoAsync</c>.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WhenRateLimitExceptionThrown_ShouldRecordRateLimitInfoInSaga( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" );
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );
        TimeSpan retryAfter = TimeSpan.FromSeconds( 60 );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoByISRCAsync( It.IsAny<string>( ) ) )
            .ThrowsAsync( new RetryAfterExceededException(
                retryAfter,
                TimeSpan.FromSeconds( 30 ),
                new Uri( "https://api.spotify.com/v1/tracks" ),
                SupportedProviders.Spotify
            ) );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert - Should record rate limit info with correct provider
        _sagaManagerMock.Verify(
            s => s.SetRateLimitInfoAsync(
                request.SagaId,
                It.Is<List<ProviderRateLimitInfo>>( info =>
                    info.Count == 1 &&
                    info[0].Provider == TestProvider &&
                    !string.IsNullOrEmpty( info[0].Endpoint )
                ),
                It.IsAny<CancellationToken>( )
            ),
            Times.Once
        );
    }

    /// <summary>
    /// When the saga already holds rate-limit info for another provider, a new
    /// <see cref="RetryAfterExceededException"/> merges this provider's info with the existing entry
    /// rather than replacing it, so <c>SetRateLimitInfoAsync</c> receives both providers.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WhenRateLimitExceptionThrown_ShouldMergeRateLimitInfoWithOtherProviders( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" );
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );
        TimeSpan retryAfter = TimeSpan.FromSeconds( 60 );

        // Saga already carries rate limit info from a different provider
        ProviderRateLimitInfo existingInfo = new(
            SupportedProviders.AppleMusic,
            DateTimeOffset.UtcNow.AddMinutes( 5 ),
            "IsrcLookup"
        );
        LookupSagaState saga = new( ) {
            SagaId = request.SagaId,
            LookupKey = "test-key",
            LookupType = LookupRequestType.IsrcLookup,
            LookupValue = "US1234567890",
            ProviderStates = [],
            RateLimitInfo = [existingInfo]
        };
        _ = _sagaManagerMock.Setup( s => s.GetAsync( request.SagaId, It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( saga );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoByISRCAsync( It.IsAny<string>( ) ) )
            .ThrowsAsync( new RetryAfterExceededException(
                retryAfter,
                TimeSpan.FromSeconds( 30 ),
                new Uri( "https://api.spotify.com/v1/tracks" ),
                SupportedProviders.Spotify
            ) );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert - Both the existing AppleMusic entry and the new Spotify entry are present
        _sagaManagerMock.Verify(
            s => s.SetRateLimitInfoAsync(
                request.SagaId,
                It.Is<List<ProviderRateLimitInfo>>( info =>
                    info.Count == 2 &&
                    info.Any( r => r.Provider == SupportedProviders.AppleMusic ) &&
                    info.Any( r => r.Provider == TestProvider )
                ),
                It.IsAny<CancellationToken>( )
            ),
            Times.Once
        );
    }

    /// <summary>
    /// A <see cref="RetryAfterExceededException"/> publishes a lookup-completion wakeup (the
    /// rate-limited sentinel) on a <c>complete:</c> channel so synchronous waiters can return a partial.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WhenRateLimitExceptionThrown_ShouldPublishLookupCompletion( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" );
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );
        TimeSpan retryAfter = TimeSpan.FromSeconds( 60 );

        SetupNotRateLimited( );
        SetupSagaNotComplete( request.SagaId ); // Need saga for PublishLookupCompletionAsync to get lookup key
        _ = _lookupServiceMock.Setup( l => l.GetInfoByISRCAsync( It.IsAny<string>( ) ) )
            .ThrowsAsync( new RetryAfterExceededException(
                retryAfter,
                TimeSpan.FromSeconds( 30 ),
                new Uri( "https://api.spotify.com/v1/tracks" ),
                SupportedProviders.Spotify
            ) );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert - Should publish lookup completion for partial result handling
        _subscriberMock.Verify(
            s => s.PublishAsync(
                It.Is<RedisChannel>( c => c.ToString( ).StartsWith( "complete:", StringComparison.Ordinal ) ),
                It.IsAny<RedisValue>( ),
                It.IsAny<CommandFlags>( )
            ),
            Times.Once
        );
    }

    #endregion

    #region Interactive Origin Rate-Limit Deferral Tests

    /// <summary>
    /// An interactive-origin request that hits a rate limit is acknowledged, re-enqueued at
    /// <see cref="QueuePriority.Background"/> with an incremented attempt count, and logs the
    /// interactive-deferred-to-background warning (EventId 3018) exactly once.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WhenRateLimitAndInteractiveOrigin_ShouldStillRequeueAtBackground( ) {
        // Arrange
        // [LoggerMessage] source-generated code gates every call with IsEnabled().
        // Without this setup Moq returns false and Log() is never invoked, making
        // the Times.Once assertion spuriously fail and Times.Never vacuously true.
        _ = _loggerMock.Setup( l => l.IsEnabled( It.IsAny<LogLevel>( ) ) ).Returns( true );

        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" ) with {
            OriginPriority = QueuePriority.Interactive
        };
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );
        TimeSpan retryAfter = TimeSpan.FromSeconds( 60 );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoByISRCAsync( It.IsAny<string>( ) ) )
            .ThrowsAsync( new RetryAfterExceededException(
                retryAfter,
                TimeSpan.FromSeconds( 30 ),
                new Uri( "https://api.spotify.com/v1/tracks" ),
                SupportedProviders.Spotify
            ) );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert — acknowledge then requeue at Background (routing unchanged by the observability gate)
        _queueMock.Verify(
            q => q.AcknowledgeAsync( message.MessageId, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
        _queueMock.Verify(
            q => q.EnqueueAsync(
                It.Is<QueuedLookupRequest>( r =>
                    r.LookupType == request.LookupType &&
                    r.AttemptCount == request.AttemptCount + 1
                ),
                QueuePriority.Background,
                It.IsAny<CancellationToken>( )
            ),
            Times.Once
        );

        // Assert — the interactive-deferral log fires exactly once.
        // Disambiguated on EventId 3018 (InteractiveDeferredToBackground) to avoid ambiguity with
        // other Warning logs on this path (LogRateLimitEncountered/3006, LogSagaMarkedPartial/3007).
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                new EventId( LogEventIds.Services.Queue.InteractiveDeferredToBackground ),
                It.IsAny<It.IsAnyType>( ),
                It.IsAny<Exception?>( ),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>( ) ),
            Times.Once,
            "LogInteractiveDeferredToBackground (Warning, EventId 3018) must fire once for an Interactive-origin rate-limited request" );
    }

    /// <summary>
    /// A background-origin request that hits a rate limit is acknowledged and re-enqueued at
    /// <see cref="QueuePriority.Background"/>, but does <em>not</em> log the interactive-deferred
    /// warning (EventId 3018), since it did not originate as interactive.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WhenRateLimitAndBackgroundOrigin_ShouldRequeueAtBackground( ) {
        // Arrange
        // [LoggerMessage] source-generated code gates every call with IsEnabled().
        // Setting it true so a mis-fired LogInteractiveDeferredToBackground would reach Log()
        // and be caught by the Times.Never assertion below (without this, Times.Never would be
        // vacuously true even if the gate ran on the wrong branch).
        _ = _loggerMock.Setup( l => l.IsEnabled( It.IsAny<LogLevel>( ) ) ).Returns( true );

        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" ) with {
            OriginPriority = QueuePriority.Background
        };
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );
        TimeSpan retryAfter = TimeSpan.FromSeconds( 60 );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoByISRCAsync( It.IsAny<string>( ) ) )
            .ThrowsAsync( new RetryAfterExceededException(
                retryAfter,
                TimeSpan.FromSeconds( 30 ),
                new Uri( "https://api.spotify.com/v1/tracks" ),
                SupportedProviders.Spotify
            ) );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert — same requeue-at-Background path; no divergence from background-origin behavior
        _queueMock.Verify(
            q => q.AcknowledgeAsync( message.MessageId, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
        _queueMock.Verify(
            q => q.EnqueueAsync(
                It.Is<QueuedLookupRequest>( r =>
                    r.LookupType == request.LookupType &&
                    r.AttemptCount == request.AttemptCount + 1
                ),
                QueuePriority.Background,
                It.IsAny<CancellationToken>( )
            ),
            Times.Once
        );

        // Assert — the interactive-deferral log must NOT fire for a Background-origin request.
        // Disambiguated on EventId 3018 (InteractiveDeferredToBackground). The IsEnabled(true)
        // setup above makes this a meaningful negative control rather than a vacuously true check.
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                new EventId( LogEventIds.Services.Queue.InteractiveDeferredToBackground ),
                It.IsAny<It.IsAnyType>( ),
                It.IsAny<Exception?>( ),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>( ) ),
            Times.Never,
            "LogInteractiveDeferredToBackground (Warning, EventId 3018) must NOT fire for a Background-origin request" );
    }

    #endregion

    #region General Exception Handling Tests

    /// <summary>
    /// A non-rate-limit exception below the retry ceiling marks the provider state complete-and-failed
    /// with an error message via <c>UpdateProviderStateAsync</c>.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WhenExceptionThrown_BelowMaxRetries_ShouldUpdateSagaWithError( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" ) with {
            AttemptCount = 1 // Below max of 5
        };
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoByISRCAsync( It.IsAny<string>( ) ) )
            .ThrowsAsync( new InvalidOperationException( "Test error" ) );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _sagaManagerMock.Verify(
            s => s.UpdateProviderStateAsync(
                request.SagaId,
                It.Is<ProviderLookupState>( state =>
                    state.Provider == TestProvider &&
                    state.IsComplete &&
                    !state.IsSuccess &&
                    state.ErrorMessage != null
                ),
                It.IsAny<CancellationToken>( )
            ),
            Times.Once
        );
    }

    /// <summary>
    /// A non-rate-limit exception below the retry ceiling acknowledges the message (no requeue, no DLQ).
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WhenExceptionThrown_BelowMaxRetries_ShouldAcknowledgeMessage( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" ) with {
            AttemptCount = 1
        };
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoByISRCAsync( It.IsAny<string>( ) ) )
            .ThrowsAsync( new InvalidOperationException( "Test error" ) );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _queueMock.Verify(
            q => q.AcknowledgeAsync( message.MessageId, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
    }

    /// <summary>
    /// A non-rate-limit exception at the max retry count moves the message to the dead-letter queue.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WhenExceptionThrown_AtMaxRetries_ShouldMoveToDlq( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" ) with {
            AttemptCount = 5 // At max retries
        };
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoByISRCAsync( It.IsAny<string>( ) ) )
            .ThrowsAsync( new InvalidOperationException( "Test error" ) );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _queueMock.Verify(
            q => q.MoveToDlqAsync( message.MessageId, It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
    }

    /// <summary>
    /// A non-rate-limit exception at the max retry count does not acknowledge the message (the DLQ
    /// move owns its lifecycle instead).
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ProcessMessage_WhenExceptionThrown_AtMaxRetries_ShouldNotAcknowledge( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" ) with {
            AttemptCount = 5
        };
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoByISRCAsync( It.IsAny<string>( ) ) )
            .ThrowsAsync( new InvalidOperationException( "Test error" ) );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert - Should NOT acknowledge when moving to DLQ
        _queueMock.Verify(
            q => q.AcknowledgeAsync( message.MessageId, It.IsAny<CancellationToken>( ) ),
            Times.Never
        );
    }

    #endregion

    #region No Message Handling Tests

    /// <summary>
    /// When the queue returns no messages, the service keeps polling (the dequeue call is made
    /// repeatedly rather than the loop stopping).
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ExecuteAsync_WhenNoMessages_ShouldContinuePolling( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        CountdownEvent pollCountdown = new( 2 ); // Wait for 2 poll calls

        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => {
                if (!pollCountdown.IsSet) {
                    _ = pollCountdown.Signal( );
                }
                return null;
            } );

        // Act - StartAsync triggers ExecuteAsync but doesn't block
        _ = service.StartAsync( CancellationToken.None );
        bool reachedTarget = pollCountdown.Wait( TimeSpan.FromSeconds( 2 ), TestContext.CancellationToken );
        await service.StopAsync( CancellationToken.None );
        pollCountdown.Dispose( );

        // Assert
        Assert.IsTrue( reachedTarget, "Service did not poll at least 2 times within timeout" );
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Builds a <see cref="QueueProcessorBackgroundService"/> bound to <see cref="TestProvider"/> from
    /// the current dependency mocks.
    /// </summary>
    private QueueProcessorBackgroundService CreateService( IMusicLookupService? lookupService = null ) =>
        new(
            _redisMock.Object,
            _queueMock.Object,
            _rateLimitTrackerMock.Object,
            _sagaManagerMock.Object,
            lookupService ?? _lookupServiceMock.Object,
            TestProvider,
            _loggerMock.Object
        );

    /// <summary>
    /// Builds a <see cref="QueuedLookupRequest"/> for <see cref="TestProvider"/> with fresh request
    /// and saga ids, the given lookup type, and lookup value.
    /// </summary>
    private static QueuedLookupRequest CreateRequest( LookupRequestType lookupType, string lookupValue ) =>
        new( ) {
            RequestId = $"req-{Guid.NewGuid( ):N}",
            Provider = TestProvider,
            LookupType = lookupType,
            LookupValue = lookupValue,
            SagaId = $"saga-{Guid.NewGuid( ):N}",
            CreatedAt = DateTimeOffset.UtcNow
        };

    /// <summary>
    /// Wraps a request in a <see cref="QueuedMessage{T}"/> with a fresh message id and the current
    /// enqueue time.
    /// </summary>
    private static QueuedMessage<QueuedLookupRequest> CreateMessage( QueuedLookupRequest request ) =>
        new( $"msg-{Guid.NewGuid( ):N}", request, DateTimeOffset.UtcNow );

    /// <summary>Builds a representative successful <see cref="MusicLookupResult"/> for lookup stubs.</summary>
    private static MusicLookupResult CreateLookupResult( ) => new( ) {
        ExternalId = "USRC12345678",
        Artist = "Test Artist",
        Title = "Test Song",
        URL = "https://example.com/track/123",
        ArtUrl = "https://example.com/art.jpg",
        IsAlbum = false,
        MarketRegion = "us"
    };

    /// <summary>Stubs the rate-limit tracker to report the provider/endpoint as not rate-limited.</summary>
    private void SetupNotRateLimited( ) {
        _ = _rateLimitTrackerMock.Setup( r => r.GetStateAsync(
                It.IsAny<SupportedProviders>( ),
                It.IsAny<string>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( new RateLimitState( false, null, null ) );
    }

    /// <summary>
    /// Stubs the rate-limit tracker to report the provider/endpoint as rate-limited, with the given
    /// time remaining and a corresponding retry-after instant.
    /// </summary>
    private void SetupRateLimited( TimeSpan timeRemaining ) {
        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.Add( timeRemaining );
        _ = _rateLimitTrackerMock.Setup( r => r.GetStateAsync(
                It.IsAny<SupportedProviders>( ),
                It.IsAny<string>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( new RateLimitState( true, retryAfter, timeRemaining ) );
    }

    /// <summary>Stubs every lookup-service method to return a successful result.</summary>
    private void SetupLookupSuccess( ) {
        MusicLookupResult result = CreateLookupResult( );

        // Setup based on lookup type
        _ = _lookupServiceMock.Setup( l => l.GetInfoByISRCAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( result );
        _ = _lookupServiceMock.Setup( l => l.GetInfoByUPCAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( result );
        _ = _lookupServiceMock.Setup( l => l.GetInfoAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( result );
        _ = _lookupServiceMock.Setup( l => l.GetInfoByIDAsync( It.IsAny<string>( ), It.IsAny<bool>( ) ) )
            .ReturnsAsync( result );
        _ = _lookupServiceMock.Setup( l => l.GetInfoAsync( It.IsAny<string>( ), It.IsAny<string>( ) ) )
            .ReturnsAsync( result );
    }

    /// <summary>
    /// Stubs the saga manager to return a saga where only Spotify is complete, so the saga is not
    /// yet whole (no <c>saga:completed</c> publication is expected).
    /// </summary>
    private void SetupSagaNotComplete( string sagaId ) {
        LookupSagaState saga = new( ) {
            SagaId = sagaId,
            LookupKey = "test-key",
            LookupType = LookupRequestType.IsrcLookup,
            LookupValue = "test-value",
            ProviderStates = new Dictionary<SupportedProviders, ProviderLookupState> {
                // Only one provider complete, saga is not complete
                [SupportedProviders.Spotify] = new(
                    SupportedProviders.Spotify,
                    IsComplete: true,
                    IsSuccess: true,
                    ResultJson: JsonSerializer.Serialize( CreateLookupResult( ), s_jsonOptions ),
                    CompletedAt: DateTimeOffset.UtcNow,
                    ErrorMessage: null
                )
            }
        };
        _ = _sagaManagerMock.Setup( s => s.GetAsync( sagaId, It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( saga );
    }

    /// <summary>
    /// Stubs the saga manager to return a saga where all three providers are complete and successful,
    /// so the saga is whole (a <c>saga:completed</c> publication is expected).
    /// </summary>
    private void SetupSagaComplete( string sagaId ) {
        LookupSagaState saga = new( ) {
            SagaId = sagaId,
            LookupKey = "test-key",
            LookupType = LookupRequestType.IsrcLookup,
            LookupValue = "test-value",
            ProviderStates = new Dictionary<SupportedProviders, ProviderLookupState> {
                [SupportedProviders.Spotify] = new(
                    SupportedProviders.Spotify,
                    IsComplete: true,
                    IsSuccess: true,
                    ResultJson: JsonSerializer.Serialize( CreateLookupResult( ), s_jsonOptions ),
                    CompletedAt: DateTimeOffset.UtcNow,
                    ErrorMessage: null
                ),
                [SupportedProviders.AppleMusic] = new(
                    SupportedProviders.AppleMusic,
                    IsComplete: true,
                    IsSuccess: true,
                    ResultJson: JsonSerializer.Serialize( CreateLookupResult( ), s_jsonOptions ),
                    CompletedAt: DateTimeOffset.UtcNow,
                    ErrorMessage: null
                ),
                [SupportedProviders.Tidal] = new(
                    SupportedProviders.Tidal,
                    IsComplete: true,
                    IsSuccess: true,
                    ResultJson: JsonSerializer.Serialize( CreateLookupResult( ), s_jsonOptions ),
                    CompletedAt: DateTimeOffset.UtcNow,
                    ErrorMessage: null
                )
            }
        };
        _ = _sagaManagerMock.Setup( s => s.GetAsync( sagaId, It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( saga );
    }

    #endregion
}
