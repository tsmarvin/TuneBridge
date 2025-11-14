using System.Text.RegularExpressions;
using idunno.AtProto;
using idunno.AtProto.Jetstream;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Domain.Implementations.Services {
    /// <summary>
    /// Monitors the ATProto Jetstream for music service links to bootstrap TuneBridge cache.
    /// Only processes links when the server is healthy to avoid overload.
    /// </summary>
    public partial class JetstreamMonitorService : IHostedService, IDisposable {
        private readonly ILogger<JetstreamMonitorService> _logger;
        private readonly IMediaLinkService _mediaLinkService;
        private readonly IServerHealthMonitor _healthMonitor;
        private readonly string _jetstreamUrl;
        private readonly string _tuneBridgeDid;
        private readonly bool _isEnabled;
        private AtProtoJetstream? _jetstream;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _connectionTask;

        // Regex patterns for music service URLs
        [GeneratedRegex( @"https?://(?:music\.apple\.com|open\.spotify\.com|tidal\.com|listen\.tidal\.com)/[^\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled )]
        private static partial Regex MusicLinkRegex( );

        /// <summary>
        /// Initializes a new instance of the <see cref="JetstreamMonitorService"/> class.
        /// </summary>
        /// <param name="logger">Logger for diagnostic information.</param>
        /// <param name="mediaLinkService">Service for processing music links.</param>
        /// <param name="healthMonitor">Service for monitoring server health.</param>
        /// <param name="jetstreamUrl">The Jetstream URL to connect to.</param>
        /// <param name="tuneBridgeDid">The TuneBridge DID to filter out own posts.</param>
        /// <param name="isEnabled">Whether the service is enabled.</param>
        public JetstreamMonitorService(
            ILogger<JetstreamMonitorService> logger,
            IMediaLinkService mediaLinkService,
            IServerHealthMonitor healthMonitor,
            string jetstreamUrl,
            string tuneBridgeDid,
            bool isEnabled
        ) {
            _logger = logger;
            _mediaLinkService = mediaLinkService;
            _healthMonitor = healthMonitor;
            _jetstreamUrl = jetstreamUrl;
            _tuneBridgeDid = tuneBridgeDid;
            _isEnabled = isEnabled;
        }

        /// <summary>
        /// Starts the Jetstream monitor service.
        /// </summary>
        public Task StartAsync( CancellationToken cancellationToken ) {
            if (!_isEnabled) {
                _logger.LogInformation( "JetstreamMonitorService is disabled. Skipping startup." );
                return Task.CompletedTask;
            }

            _logger.LogInformation( "Starting JetstreamMonitorService. Connecting to {JetstreamUrl}", _jetstreamUrl );

            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );

            // Start connection in background
            _connectionTask = Task.Run( async ( ) => await RunAsync( _cancellationTokenSource.Token ), _cancellationTokenSource.Token );

            return Task.CompletedTask;
        }

        /// <summary>
        /// Stops the Jetstream monitor service.
        /// </summary>
        public async Task StopAsync( CancellationToken cancellationToken ) {
            if (!_isEnabled) {
                return;
            }

            _logger.LogInformation( "Stopping JetstreamMonitorService" );

            // Signal cancellation
            _cancellationTokenSource?.Cancel( );

            // Close Jetstream connection gracefully
            if (_jetstream != null) {
                try {
                    await _jetstream.CloseAsync(
                        System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                        "Service stopping",
                        cancellationToken
                    );
                } catch (Exception ex) {
                    _logger.LogWarning( ex, "Error closing Jetstream connection" );
                }
            }

            // Wait for connection task to complete
            if (_connectionTask != null) {
                try {
                    await _connectionTask.WaitAsync( TimeSpan.FromSeconds( 10 ), cancellationToken );
                } catch (TimeoutException) {
                    _logger.LogWarning( "Jetstream connection task did not complete within timeout" );
                }
            }

            _logger.LogInformation( "JetstreamMonitorService stopped" );
        }

        /// <summary>
        /// Main loop that maintains the Jetstream connection and processes events.
        /// </summary>
        private async Task RunAsync( CancellationToken cancellationToken ) {
            while (!cancellationToken.IsCancellationRequested) {
                try {
                    await ConnectAndProcessAsync( cancellationToken );
                } catch (OperationCanceledException) {
                    _logger.LogInformation( "JetstreamMonitorService cancelled" );
                    break;
                } catch (Exception ex) {
                    _logger.LogError( ex, "Error in Jetstream connection. Reconnecting in 30 seconds..." );
                    try {
                        await Task.Delay( TimeSpan.FromSeconds( 30 ), cancellationToken );
                    } catch (OperationCanceledException) {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Establishes a Jetstream connection and processes incoming events.
        /// </summary>
        private async Task ConnectAndProcessAsync( CancellationToken cancellationToken ) {
            // Build Jetstream client with filters for app.bsky.feed.post
            AtProtoJetstreamBuilder builder = AtProtoJetstream.CreateBuilder( )
                .ConnectTo( new Uri( _jetstreamUrl ) )
                .FilterTo( [ new Nsid( "app.bsky.feed.post" ) ] );

            using (_jetstream = builder.Build( )) {
                // Subscribe to record received events
                _jetstream.RecordReceived += OnRecordReceived;
                _jetstream.ConnectionStateChanged += OnConnectionStateChanged;
                _jetstream.FaultRaised += OnFaultRaised;

                try {
                    _logger.LogInformation( "Connecting to Jetstream at {Url}", _jetstreamUrl );
                    await _jetstream.ConnectAsync( cancellationToken );
                } finally {
                    _jetstream.RecordReceived -= OnRecordReceived;
                    _jetstream.ConnectionStateChanged -= OnConnectionStateChanged;
                    _jetstream.FaultRaised -= OnFaultRaised;
                }
            }
        }

        /// <summary>
        /// Handles incoming Jetstream records.
        /// </summary>
        private void OnRecordReceived( object? sender, idunno.AtProto.Jetstream.Events.RecordReceivedEventArgs e ) {
            try {
                // Only process commit events with "create" operations
                if (e.ParsedEvent is not AtJetstreamCommitEvent commitEvent ||
                    commitEvent.Commit.Operation != "create") {
                    return;
                }

                // Skip posts from TuneBridge's own DID
                if (!string.IsNullOrEmpty( _tuneBridgeDid ) &&
                    e.ParsedEvent.Did.ToString( ) == _tuneBridgeDid) {
                    return;
                }

                // Extract post text from the record
                string? postText = ExtractPostText( commitEvent.Commit.Record );
                if (string.IsNullOrWhiteSpace( postText )) {
                    return;
                }

                // Check for music links
                MatchCollection matches = MusicLinkRegex( ).Matches( postText );
                if (matches.Count == 0) {
                    return;
                }

                // Check server health before processing
                if (!_healthMonitor.IsHealthy( )) {
                    _logger.LogWarning(
                        "Server is unhealthy (error rate: {ErrorRate:P2}). Skipping link processing.",
                        _healthMonitor.CurrentErrorRate
                    );
                    return;
                }

                // Process links in background
                _ = Task.Run( async ( ) => await ProcessLinksAsync( postText, e.ParsedEvent.Did.ToString( ) ) );
            } catch (Exception ex) {
                _logger.LogError( ex, "Error processing Jetstream record" );
            }
        }

        /// <summary>
        /// Processes discovered music links through the media link service.
        /// </summary>
        private async Task ProcessLinksAsync( string content, string authorDid ) {
            try {
                _logger.LogInformation( "Processing music links from {AuthorDid}", authorDid );

                await foreach (MediaLinkResult result in _mediaLinkService.GetInfoAsync( content )) {
                    if (result.Results.Count > 0) {
                        _logger.LogInformation(
                            "Successfully cached music link from Jetstream. Results: {ResultCount}",
                            result.Results.Count
                        );
                        _healthMonitor.RecordSuccess( );
                    }
                }
            } catch (HttpRequestException ex) when (ex.StatusCode.HasValue) {
                _logger.LogWarning(
                    ex,
                    "HTTP error processing music link: {StatusCode}",
                    (int)ex.StatusCode.Value
                );
                _healthMonitor.RecordFailure( (int)ex.StatusCode.Value );
            } catch (Exception ex) {
                _logger.LogError( ex, "Error processing music links from Jetstream" );
                _healthMonitor.RecordFailure( 500 ); // Generic server error
            }
        }

        /// <summary>
        /// Extracts post text from a Jetstream record.
        /// </summary>
        private string? ExtractPostText( object? record ) {
            if (record == null) {
                return null;
            }

            // The record is a JsonElement, so we need to extract the text field
            if (record is System.Text.Json.JsonElement element &&
                element.ValueKind == System.Text.Json.JsonValueKind.Object &&
                element.TryGetProperty( "text", out System.Text.Json.JsonElement textElement ) &&
                textElement.ValueKind == System.Text.Json.JsonValueKind.String) {
                return textElement.GetString( );
            }

            return null;
        }

        /// <summary>
        /// Handles connection state changes.
        /// </summary>
        private void OnConnectionStateChanged( object? sender, idunno.AtProto.Jetstream.Events.ConnectionStateChangedEventArgs e ) {
            _logger.LogInformation( "Jetstream connection state changed to: {State}", e.State );
        }

        /// <summary>
        /// Handles Jetstream faults.
        /// </summary>
        private void OnFaultRaised( object? sender, idunno.AtProto.Jetstream.Events.FaultRaisedEventArgs e ) {
            _logger.LogError( "Jetstream fault: {Fault}", e.Fault );
        }

        /// <summary>
        /// Disposes resources used by the service.
        /// </summary>
        public void Dispose( ) {
            _cancellationTokenSource?.Cancel( );
            _cancellationTokenSource?.Dispose( );
            _jetstream?.Dispose( );
        }
    }
}
