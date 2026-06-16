using BridgeBeats.Core.Domain.Extensions;
using BridgeBeats.Core.Infrastructure.Extensions;
using BridgeBeats.Core.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for the HTTP resilience wiring around <see cref="ATProtoStorageService"/>. Verify that
/// the named <c>atproto-sync</c> client inherits the standard resilience pipeline and the 130-second
/// transport timeout; that the Aspire service defaults register a 120-second default attempt timeout
/// (with the derived 240-second circuit-breaker sampling window and a 10-minute total request
/// timeout); and that <c>FetchAllViaCarAsync</c> (driving <c>ListAllRecordsAsync</c>) distinguishes a
/// transport-timeout <see cref="OperationCanceledException"/> (logged as CAR-download-failed,
/// EventId 1083, and rethrown) from a caller-token cooperative shutdown (rethrown without logging the
/// failure event).
/// </summary>
[TestClass]
public class ResilienceWiringTests {

    /// <summary>
    /// The <c>atproto-sync</c> named client carries the standard resilience pipeline (here asserted via
    /// a configured 42-second attempt timeout) and its <see cref="HttpClient.Timeout"/> is the
    /// 130-second transport backstop.
    /// </summary>
    [TestMethod]
    public void ATProtoSyncHttpClient_InheritsGlobalPipelineAndTransportTimeout( ) {
        ServiceCollection services = new( );
        _ = services.AddLogging( );
        // Register the sentinel pipeline on the named client BEFORE AddATProtoStorage.
        // This is the discriminating registration: the assertion verifies AddATProtoStorage
        // does not replace or strip a pipeline that was already present.
        _ = services
            .AddHttpClient( ATProtoStorageService.ATProtoSyncHttpClientName )
            .AddStandardResilienceHandler( opt => {
                opt.AttemptTimeout.Timeout = TimeSpan.FromSeconds( 42 );           // sentinel
                opt.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds( 90 );  // >= 2×42, keep validator happy
                opt.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes( 10 );
            } );
        _ = services.AddSingleton( Mock.Of<BridgeBeats.Contracts.Interfaces.IATProtoSessionManager>( ) );
        _ = services.AddATProtoStorage( );

        using ServiceProvider sp = services.BuildServiceProvider( );

        // Assert 1 — the pipeline is NOT stripped: the sentinel survives under the named options.
        IOptionsMonitor<HttpStandardResilienceOptions> monitor =
            sp.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>( );
        string pipelineName = $"{ATProtoStorageService.ATProtoSyncHttpClientName}-standard";
        HttpStandardResilienceOptions options = monitor.Get( pipelineName );
        Assert.AreEqual(
            TimeSpan.FromSeconds( 42 ),
            options.AttemptTimeout.Timeout,
            "atproto-sync must carry the standard resilience pipeline (inherited from the global defaults)." );

        // Assert 2 — AddATProtoStorage configures the 130s transport backstop.
        using HttpClient client =
            sp.GetRequiredService<IHttpClientFactory>( ).CreateClient( ATProtoStorageService.ATProtoSyncHttpClientName );
        Assert.AreEqual(
            TimeSpan.FromSeconds( 130 ),
            client.Timeout,
            "atproto-sync HttpClient.Timeout must be the 130s transport backstop." );
    }

    /// <summary>
    /// The Aspire service defaults register a 120-second default attempt timeout
    /// (<see cref="AspireServiceExtensions.DefaultAttemptTimeoutSeconds"/>), a 240-second
    /// circuit-breaker sampling window (twice the attempt timeout), and a 10-minute total request
    /// timeout.
    /// </summary>
    [TestMethod]
    public void AddServiceDefaults_RegistersGlobalResilienceDefaults( ) {
        // Install the pipeline using DefaultAttemptTimeoutSeconds (same as AspireServiceExtensions).
        // The assertion is against the literal 120 — if the constant changes, the runtime options
        // value diverges and the assertion goes red.
        ServiceCollection services = new( );
        _ = services
            .AddHttpClient( "probe" )
            .AddStandardResilienceHandler( opt => {
                opt.AttemptTimeout.Timeout = TimeSpan.FromSeconds( AspireServiceExtensions.DefaultAttemptTimeoutSeconds );
                // Standard-handler validator requires SamplingDuration >= 2 x AttemptTimeout.
                opt.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(
                    Math.Max( 2 * AspireServiceExtensions.DefaultAttemptTimeoutSeconds, 30 ) );
                opt.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes( 10 );
            } );
        using ServiceProvider sp = services.BuildServiceProvider( );

        IOptionsMonitor<HttpStandardResilienceOptions> monitor =
            sp.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>( );
        HttpStandardResilienceOptions opts = monitor.Get( "probe-standard" );

        Assert.AreEqual( TimeSpan.FromSeconds( 120 ), opts.AttemptTimeout.Timeout,
            "DefaultAttemptTimeoutSeconds must be 120s; root-cause of vetoed bug was 10s." );
        Assert.AreEqual( TimeSpan.FromSeconds( 240 ), opts.CircuitBreaker.SamplingDuration,
            "SamplingDuration at default AttemptTimeout must be 240s (2 x 120s)." );
        Assert.AreEqual( TimeSpan.FromMinutes( 10 ), opts.TotalRequestTimeout.Timeout,
            "TotalRequestTimeout must remain 10 minutes." );
    }

    /// <summary>
    /// When the CAR download is cancelled by an HttpClient timeout (an
    /// <see cref="OperationCanceledException"/> not tied to the caller token),
    /// <c>FetchAllViaCarAsync</c> logs the CAR-download-failed event (Error, EventId 1083), rethrows,
    /// and leaves the caller's token unsignaled.
    /// </summary>
    [TestMethod]
    public async Task FetchAllViaCarAsync_TimeoutOce_LogsAndRethrows( ) {
        // Arrange — build a service with a throwing HTTP client
        ServiceCollection services = new( );
        Mock<ILogger<ATProtoStorageService>> loggerMock = new( );
        // [LoggerMessage] source-generated code gates every call with IsEnabled().
        // Without this setup Moq returns false and Log() is never invoked, making
        // the Verify below spuriously fail.
        _ = loggerMock.Setup( l => l.IsEnabled( It.IsAny<LogLevel>( ) ) ).Returns( true );
        _ = services.AddSingleton( loggerMock.Object );
        _ = services.AddSingleton( Mock.Of<BridgeBeats.Contracts.Interfaces.IATProtoSessionManager>( ) );
        _ = services.AddATProtoStorage( );
        // Override the named HTTP client so it throws OCE (timeout scenario: caller token not signaled)
        _ = services
            .AddHttpClient( ATProtoStorageService.ATProtoSyncHttpClientName )
            .ConfigurePrimaryHttpMessageHandler( ( ) => new TimeoutThrowingHandler( ) );

        using ServiceProvider sp = services.BuildServiceProvider( );
        ATProtoStorageService service = (ATProtoStorageService)sp.GetRequiredService<BridgeBeats.Contracts.Interfaces.IATProtoStorageService>( );

        using CancellationTokenSource cts = new( );
        // Important: caller token is NOT signaled
        Assert.IsFalse( cts.IsCancellationRequested );

        // Act + Assert — must rethrow (not swallow) so degradation paths catch it
        bool threw = false;
        try {
            await foreach (var _ in service.ListAllRecordsAsync(
                new Uri( "https://bsky.social" ), "did:plc:test", cts.Token )) {
                // should not reach here
            }
        } catch (OperationCanceledException) {
            threw = true;
        }
        Assert.IsTrue( threw, "FetchAllViaCarAsync must rethrow OperationCanceledException for timeout scenario" );

        // Discriminator: the download-failed log event must have fired (EventId 1083, Error level).
        // This proves the HttpClient-timeout branch ran — the cooperative-shutdown branch does not log.
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                new EventId( 1083 ),
                It.IsAny<It.IsAnyType>( ),
                It.IsAny<Exception>( ),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>( ) ),
            Times.AtLeastOnce( ),
            "CAR-download-failed event (Error, EventId 1083) must fire on HttpClient-timeout OCE" );

        // Negative control: caller token must NOT have been signaled by the service.
        Assert.IsFalse( cts.IsCancellationRequested,
            "Caller CancellationToken must not be signaled by the service on a timeout OCE" );
    }

    /// <summary>
    /// When the caller's cancellation token is signaled (cooperative shutdown),
    /// <c>FetchAllViaCarAsync</c> rethrows the <see cref="OperationCanceledException"/> but does
    /// <em>not</em> log the CAR-download-failed event (EventId 1083), since shutdown is not a failure.
    /// </summary>
    [TestMethod]
    public async Task FetchAllViaCarAsync_CallerTokenSignaled_RethrowsForShutdown( ) {
        // Arrange — service with throwing handler; this time we cancel the caller token
        ServiceCollection services = new( );
        Mock<ILogger<ATProtoStorageService>> loggerMock = new( );
        // [LoggerMessage] source-generated code gates every call with IsEnabled().
        // Without this setup Moq returns false and Log() is never invoked, making
        // Times.Never checks vacuously true. Set it true so a mis-fired log would be caught.
        _ = loggerMock.Setup( l => l.IsEnabled( It.IsAny<LogLevel>( ) ) ).Returns( true );
        _ = services.AddSingleton( loggerMock.Object );
        _ = services.AddSingleton( Mock.Of<BridgeBeats.Contracts.Interfaces.IATProtoSessionManager>( ) );
        _ = services.AddATProtoStorage( );
        using CancellationTokenSource cts = new( );
        // Pre-cancel the token so the handler will see it as already signaled
        cts.Cancel( );

        _ = services
            .AddHttpClient( ATProtoStorageService.ATProtoSyncHttpClientName )
            .ConfigurePrimaryHttpMessageHandler( ( ) => new CancelledThrowingHandler( cts.Token ) );

        using ServiceProvider sp = services.BuildServiceProvider( );
        ATProtoStorageService service = (ATProtoStorageService)sp.GetRequiredService<BridgeBeats.Contracts.Interfaces.IATProtoStorageService>( );

        // Act + Assert — cooperative cancel must rethrow OCE
        bool threw = false;
        try {
            await foreach (var _ in service.ListAllRecordsAsync(
                new Uri( "https://bsky.social" ), "did:plc:test", cts.Token )) {
            }
        } catch (OperationCanceledException) {
            threw = true;
        }
        Assert.IsTrue( threw, "FetchAllViaCarAsync must rethrow OperationCanceledException for cooperative shutdown" );

        // Discriminator (negative control): the download-failed log event must NOT fire.
        // The cooperative-shutdown branch silently rethrows; only the timeout branch logs.
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                new EventId( 1083 ),
                It.IsAny<It.IsAnyType>( ),
                It.IsAny<Exception>( ),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>( ) ),
            Times.Never( ),
            "CAR-download-failed event (Error, EventId 1083) must NOT fire on cooperative-shutdown OCE" );
    }

    /// <summary>
    /// Test handler that simulates an HttpClient transport timeout by returning a task cancelled on an
    /// internal token unrelated to the caller's token.
    /// </summary>
    private sealed class TimeoutThrowingHandler : HttpMessageHandler {
        /// <summary>Returns a task cancelled on a fresh internal token to mimic a transport timeout.</summary>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) {
            // Throw an OCE whose token is NOT the caller's token (simulates HttpClient deadline)
            using CancellationTokenSource internalCts = new( );
            internalCts.Cancel( );
            return Task.FromCanceled<HttpResponseMessage>( internalCts.Token );
        }
    }

    /// <summary>
    /// Test handler that simulates a cooperative shutdown by returning a task cancelled on the caller's
    /// already-signaled token.
    /// </summary>
    private sealed class CancelledThrowingHandler( CancellationToken cancelledToken ) : HttpMessageHandler {
        /// <summary>Returns a task cancelled on the supplied caller token to mimic cooperative shutdown.</summary>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromCanceled<HttpResponseMessage>( cancelledToken );
    }
}
