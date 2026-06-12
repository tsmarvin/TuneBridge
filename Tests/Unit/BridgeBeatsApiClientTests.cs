using System.Net;
using System.Text;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Worker.Discord.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Polly.Timeout;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="BridgeBeatsApiClient.GetInfoAsync"/> covering transport fault handling
/// (Part A tests A1–A7).
/// </summary>
[TestClass]
public class BridgeBeatsApiClientTests {

    // -------------------------------------------------------------------------
    // A1–A7: GetInfoAsync transport fault handling via fake HttpMessageHandlers
    // -------------------------------------------------------------------------

    private static BridgeBeatsApiClient CreateClient( HttpMessageHandler innerHandler ) {
        HttpClient httpClient = new( innerHandler ) {
            BaseAddress = new Uri( "http://test-host" )
        };
        Mock<ILogger<BridgeBeatsApiClient>> logger = new( );
        return new BridgeBeatsApiClient( httpClient, logger.Object, string.Empty );
    }

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
    /// A1 — Happy path: yields one result per element returned by the API.
    /// Failure-first: with unimplemented deserialization, yields 0 elements.
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
    /// A2 — TimeoutRejectedException: logs warning and yields nothing; does not throw.
    /// Failure-first: before catch broadening, TimeoutRejectedException propagated unhandled.
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
    /// A3 — OperationCanceledException: logs warning and yields nothing; does not throw.
    /// Failure-first: before catch broadening, OCE propagated unhandled.
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
    /// A4 — TaskCanceledException (IS-A OperationCanceledException): logs warning and yields nothing; does not throw.
    /// Failure-first: before catch broadening, TCE propagated unhandled.
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
    /// A5 — HttpRequestException regression: logs error and yields nothing; does not throw.
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
    /// A6 — JsonException regression (malformed JSON): logs and yields nothing; does not throw.
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
    /// A7 — Empty array: yields nothing AND logs no error (positive control: healthy-empty is not a fault).
    /// Failure-first: a buggy impl treating [] as error would emit an Error log; this test would fail.
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

    private static async Task<List<T>> CollectAsync<T>( IAsyncEnumerable<T> source ) {
        List<T> list = [];
        await foreach (T item in source) {
            list.Add( item );
        }
        return list;
    }

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

    /// <summary>Throws a specified exception on every request.</summary>
    private sealed class ThrowingDelegatingHandler( Exception exception ) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => throw exception;
    }

    /// <summary>Returns a fixed HTTP status code and body.</summary>
    private sealed class StaticResponseHandler( HttpStatusCode status, string body ) : HttpMessageHandler {
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
