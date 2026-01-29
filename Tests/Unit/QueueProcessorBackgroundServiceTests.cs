using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Exceptions;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Domain.Services.Queue;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="QueueProcessorBackgroundService"/> to verify
/// proper queue consumption, rate limit handling, saga state updates, and error handling.
/// </summary>
[TestClass]
public class QueueProcessorBackgroundServiceTests {
    private Mock<IConnectionMultiplexer> _redisMock = null!;
    private Mock<ISubscriber> _subscriberMock = null!;
    private Mock<IRequestQueue<QueuedLookupRequest>> _queueMock = null!;
    private Mock<IRateLimitTracker> _rateLimitTrackerMock = null!;
    private Mock<ISagaStateManager> _sagaManagerMock = null!;
    private Mock<IMusicLookupService> _lookupServiceMock = null!;
    private Mock<ILogger<QueueProcessorBackgroundService>> _loggerMock = null!;

    private const SupportedProviders TestProvider = SupportedProviders.Spotify;

    private static readonly JsonSerializerOptions s_jsonOptions = new( ) {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Initializes mocks before each test.
    /// </summary>
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

    /// <summary>
    /// Verifies that the constructor creates a valid instance with valid dependencies.
    /// </summary>
    [TestMethod]
    public void Constructor_WithValidDependencies_ShouldCreateInstance( ) {
        // Act
        QueueProcessorBackgroundService service = CreateService( );

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

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when the queue is null.
    /// </summary>
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

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when the rate limit tracker is null.
    /// </summary>
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

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when the saga manager is null.
    /// </summary>
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

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when the lookup service is null.
    /// </summary>
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

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when the logger is null.
    /// </summary>
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
    /// Verifies that the lookup service is called when the endpoint is not rate limited.
    /// </summary>
    [TestMethod]
    public async Task ProcessMessage_WhenEndpointNotRateLimited_ShouldCallLookupService( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" );
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        SetupLookupSuccess( request );
        SetupSagaNotComplete( request.SagaId );

        // Setup queue to return one message then null (to exit loop)
        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act - Start and quickly cancel
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200 ); // Give time for processing
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _lookupServiceMock.Verify(
            l => l.GetInfoByISRCAsync( "US1234567890" ),
            Times.Once
        );
    }

    /// <summary>
    /// Verifies that messages are requeued with delay when the endpoint is rate limited.
    /// </summary>
    [TestMethod]
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
        await Task.Delay( 200 );
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
    /// Verifies that saga state is updated after a successful lookup.
    /// </summary>
    [TestMethod]
    public async Task ProcessMessage_WithSuccessfulLookup_ShouldUpdateSagaState( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" );
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        SetupLookupSuccess( request );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200 );
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

    /// <summary>
    /// Verifies that messages are acknowledged after a successful lookup.
    /// </summary>
    [TestMethod]
    public async Task ProcessMessage_WithSuccessfulLookup_ShouldAcknowledgeMessage( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" );
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        SetupLookupSuccess( request );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200 );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _queueMock.Verify(
            q => q.AcknowledgeAsync( message.MessageId, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
    }

    /// <summary>
    /// Verifies that a completion event is published when the saga completes.
    /// </summary>
    [TestMethod]
    public async Task ProcessMessage_WhenSagaCompletes_ShouldPublishCompletionEvent( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" );
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        SetupLookupSuccess( request );
        SetupSagaComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200 );
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
    /// Verifies that URI lookups call GetInfoAsync with the correct URI.
    /// </summary>
    [TestMethod]
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
        await Task.Delay( 200 );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _lookupServiceMock.Verify( l => l.GetInfoAsync( testUrl ), Times.Once );
    }

    /// <summary>
    /// Verifies that UPC lookups call GetInfoByUPCAsync with the correct UPC.
    /// </summary>
    [TestMethod]
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
        await Task.Delay( 200 );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _lookupServiceMock.Verify( l => l.GetInfoByUPCAsync( testUpc ), Times.Once );
    }

    /// <summary>
    /// Verifies that song ID lookups call GetInfoByIDAsync with the correct ID.
    /// </summary>
    [TestMethod]
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
        await Task.Delay( 200 );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _lookupServiceMock.Verify( l => l.GetInfoByIDAsync( testId, false ), Times.Once );
    }

    /// <summary>
    /// Verifies that AlbumIdLookup calls GetInfoByIDAsync with isAlbum set to true.
    /// </summary>
    [TestMethod]
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
        await Task.Delay( 200 );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _lookupServiceMock.Verify( l => l.GetInfoByIDAsync( testId, true ), Times.Once );
    }

    /// <summary>
    /// Verifies that SongLookup calls GetInfoAsync with the title and artist from the request.
    /// </summary>
    [TestMethod]
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
        await Task.Delay( 200 );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _lookupServiceMock.Verify( l => l.GetInfoAsync( "Test Song", "Test Artist" ), Times.Once );
    }

    #endregion

    #region Rate Limit Exception Handling Tests

    /// <summary>
    /// Verifies that rate limit exceptions are recorded in the rate limit tracker.
    /// </summary>
    [TestMethod]
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
        await Task.Delay( 200 );
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
    /// Verifies that rate-limited messages are acknowledged and re-enqueued with incremented attempt count.
    /// </summary>
    [TestMethod]
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
        await Task.Delay( 200 );
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
    /// Verifies that rate limit exceptions cause the saga to be marked as partial.
    /// </summary>
    [TestMethod]
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
        await Task.Delay( 200 );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert - Should mark the saga as partial
        _sagaManagerMock.Verify(
            s => s.SetIsPartialAsync( request.SagaId, true, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
    }

    /// <summary>
    /// Verifies that rate limit information is recorded in the saga state.
    /// </summary>
    [TestMethod]
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
        await Task.Delay( 200 );
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
    /// Verifies that rate limit exceptions trigger lookup completion publication for partial result handling.
    /// </summary>
    [TestMethod]
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
        await Task.Delay( 200 );
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

    #region General Exception Handling Tests

    /// <summary>
    /// Verifies that general exceptions update the saga with error state when below max retries.
    /// </summary>
    [TestMethod]
    public async Task ProcessMessage_WhenExceptionThrown_BelowMaxRetries_ShouldUpdateSagaWithError( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" ) with {
            AttemptCount = 1 // Below max of 5
        };
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoByISRCAsync( It.IsAny<string>( ) ) )
            .ThrowsAsync( new Exception( "Test error" ) );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200 );
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
    /// Verifies that messages are acknowledged after handling errors below max retries.
    /// </summary>
    [TestMethod]
    public async Task ProcessMessage_WhenExceptionThrown_BelowMaxRetries_ShouldAcknowledgeMessage( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" ) with {
            AttemptCount = 1
        };
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoByISRCAsync( It.IsAny<string>( ) ) )
            .ThrowsAsync( new Exception( "Test error" ) );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200 );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _queueMock.Verify(
            q => q.AcknowledgeAsync( message.MessageId, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
    }

    /// <summary>
    /// Verifies that messages are moved to DLQ when max retries is reached.
    /// </summary>
    [TestMethod]
    public async Task ProcessMessage_WhenExceptionThrown_AtMaxRetries_ShouldMoveToDlq( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" ) with {
            AttemptCount = 5 // At max retries
        };
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoByISRCAsync( It.IsAny<string>( ) ) )
            .ThrowsAsync( new Exception( "Test error" ) );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200 );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert
        _queueMock.Verify(
            q => q.MoveToDlqAsync( message.MessageId, It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
    }

    /// <summary>
    /// Verifies that messages are not acknowledged when moving to DLQ.
    /// </summary>
    [TestMethod]
    public async Task ProcessMessage_WhenExceptionThrown_AtMaxRetries_ShouldNotAcknowledge( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "US1234567890" ) with {
            AttemptCount = 5
        };
        QueuedMessage<QueuedLookupRequest> message = CreateMessage( request );

        SetupNotRateLimited( );
        _ = _lookupServiceMock.Setup( l => l.GetInfoByISRCAsync( It.IsAny<string>( ) ) )
            .ThrowsAsync( new Exception( "Test error" ) );
        SetupSagaNotComplete( request.SagaId );

        int callCount = 0;
        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => callCount++ == 0 ? message : null );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 200 );
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
    /// Verifies that the service continues polling when no messages are available.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_WhenNoMessages_ShouldContinuePolling( ) {
        // Arrange
        QueueProcessorBackgroundService service = CreateService( );
        int dequeueCallCount = 0;

        _ = _queueMock.Setup( q => q.DequeueAsync( It.IsAny<IRateLimitTracker>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( ( ) => {
                dequeueCallCount++;
                return null;
            } );

        // Act
        using CancellationTokenSource cts = new( );
        Task serviceTask = service.StartAsync( cts.Token );
        await Task.Delay( 350 ); // Should allow for multiple poll cycles (100ms delay each)
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        // Assert - Should have polled multiple times
        Assert.IsGreaterThanOrEqualTo( 2, dequeueCallCount, $"Expected at least 2 dequeue calls, got {dequeueCallCount}" );
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a new <see cref="QueueProcessorBackgroundService"/> instance with the configured mocks.
    /// </summary>
    /// <returns>A new <see cref="QueueProcessorBackgroundService"/> instance.</returns>
    private QueueProcessorBackgroundService CreateService( ) =>
        new(
            _redisMock.Object,
            _queueMock.Object,
            _rateLimitTrackerMock.Object,
            _sagaManagerMock.Object,
            _lookupServiceMock.Object,
            TestProvider,
            _loggerMock.Object
        );

    /// <summary>
    /// Creates a test <see cref="QueuedLookupRequest"/> with the specified lookup type and value.
    /// </summary>
    /// <param name="lookupType">The type of lookup request.</param>
    /// <param name="lookupValue">The value to look up.</param>
    /// <returns>A new <see cref="QueuedLookupRequest"/> instance.</returns>
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
    /// Creates a test <see cref="QueuedMessage{T}"/> wrapping the specified request.
    /// </summary>
    /// <param name="request">The request to wrap in a message.</param>
    /// <returns>A new <see cref="QueuedMessage{T}"/> instance.</returns>
    private static QueuedMessage<QueuedLookupRequest> CreateMessage( QueuedLookupRequest request ) =>
        new( $"msg-{Guid.NewGuid( ):N}", request, DateTimeOffset.UtcNow );

    /// <summary>
    /// Creates a test <see cref="MusicLookupResult"/> with sample data.
    /// </summary>
    /// <returns>A new <see cref="MusicLookupResult"/> instance.</returns>
    private static MusicLookupResult CreateLookupResult( ) => new( ) {
        ExternalId = "USRC12345678",
        Artist = "Test Artist",
        Title = "Test Song",
        URL = "https://example.com/track/123",
        ArtUrl = "https://example.com/art.jpg",
        IsAlbum = false,
        MarketRegion = "us"
    };

    /// <summary>
    /// Configures the rate limit tracker mock to return a non-rate-limited state.
    /// </summary>
    private void SetupNotRateLimited( ) {
        _ = _rateLimitTrackerMock.Setup( r => r.GetStateAsync(
                It.IsAny<SupportedProviders>( ),
                It.IsAny<string>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( new RateLimitState( false, null, null ) );
    }

    /// <summary>
    /// Configures the rate limit tracker mock to return a rate-limited state with the specified remaining time.
    /// </summary>
    /// <param name="timeRemaining">The time remaining until the rate limit expires.</param>
    private void SetupRateLimited( TimeSpan timeRemaining ) {
        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.Add( timeRemaining );
        _ = _rateLimitTrackerMock.Setup( r => r.GetStateAsync(
                It.IsAny<SupportedProviders>( ),
                It.IsAny<string>( ),
                It.IsAny<CancellationToken>( )
            ) )
            .ReturnsAsync( new RateLimitState( true, retryAfter, timeRemaining ) );
    }

    /// <summary>
    /// Configures the lookup service mock to return a successful result for all lookup methods.
    /// </summary>
    /// <param name="request">The request used to determine the lookup context.</param>
    private void SetupLookupSuccess( QueuedLookupRequest request ) {
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
    /// Configures the saga manager mock to return an incomplete saga with only one provider complete.
    /// </summary>
    /// <param name="sagaId">The saga ID to configure.</param>
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
    /// Configures the saga manager mock to return a complete saga with all providers finished.
    /// </summary>
    /// <param name="sagaId">The saga ID to configure.</param>
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
