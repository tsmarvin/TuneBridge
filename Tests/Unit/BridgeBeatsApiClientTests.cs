using System.Net;
using System.Text;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Worker.Discord.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Polly.Timeout;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests <see cref="BridgeBeatsApiClient"/>, the Discord worker's HTTP client for the BridgeBeats Web
/// API, focusing on how <see cref="BridgeBeatsApiClient.GetInfoAsync"/> streams results and degrades
/// when the call fails.
/// </summary>
/// <remarks>
/// The client returns an <see cref="IAsyncEnumerable{T}"/> of <see cref="MediaLinkResult"/>. The
/// resilience contract under test: transport timeouts and cancellations
/// (<see cref="TimeoutRejectedException"/>, <see cref="OperationCanceledException"/>,
/// <see cref="TaskCanceledException"/>) yield nothing and log a warning; an
/// <see cref="HttpRequestException"/> yields nothing and logs an error; malformed JSON yields nothing
/// without throwing; and a healthy-but-empty response yields nothing and logs no error. Each test
/// drives the client through an in-memory <see cref="HttpMessageHandler"/> double.
/// </remarks>
[TestClass]
public class BridgeBeatsApiClientTests {

    // -------------------------------------------------------------------------
    // GetInfoAsync transport fault handling via fake HttpMessageHandlers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="BridgeBeatsApiClient"/> over the supplied message handler with a discarding
    /// logger and an empty internal service key, for tests that do not inspect logging.
    /// </summary>
    /// <param name="innerHandler">The message handler backing the client's HTTP calls.</param>
    /// <returns>A configured client under test.</returns>
    private static BridgeBeatsApiClient CreateClient( HttpMessageHandler innerHandler ) {
        HttpClient httpClient = new( innerHandler ) {
            BaseAddress = new Uri( "http://test-host" )
        };
        Mock<ILogger<BridgeBeatsApiClient>> logger = new( );
        return new BridgeBeatsApiClient( httpClient, logger.Object, string.Empty );
    }

    /// <summary>
    /// Builds a <see cref="BridgeBeatsApiClient"/> over the supplied message handler with a verifiable
    /// mock logger (with all levels enabled) so a test can assert which log level was emitted.
    /// </summary>
    /// <param name="innerHandler">The message handler backing the client's HTTP calls.</param>
    /// <param name="loggerMock">Receives the mock logger for later verification.</param>
    /// <returns>A configured client under test.</returns>
    private static BridgeBeatsApiClient CreateClientWithMockLogger(
        HttpMessageHandler innerHandler,
        out Mock<ILogger<BridgeBeatsApiClient>> loggerMock
    ) {
        loggerMock = new Mock<ILogger<BridgeBeatsApiClient>>( );
        // [LoggerMessage] source-generated code gates every call with IsEnabled().
        // Without this setup Moq returns false and Log() is never invoked, making
        // VerifyWarningLogged / VerifyErrorLogged spuriously fail.
        _ = loggerMock.Setup( l => l.IsEnabled( It.IsAny<LogLevel>( ) ) ).Returns( true );
        HttpClient httpClient = new( innerHandler ) { BaseAddress = new Uri( "http://test-host" ) };
        return new BridgeBeatsApiClient( httpClient, loggerMock.Object, string.Empty );
    }

    /// <summary>
    /// Verifies that, given a 200 response carrying a one-element result array, the client streams a
    /// single <see cref="MediaLinkResult"/>.
    /// </summary>
    [TestMethod]
    public async Task GetInfoAsync_HappyPath_YieldsResults( ) {
        // Arrange
        string json = """
            [{"results":{},"isPartial":false,"inputLinks":[],"lookedUpAt":"2026-01-01T00:00:00Z"}]
            """;
        BridgeBeatsApiClient client = CreateClient( new StaticResponseHandler( HttpStatusCode.OK, json ) );

        // Act
        List<MediaLinkResult> results = await CollectAsync( client.GetInfoAsync( "https://example.com" ) );

        // Assert
        Assert.HasCount( 1, results );
    }

    /// <summary>
    /// Verifies that a Polly <see cref="TimeoutRejectedException"/> from the transport is swallowed: the
    /// client yields no results and logs a warning rather than throwing.
    /// </summary>
    [TestMethod]
    public async Task GetInfoAsync_TimeoutRejectedException_YieldsNothingNoThrow( ) {
        // Arrange
        BridgeBeatsApiClient client = CreateClientWithMockLogger(
            new ThrowingDelegatingHandler( new TimeoutRejectedException( "timed out" ) ),
            out Mock<ILogger<BridgeBeatsApiClient>> loggerMock
        );

        // Act — must not throw
        List<MediaLinkResult> results = await CollectAsync( client.GetInfoAsync( "https://example.com" ) );

        // Assert
        Assert.IsEmpty( results );
        VerifyWarningLogged( loggerMock );
    }

    /// <summary>
    /// Verifies that an <see cref="OperationCanceledException"/> from the transport is swallowed: the
    /// client yields no results and logs a warning rather than throwing.
    /// </summary>
    [TestMethod]
    public async Task GetInfoAsync_OperationCanceledException_YieldsNothingNoThrow( ) {
        // Arrange
        BridgeBeatsApiClient client = CreateClientWithMockLogger(
            new ThrowingDelegatingHandler( new OperationCanceledException( "cancelled" ) ),
            out Mock<ILogger<BridgeBeatsApiClient>> loggerMock
        );

        // Act
        List<MediaLinkResult> results = await CollectAsync( client.GetInfoAsync( "https://example.com" ) );

        // Assert
        Assert.IsEmpty( results );
        VerifyWarningLogged( loggerMock );
    }

    /// <summary>
    /// Verifies that a <see cref="TaskCanceledException"/> from the transport is swallowed: the client
    /// yields no results and logs a warning rather than throwing.
    /// </summary>
    [TestMethod]
    public async Task GetInfoAsync_TaskCanceledException_YieldsNothingNoThrow( ) {
        // Arrange
        BridgeBeatsApiClient client = CreateClientWithMockLogger(
            new ThrowingDelegatingHandler( new TaskCanceledException( "task cancelled" ) ),
            out Mock<ILogger<BridgeBeatsApiClient>> loggerMock
        );

        // Act
        List<MediaLinkResult> results = await CollectAsync( client.GetInfoAsync( "https://example.com" ) );

        // Assert
        Assert.IsEmpty( results );
        VerifyWarningLogged( loggerMock );
    }

    /// <summary>
    /// Verifies that an <see cref="HttpRequestException"/> (a transport failure) is swallowed: the
    /// client yields no results and logs an error, distinguishing a real failure from a benign
    /// cancellation.
    /// </summary>
    [TestMethod]
    public async Task GetInfoAsync_HttpRequestException_YieldsNothingNoThrow( ) {
        // Arrange
        BridgeBeatsApiClient client = CreateClientWithMockLogger(
            new ThrowingDelegatingHandler( new HttpRequestException( "network error" ) ),
            out Mock<ILogger<BridgeBeatsApiClient>> loggerMock
        );

        // Act
        List<MediaLinkResult> results = await CollectAsync( client.GetInfoAsync( "https://example.com" ) );

        // Assert
        Assert.IsEmpty( results );
        VerifyErrorLogged( loggerMock );
    }

    /// <summary>
    /// Verifies that a 200 response with an unparseable body yields no results without throwing, so a
    /// malformed payload degrades gracefully.
    /// </summary>
    [TestMethod]
    public async Task GetInfoAsync_MalformedJson_YieldsNothingNoThrow( ) {
        // Arrange
        BridgeBeatsApiClient client = CreateClient( new StaticResponseHandler( HttpStatusCode.OK, "not-valid-json{{" ) );

        // Act
        List<MediaLinkResult> results = await CollectAsync( client.GetInfoAsync( "https://example.com" ) );

        // Assert
        Assert.IsEmpty( results );
    }

    /// <summary>
    /// Verifies that a healthy 200 response with an empty array yields no results and logs no error,
    /// confirming "no matches" is a normal outcome rather than a failure.
    /// </summary>
    [TestMethod]
    public async Task GetInfoAsync_EmptyArray_YieldsNothingAndLogsNoError( ) {
        // Arrange
        BridgeBeatsApiClient client = CreateClientWithMockLogger(
            new StaticResponseHandler( HttpStatusCode.OK, "[]" ),
            out Mock<ILogger<BridgeBeatsApiClient>> loggerMock
        );

        // Act
        List<MediaLinkResult> results = await CollectAsync( client.GetInfoAsync( "https://example.com" ) );

        // Assert — zero results, no error logged
        Assert.IsEmpty( results );
        VerifyNoErrorLogged( loggerMock );
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Drains an async sequence into a list so a test can assert on its contents.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="source">The async sequence to materialize.</param>
    /// <returns>A list containing every element yielded by <paramref name="source"/>.</returns>
    private static async Task<List<T>> CollectAsync<T>( IAsyncEnumerable<T> source ) {
        List<T> list = [];
        await foreach (T item in source) {
            list.Add( item );
        }
        return list;
    }

    /// <summary>Asserts that the logger recorded at least one <see cref="LogLevel.Warning"/> entry.</summary>
    /// <param name="loggerMock">The mock logger to verify.</param>
    private static void VerifyWarningLogged( Mock<ILogger<BridgeBeatsApiClient>> loggerMock ) {
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>( ),
                It.IsAny<It.IsAnyType>( ),
                It.IsAny<Exception>( ),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>( ) ),
            Times.AtLeastOnce( ),
            "Expected a Warning-level log for timeout/cancellation" );
    }

    /// <summary>Asserts that the logger recorded at least one <see cref="LogLevel.Error"/> entry.</summary>
    /// <param name="loggerMock">The mock logger to verify.</param>
    private static void VerifyErrorLogged( Mock<ILogger<BridgeBeatsApiClient>> loggerMock ) {
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>( ),
                It.IsAny<It.IsAnyType>( ),
                It.IsAny<Exception>( ),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>( ) ),
            Times.AtLeastOnce( ),
            "Expected an Error-level log for the HTTP transport failure" );
    }

    /// <summary>Asserts that the logger recorded no <see cref="LogLevel.Error"/> entries.</summary>
    /// <param name="loggerMock">The mock logger to verify.</param>
    private static void VerifyNoErrorLogged( Mock<ILogger<BridgeBeatsApiClient>> loggerMock ) {
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>( ),
                It.IsAny<It.IsAnyType>( ),
                It.IsAny<Exception>( ),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>( ) ),
            Times.Never( ),
            "Healthy-empty response must not emit an Error log" );
    }

    // -------------------------------------------------------------------------
    // Fake handlers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Test message handler that throws the configured exception on every send, simulating a transport
    /// failure or cancellation.
    /// </summary>
    /// <param name="exception">The exception thrown from each send.</param>
    private sealed class ThrowingDelegatingHandler( Exception exception ) : HttpMessageHandler {
        /// <summary>Throws the configured exception instead of sending the request.</summary>
        /// <param name="request">The outgoing request (unused).</param>
        /// <param name="cancellationToken">A cancellation token (unused).</param>
        /// <returns>This method never returns; it always throws.</returns>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => throw exception;
    }

    /// <summary>
    /// Test message handler that returns a fixed status code and JSON body for every request, letting a
    /// test feed the client a canned response.
    /// </summary>
    /// <param name="status">The HTTP status code to return.</param>
    /// <param name="body">The response body returned as <c>application/json</c>.</param>
    private sealed class StaticResponseHandler( HttpStatusCode status, string body ) : HttpMessageHandler {
        /// <summary>Returns the configured status and body for the request.</summary>
        /// <param name="request">The outgoing request (unused).</param>
        /// <param name="cancellationToken">A cancellation token (unused).</param>
        /// <returns>A response carrying the configured status and JSON body.</returns>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) {
            HttpResponseMessage response = new( status ) {
                Content = new StringContent( body, Encoding.UTF8, "application/json" )
            };
            return Task.FromResult( response );
        }
    }
}
