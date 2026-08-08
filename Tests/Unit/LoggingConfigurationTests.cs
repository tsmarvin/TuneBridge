using System.Text.RegularExpressions;
using BridgeBeats.Contracts.Constants;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests the Serilog and OpenTelemetry logging configuration: file sink creation and rotation/retention,
/// reading the OTLP endpoint from configuration, combining file and OTLP sinks, console logging, and the
/// health-check noise filter that excludes successful <c>/health</c> requests and the MVC/routing log noise they
/// produce while still logging failed health checks and non-health traffic.
/// </summary>
[TestClass]
public partial class LoggingConfigurationTests {
    /// <summary>
    /// Verifies a logger configured with a file sink at a configured path creates the log directory and writes
    /// without error.
    /// </summary>
    [TestMethod]
    public void ConfigureSerilog_WithDefaultFilePath_ShouldCreateLogger( ) {
        // Arrange
        string logPath = Path.Combine( TestArtifacts.CreateDirectory( "test-log" ), "test-.log" );
        _ = Directory.CreateDirectory( Path.GetDirectoryName( logPath )! );

        try {
            Dictionary<string, string?> config = new( ) {
                ["BridgeBeats:LogFilePath"] = logPath,
                ["Logging:LogLevel:Default"] = "Information"
            };

            IConfiguration configuration = new ConfigurationBuilder( )
                .AddInMemoryCollection( config )
                .Build( );

            // Act
            Logger logger = new LoggerConfiguration( )
                .ReadFrom.Configuration( configuration )
                .WriteTo.File(
                    path: logPath,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    retainedFileCountLimit: 5,
                    rollOnFileSizeLimit: true,
                    shared: false
                )
                .CreateLogger( );

            logger.Information( "Test log message" );
            logger.Dispose( );

            // Assert
            Assert.IsTrue( Directory.Exists( Path.GetDirectoryName( logPath )! ), "Log directory should be created" );
        } finally {
            // Cleanup
            string? logDir = Path.GetDirectoryName( logPath );
            if (logDir != null && Directory.Exists( logDir )) {
                Directory.Delete( logDir, true );
            }
        }
    }

    /// <summary>
    /// Verifies a logger configured with daily rolling, a size limit, and a retained-file count produces at least
    /// one log file in the configured directory.
    /// </summary>
    [TestMethod]
    public void ConfigureSerilog_WithFileRotation_ShouldRespectRetentionPolicy( ) {
        // Arrange
        string logDir = TestArtifacts.CreateDirectory( "test-log-rotation" );
        string logPath = Path.Combine( logDir, "test-.log" );
        _ = Directory.CreateDirectory( logDir );

        try {
            Dictionary<string, string?> config = new( ) {
                ["BridgeBeats:LogDirPath"] = logDir,
                ["Logging:LogLevel:Default"] = "Information"
            };

            IConfiguration configuration = new ConfigurationBuilder( )
                .AddInMemoryCollection( config )
                .Build( );

            // Act - Create logger with retention policy of 5 files
            Logger logger = new LoggerConfiguration( )
                .ReadFrom.Configuration( configuration )
                .WriteTo.File(
                    path: logPath,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    retainedFileCountLimit: 5,
                    rollOnFileSizeLimit: true,
                    shared: false
                )
                .CreateLogger( );

            logger.Information( "Test log message for rotation" );
            logger.Dispose( );

            // Assert - Verify the configuration parameters are valid
            // Note: Actual file rotation testing would require writing 10MB+ of data
            // which is impractical for a unit test
            Assert.IsTrue( Directory.Exists( logDir ), "Log directory should exist" );
            string[] logFiles = Directory.GetFiles( logDir, "*.log" );
            Assert.IsNotEmpty( logFiles, "At least one log file should be created" );
        } finally {
            // Cleanup
            if (Directory.Exists( logDir )) {
                Directory.Delete( logDir, true );
            }
        }
    }

    /// <summary>
    /// Verifies a configured OTLP endpoint is read back as a non-empty, absolute HTTP URI.
    /// </summary>
    [TestMethod]
    public void OpenTelemetryConfiguration_WithValidEndpoint_ShouldNotThrow( ) {
        // Arrange
        Dictionary<string, string?> config = new( ) {
            ["OpenTelemetry:OtlpEndpoint"] = "http://aspire-dashboard:4317",
            ["Logging:LogLevel:Default"] = "Information"
        };

        IConfiguration configuration = new ConfigurationBuilder( )
            .AddInMemoryCollection( config )
            .Build( );

        // Act & Assert - Should not throw
        string? otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];
        Assert.IsFalse( string.IsNullOrWhiteSpace( otlpEndpoint ), "OTLP endpoint should be configured" );
        Assert.IsTrue( Uri.TryCreate( otlpEndpoint, UriKind.Absolute, out Uri? uri ), "OTLP endpoint should be a valid URI" );
        Assert.AreEqual( "http", uri!.Scheme, "OTLP endpoint should use HTTP scheme" );
    }

    /// <summary>
    /// Verifies an empty OTLP endpoint is read back as empty/whitespace, the signal the host uses to skip OTLP export.
    /// </summary>
    [TestMethod]
    public void OpenTelemetryConfiguration_WithEmptyEndpoint_ShouldHandleGracefully( ) {
        // Arrange
        Dictionary<string, string?> config = new( ) {
            ["OpenTelemetry:OtlpEndpoint"] = string.Empty,
            ["Logging:LogLevel:Default"] = "Information"
        };

        IConfiguration configuration = new ConfigurationBuilder( )
            .AddInMemoryCollection( config )
            .Build( );

        // Act
        string? otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];

        // Assert - When endpoint is empty/null, OpenTelemetry should not be configured
        Assert.IsTrue( string.IsNullOrWhiteSpace( otlpEndpoint ), "OTLP endpoint should be empty" );
    }

    /// <summary>
    /// Verifies a configuration carrying both a log directory and an OTLP endpoint exposes both values and supports
    /// building a working file logger alongside OTLP export.
    /// </summary>
    [TestMethod]
    public void LoggingConfiguration_ShouldSupportBothFileAndOpenTelemetry( ) {
        // Arrange
        string logPath = Path.Combine( TestArtifacts.CreateDirectory( "test-dual-log" ), "test-.log" );
        _ = Directory.CreateDirectory( Path.GetDirectoryName( logPath )! );

        try {
            Dictionary<string, string?> config = new( ) {
                ["BridgeBeats:LogDirPath"] = Path.GetDirectoryName(logPath)!,
                ["Logging:LogLevel:Default"] = "Information",
                ["OpenTelemetry:OtlpEndpoint"] = "http://aspire-dashboard:4317"
            };

            IConfiguration configuration = new ConfigurationBuilder( )
                .AddInMemoryCollection( config )
                .Build( );

            // Act & Assert
            string? dirPath = configuration["BridgeBeats:LogDirPath"];
            string? otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];

            Assert.IsFalse( string.IsNullOrWhiteSpace( dirPath ), "Log Dir Path should be configured" );
            Assert.IsFalse( string.IsNullOrWhiteSpace( otlpEndpoint ), "OTLP endpoint should be configured" );

            // Verify both can be configured simultaneously
            Logger logger = new LoggerConfiguration( )
                .ReadFrom.Configuration( configuration )
                .WriteTo.File(
                    path: logPath,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    retainedFileCountLimit: 5,
                    rollOnFileSizeLimit: true,
                    shared: false
                )
                .CreateLogger( );

            logger.Information( "Test message for dual logging" );
            logger.Dispose( );

            Assert.IsTrue( Directory.Exists( Path.GetDirectoryName( logPath )! ), "Log directory should exist" );
        } finally {
            // Cleanup
            string? logDir = Path.GetDirectoryName( logPath );
            if (logDir != null && Directory.Exists( logDir )) {
                Directory.Delete( logDir, true );
            }
        }
    }

    /// <summary>
    /// Verifies the health-check noise filter: a successful Information-level <c>/health</c> request and the MVC
    /// controller-action and routing logs it generates are excluded, while a failed (500) health check, non-health
    /// endpoints, and non-health controller actions are still logged. Asserts <c>/health</c> appears exactly once
    /// (the failure).
    /// </summary>
    [TestMethod]
    public void HealthCheckLoggingFilter_ShouldExcludeSuccessfulHealthChecks( ) {
        // Arrange
        string logPath = TestArtifacts.CreateFilePath( "test-health-filter", ".log" );

        try {
            // Create logger with health check filter (mimicking StartupExtensions.ConfigureSerilog)
            Logger logger = new LoggerConfiguration( )
                .MinimumLevel.Information( )
                .Filter.ByExcluding( logEvent => {
                    // Exclude successful health check requests from logs (but keep failures)
                    // This filters out Information level logs for /health endpoint
                    if (logEvent.Level != Serilog.Events.LogEventLevel.Information) {
                        return false; // Don't exclude warnings, errors, etc. (allows failures and higher log levels to pass through)
                    }

                    // Check if this is a log event related to health endpoint
                    // This covers both HTTP request logs from Serilog.AspNetCore and MVC action execution logs
                    string? sourceContext = logEvent.Properties.TryGetValue( "SourceContext", out Serilog.Events.LogEventPropertyValue? sourceValue )
                        ? sourceValue.ToString( ).Trim( '"' )
                        : null;

                    // Filter MVC controller action execution logs for health endpoint
                    // Check if this is an MVC/Routing infrastructure log
                    if (sourceContext != null &&
                        (sourceContext.Contains( "Microsoft.AspNetCore.Mvc" ) ||
                         sourceContext.Contains( "Microsoft.AspNetCore.Routing" ))) {

                        // Check ActionName property first (most reliable indicator)
                        if (logEvent.Properties.TryGetValue( "ActionName", out Serilog.Events.LogEventPropertyValue? actionValue )) {
                            string actionName = actionValue.ToString( );
                            if (actionName.Contains( "Health", StringComparison.OrdinalIgnoreCase )) {
                                return true; // Exclude health check related MVC logs
                            }
                        }

                        // Also check message text for "Health" keyword as fallback
                        // This catches logs like "Route matched with {action = "Health", controller = "Home"}"
                        string messageText = logEvent.RenderMessage( );
                        if (messageText.Contains( "Health", StringComparison.OrdinalIgnoreCase )) {
                            return true; // Exclude health check related MVC logs
                        }
                    }

                    // Filter HTTP request completion logs from Serilog.AspNetCore for successful health checks
                    if (logEvent.Properties.TryGetValue( "RequestPath", out Serilog.Events.LogEventPropertyValue? pathValue ) &&
                        pathValue.ToString( ).Trim( '"' ).Equals( EndpointPaths.Health, StringComparison.OrdinalIgnoreCase ) &&
                        logEvent.Properties.TryGetValue( "StatusCode", out Serilog.Events.LogEventPropertyValue? statusValue ) &&
                        statusValue.ToString( ) == "200") {
                        return true; // Exclude successful health check HTTP logs
                    }

                    return false;
                } )
                .WriteTo.File( logPath )
                .CreateLogger( );

            // Act - Simulate HTTP request logs with properties
            logger
                .ForContext( "RequestPath", "/health" )
                .ForContext( "StatusCode", 200 )
                .Information( "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms",
                    "GET", "/health", 200, 1.2345 );

            logger
                .ForContext( "RequestPath", "/api/data" )
                .ForContext( "StatusCode", 200 )
                .Information( "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms",
                    "GET", "/api/data", 200, 5.6789 );

            logger
                .ForContext( "RequestPath", "/health" )
                .ForContext( "StatusCode", 500 )
                .Warning( "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms",
                    "GET", "/health", 500, 10.0 );

            // Simulate MVC action execution logs for health endpoint
            logger
                .ForContext( "SourceContext", "Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker" )
                .ForContext( "ActionName", "BridgeBeats.Web.Controllers.HomeController.Health (BridgeBeats)" )
                .Information( "Executing controller action with signature Microsoft.AspNetCore.Mvc.IActionResult Health() on controller BridgeBeats.Web.Controllers.HomeController (BridgeBeats)." );

            // Note: Some MVC infrastructure logs like "Executing OkObjectResult" may not have Health context
            // and thus may not be filterable without overly broad filtering. This is acceptable as long as
            // the primary noisy logs (route matching, action execution) are filtered.
            logger
                .ForContext( "SourceContext", "Microsoft.AspNetCore.Routing.EndpointMiddleware" )
                .Information( "Route matched with {{action = \"Health\", controller = \"Home\"}}. Executing controller action." );

            // Simulate MVC action execution logs for a different endpoint
            logger
                .ForContext( "SourceContext", "Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker" )
                .ForContext( "ActionName", "BridgeBeats.Web.Controllers.HomeController.Index (BridgeBeats)" )
                .Information( "Executing controller action with signature Microsoft.AspNetCore.Mvc.IActionResult Index() on controller BridgeBeats.Web.Controllers.HomeController (BridgeBeats)." );

            logger.Dispose( );

            // Assert
            string logContent = File.ReadAllText( logPath );

            // Successful health check (200 OK at Information level) should be filtered out
            int healthOccurrences = s_HealthEndpoint( ).Count( logContent );

            // Should only have 1 occurrence (the warning for 500 error), not 2
            Assert.AreEqual( 1, healthOccurrences,
                $"Expected only 1 /health occurrence (the failed one), but found {healthOccurrences}. Successful health check at Information level should be filtered." );

            // MVC action logs for Health endpoint should be filtered
            Assert.DoesNotContain( "Health() on controller", logContent, "Health controller action logs should be filtered" );
            Assert.DoesNotContain( "Route matched", logContent, "Health routing logs should be filtered" );

            // MVC action logs for other endpoints should still be logged
            Assert.Contains( "Index()", logContent, "Non-health controller action logs should be logged" );

            // Other requests should still be logged
            Assert.Contains( "/api/data", logContent, "Non-health endpoints should be logged" );

            // Failed health check (non-Information level) should be logged
            Assert.Contains( "500", logContent, "Failed health check requests should still be logged" );
        } finally {
            // Cleanup
            if (File.Exists( logPath )) {
                File.Delete( logPath );
            }
        }
    }

    /// <summary>
    /// Verifies a logger writing to both console and file sinks records the message to the file.
    /// </summary>
    [TestMethod]
    public void ConfigureSerilog_ShouldConfigureConsoleLogging( ) {
        // Arrange
        string logPath = TestArtifacts.CreateFilePath( "test-console-log", ".log" );

        try {
            // Act - Create logger with both console and file output (mimicking StartupExtensions.ConfigureSerilog)
            Logger logger = new LoggerConfiguration( )
                .MinimumLevel.Information( )
                .WriteTo.Console( )
                .WriteTo.File( logPath )
                .CreateLogger( );

            logger.Information( "Test message for console and file" );
            logger.Dispose( );

            // Assert
            string logContent = File.ReadAllText( logPath );
            Assert.Contains( "Test message for console and file", logContent, "Log message should be written to file" );

            // Note: We can't easily test actual console output in unit tests, but we can verify
            // the logger configuration accepts both sinks without errors
        } finally {
            // Cleanup
            if (File.Exists( logPath )) {
                File.Delete( logPath );
            }
        }
    }

    /// <summary>
    /// Source-generated regex matching the literal <c>/health</c>, used to count how many times the health endpoint
    /// appears in captured log output.
    /// </summary>
    /// <returns>A compiled <see cref="Regex"/> matching <c>/health</c>.</returns>
    [GeneratedRegex( "/health" )]
    private static partial Regex s_HealthEndpoint( );
}
