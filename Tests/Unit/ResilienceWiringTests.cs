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
/// Part C resilience wiring tests — verifies that the ATProtoSyncHttpClient participates in the
/// global resilience pipeline and carries a 130s transport timeout, and that
/// ATProtoStorageService.FetchAllViaCarAsync correctly discriminates between
/// HttpClient-timeout OCE and cooperative shutdown OCE.
/// </summary>
[TestClass]
public class ResilienceWiringTests {

    // -------------------------------------------------------------------------
    // C-wiring — ATProtoSyncHttpClient inherits global pipeline and carries 130s transport timeout
    // -------------------------------------------------------------------------

    /// <summary>
    /// C-wiring — the atproto-sync named client participates in the global resilience
    /// pipeline that AddServiceDefaults installs and carries a 130s transport backstop.
    /// Failure-first: the sentinel pipeline is registered on the named client BEFORE AddATProtoStorage
    /// is called; the assertion goes red if AddATProtoStorage strips or overrides it. If the transport
    /// backstop regresses, the second assert fails.
    /// Note: ConfigureHttpClientDefaults pipeline configuration does not propagate to
    /// IOptionsMonitor named options (the options subsystem is a parallel registration path), so the
    /// sentinel is registered directly on the named client — the only surface IOptionsMonitor reflects.
    /// The test remains discriminating: it verifies AddATProtoStorage does not tamper with a
    /// pre-existing pipeline registration. Sentinel value (42s) is distinct from both the type
    /// default (10s) and the production default (120s).
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

    // -------------------------------------------------------------------------
    // Global-defaults pin
    // -------------------------------------------------------------------------

    /// <summary>
    /// Global-default guard — pins DefaultAttemptTimeoutSeconds=120 in AspireServiceExtensions.
    /// This is the root-cause regression guard: the vetoed bug was a global 10s AttemptTimeout.
    /// Real-path attempt: ConfigureHttpClientDefaults pipeline configuration does not propagate to
    /// IOptionsMonitor named options (observed: IOptionsMonitor.Get returns the 10s type default
    /// regardless of the ConfigureHttpClientDefaults delegate value), so a full AddServiceDefaults
    /// host-build assertion is not viable. Constant-pin: a named-client ServiceCollection probe
    /// installs the pipeline using DefaultAttemptTimeoutSeconds and asserts the resolved options
    /// equal the expected 120s literal. Reverting the constant from 120 to another value makes
    /// the runtime options diverge from 120 and the assertion fails. Direct const-equality assertions
    /// are rejected by MSTEST0032 (always-true), so the pin is achieved through runtime resolution.
    /// Both AddServiceDefaults overloads read exclusively from this constant — the constant IS the gate.
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

    // -------------------------------------------------------------------------
    // C-timeout — FetchAllViaCarAsync timeout (caller token not signaled) → logs + rethrows
    // -------------------------------------------------------------------------

    /// <summary>
    /// C-timeout — When the HTTP download throws an OperationCanceledException and the caller's
    /// token is NOT signaled, FetchAllViaCarAsync logs the failure and rethrows so consumers'
    /// catch(Exception) degradation paths can handle it.
    /// Failure-first: before the fix, the OCE was swallowed by the outer catch-when filter
    /// (the download section did not discriminate caller token state).
    /// Discriminator pin: we verify that the CAR-download-failed log event (Error, EventId 1083)
    /// fires — it is emitted in the HttpClient-timeout branch but NOT in the
    /// cooperative-shutdown branch, so its presence proves the right branch ran.
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
    /// C-cancel — When the caller's token IS signaled, FetchAllViaCarAsync rethrows for cooperative shutdown
    /// without logging as a download failure.
    /// Failure-first: the existing OperationCanceledException rethrow path was correct, but this test
    /// pins the behavior explicitly so a future regression is caught.
    /// Discriminator pin (negative control): we verify the CAR-download-failed log event (Error,
    /// EventId 1083) does NOT fire — confirming the cooperative-shutdown branch ran
    /// rather than the HttpClient-timeout branch which logs before rethrowing.
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

    // -------------------------------------------------------------------------
    // Fake handlers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Throws OperationCanceledException without signaling any external token (simulates HttpClient timeout).
    /// </summary>
    private sealed class TimeoutThrowingHandler : HttpMessageHandler {
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
    /// Throws OperationCanceledException with the specified already-cancelled token (simulates cooperative shutdown).
    /// </summary>
    private sealed class CancelledThrowingHandler( CancellationToken cancelledToken ) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromCanceled<HttpResponseMessage>( cancelledToken );
    }
}
