using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TuneBridge.Common;
using TuneBridge.Common.Contracts.DTOs;
using TuneBridge.JetStreamMonitor.Infrastructure;

namespace TuneBridge.JetStreamMonitor.Domain;

/// <summary>
/// Background service that monitors the Bluesky Jetstream for music links from supported providers.
/// Connects to the Jetstream WebSocket endpoint and processes posts in real-time.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="JetstreamMonitorService"/> class.
/// </remarks>
/// <param name="logger">Logger for diagnostic information.</param>
/// <param name="linkDetector">Service for detecting music links in text.</param>
/// <param name="serviceProvider">Service provider for creating scoped database contexts.</param>
/// <param name="tuneBridgeClient">Client for submitting URLs to TuneBridge API.</param>
public class JetstreamMonitorService(
    ILogger<JetstreamMonitorService> logger,
    IDbContextFactory<JetstreamMonitorContext> dbContextFactory,
    TuneBridgeApiClient tuneBridgeClient,
    UriBuilder uriBuilder
) : BackgroundService {

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        logger.LogInformation( "Starting Jetstream monitor service..." );

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await MonitorJetstreamAsync( stoppingToken );
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                logger.LogError( ex, "Error in Jetstream monitor, reconnecting in 5 seconds..." );
                await Task.Delay( TimeSpan.FromSeconds( 5 ), stoppingToken );
            }
        }

        logger.LogInformation( "Jetstream monitor service stopped" );
    }

    /// <summary>
    /// Connects to the Jetstream WebSocket and monitors for posts containing music links.
    /// </summary>
    private async Task MonitorJetstreamAsync( CancellationToken cancellationToken ) {
        using ClientWebSocket webSocket = new( );

        logger.LogInformation( "Connecting to Jetstream." );

        await webSocket.ConnectAsync( uriBuilder.Uri, cancellationToken );

        logger.LogInformation( "Connected to Jetstream, monitoring for music links..." );

        byte[] buffer = new byte[1024 * 1024]; // 1MB buffer
        Memory<byte> bufferMemory = buffer.AsMemory( );

        while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested) {
            ValueWebSocketReceiveResult result = await webSocket.ReceiveAsync( bufferMemory, cancellationToken );

            if (result.MessageType == WebSocketMessageType.Close) {
                logger.LogWarning( "Jetstream connection closed by server" );
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Closing",
                    cancellationToken
                );
                break;
            }

            if (result.MessageType == WebSocketMessageType.Text) {
                string message = System.Text.Encoding.UTF8.GetString( bufferMemory[..result.Count ].Span );
                await ProcessJetstreamMessageAsync( message, cancellationToken );
            }
        }
    }

    /// <summary>
    /// Processes a Jetstream message and extracts music links if present.
    /// </summary>
    private async Task ProcessJetstreamMessageAsync( string jsonMessage, CancellationToken cancellationToken ) {
        try {
            // Parse the Jetstream event
            JsonDocument doc = System.Text.Json.JsonDocument.Parse( jsonMessage );
            JsonElement root = doc.RootElement;

            // Check if this is a post create event
            if (!root.TryGetProperty( "kind", out JsonElement kindElement ) ||
                kindElement.GetString( ) != "commit") {
                return;
            }

            if (!root.TryGetProperty( "commit", out JsonElement commitElement )) {
                return;
            }

            if (!commitElement.TryGetProperty( "operation", out JsonElement operationElement ) ||
                operationElement.GetString( ) != "create") {
                return;
            }

            if (!commitElement.TryGetProperty( "collection", out JsonElement collectionElement ) ||
                collectionElement.GetString( ) != "app.bsky.feed.post") {
                return;
            }

            // Extract post record
            if (!commitElement.TryGetProperty( "record", out JsonElement recordElement )) {
                return;
            }

            // Get post text
            if (!recordElement.TryGetProperty( "text", out JsonElement textElement )) {
                return;
            }

            string postText = textElement.GetString( ) ?? string.Empty;

            // Check for music links
            if (MusicLinkDetector.ContainsMusicLink( postText )) {
                List<(string provider, string url)> detectedLinks = MusicLinkDetector.DetectMusicLinks( postText );

                // Get DID and post URI
                string did = root.TryGetProperty( "did", out JsonElement didElement )
                    ? didElement.GetString( ) ?? "unknown"
                    : "unknown";

                string rkey = commitElement.TryGetProperty( "rkey", out JsonElement rkeyElement )
                    ? rkeyElement.GetString( ) ?? "unknown"
                    : "unknown";

                string postUri = $"at://{did}/app.bsky.feed.post/{rkey}";

                // Store links in database
                await StoreMusicLinksAsync( detectedLinks, postUri, cancellationToken );
            }
        } catch (Exception ex) {
            logger.LogDebug( ex, "Failed to process Jetstream message" );
        }
    }

    /// <summary>
    /// Stores detected music links in the database, updating existing entries if the URL already exists.
    /// </summary>
    private async Task StoreMusicLinksAsync(
        List<(string provider, string url)> detectedLinks,
        string postUri,
        CancellationToken cancellationToken
    ) {
        foreach ((string provider, string url) in detectedLinks) {
            try {
                JetstreamMonitorContext dbContext = dbContextFactory.CreateDbContext();
                // Check if URL already exists
                DetectedMusicLink? existingLink = await dbContext.DetectedMusicLinks
                    .FirstOrDefaultAsync( link => link.Url == url, cancellationToken );

                if (existingLink != null) {
                    // Update existing entry
                    existingLink.LastSeenAt = DateTimeOffset.UtcNow;
                    existingLink.SeenCount++;

                    logger.LogInformation(
                        "Updated existing {provider} link (seen {count} times): {url}",
                        provider,
                        existingLink.SeenCount,
                        url
                    );
                } else {
                    // Create new entry
                    DetectedMusicLink newLink = new( ) {
                        Url = url,
                        Provider = provider,
                        PostUri = postUri,
                        FirstDetectedAt = DateTimeOffset.UtcNow,
                        LastSeenAt = DateTimeOffset.UtcNow,
                        SeenCount = 1
                    };

                    _ = dbContext.DetectedMusicLinks.Add( newLink );

                    logger.LogInformation(
                        "Detected new {provider} link in post {postUri}: {url}",
                        provider,
                        postUri,
                        url
                    );

                    // Write to console for easy visibility
                    Console.WriteLine( $"[{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}] {provider}: {url}" );
                    Console.WriteLine( $"  Post: {postUri}" );

                    // Submit new URL to TuneBridge for cross-platform lookup
                    _ = Task.Run( async ( ) => await SubmitUrlToTuneBridgeAsync( url, cancellationToken ), cancellationToken );
                }

                _ = await dbContext.SaveChangesAsync( cancellationToken );
            } catch (Exception ex) {
                logger.LogError( ex, "Failed to store music link: {url}", url );
            }
        }
    }

    /// <summary>
    /// Submits a detected music URL to TuneBridge API for cross-platform lookup.
    /// </summary>
    /// <param name="url">The music URL to submit.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    private async Task SubmitUrlToTuneBridgeAsync( string url, CancellationToken cancellationToken ) {
        try {
            logger.LogDebug( "Submitting URL to TuneBridge API: {url}", url );

            await foreach (MediaLinkResult result in tuneBridgeClient.SubmitUrlAsync( url, cancellationToken )) {
                if (result.Messages?.Count > 0) {
                    Console.WriteLine( $"  TuneBridge messages:\n{string.Join( ", ", result.Messages )}" );
                }
            }
        } catch (Exception ex) {
            logger.LogWarning( ex, "Failed to submit URL to TuneBridge: {url}", url );
        }
    }
}
