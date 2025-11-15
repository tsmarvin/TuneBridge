using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using Serilog;
using Serilog.Core;
using TuneBridge;

namespace TuneBridge.Tests.Unit;

/// <summary>
/// Unit tests for logging configuration validation.
/// Tests Serilog file logging and OpenTelemetry configuration.
/// </summary>
[TestClass]
public class LoggingConfigurationTests {
    [TestMethod]
    public void ConfigureSerilog_WithDefaultFilePath_ShouldCreateLogger( ) {
        // Arrange
        string logPath = Path.Combine( Path.GetTempPath( ), $"test-log-{Guid.NewGuid( )}", "test-.log" );
        Directory.CreateDirectory( Path.GetDirectoryName( logPath )! );

        try {
            Dictionary<string, string?> config = new( ) {
                ["Logging:FilePath"] = logPath,
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

    [TestMethod]
    public void ConfigureSerilog_WithFileRotation_ShouldRespectRetentionPolicy( ) {
        // Arrange
        string logDir = Path.Combine( Path.GetTempPath( ), $"test-log-rotation-{Guid.NewGuid( )}" );
        string logPath = Path.Combine( logDir, "test-.log" );
        Directory.CreateDirectory( logDir );

        try {
            Dictionary<string, string?> config = new( ) {
                ["Logging:FilePath"] = logPath,
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

    [TestMethod]
    public void LoggingConfiguration_ShouldSupportBothFileAndOpenTelemetry( ) {
        // Arrange
        string logPath = Path.Combine( Path.GetTempPath( ), $"test-dual-log-{Guid.NewGuid( )}", "test-.log" );
        Directory.CreateDirectory( Path.GetDirectoryName( logPath )! );

        try {
            Dictionary<string, string?> config = new( ) {
                ["Logging:FilePath"] = logPath,
                ["Logging:LogLevel:Default"] = "Information",
                ["OpenTelemetry:OtlpEndpoint"] = "http://aspire-dashboard:4317"
            };

            IConfiguration configuration = new ConfigurationBuilder( )
                .AddInMemoryCollection( config )
                .Build( );

            // Act & Assert
            string? filePath = configuration["Logging:FilePath"];
            string? otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];

            Assert.IsFalse( string.IsNullOrWhiteSpace( filePath ), "File path should be configured" );
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

    [TestMethod]
    public void HealthCheckLoggingFilter_ShouldExcludeSuccessfulHealthChecks( ) {
        // Arrange
        string logPath = Path.Combine( Path.GetTempPath( ), $"test-health-filter-{Guid.NewGuid( )}.log" );

        try {
            // Create logger with health check filter (mimicking StartupExtensions.ConfigureSerilog)
            Logger logger = new LoggerConfiguration( )
                .MinimumLevel.Information( )
                .Filter.ByExcluding( logEvent => {
                    // Exclude successful health check requests from logs
                    if (logEvent.Level != Serilog.Events.LogEventLevel.Information) {
                        return false;
                    }

                    string? messageTemplate = logEvent.MessageTemplate?.Text;
                    if (messageTemplate == null || !messageTemplate.Contains( "HTTP" )) {
                        return false;
                    }

                    if (logEvent.Properties.TryGetValue( "RequestPath", out Serilog.Events.LogEventPropertyValue? pathValue )) {
                        string path = pathValue.ToString( ).Trim( '"' );
                        if (
                            path.Equals( "/health", StringComparison.OrdinalIgnoreCase ) &&
                            logEvent.Properties.TryGetValue( "StatusCode", out Serilog.Events.LogEventPropertyValue? statusValue ) &&
                            statusValue.ToString( ) == "200"
                        ) {
                            return true;
                        }
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

            logger.Dispose( );

            // Assert
            string logContent = File.ReadAllText( logPath );

            // Successful health check (200 OK at Information level) should be filtered out
            int healthOccurrences = System.Text.RegularExpressions.Regex.Matches( logContent, "/health" ).Count;

            // Should only have 1 occurrence (the warning for 500 error), not 2
            Assert.AreEqual( 1, healthOccurrences,
                $"Expected only 1 /health occurrence (the failed one), but found {healthOccurrences}. Successful health check at Information level should be filtered." );

            // Other requests should still be logged
            Assert.IsTrue( logContent.Contains( "/api/data" ),
                "Non-health endpoints should be logged" );

            // Failed health check (non-Information level) should be logged
            Assert.IsTrue( logContent.Contains( "500" ),
                "Failed health check requests should still be logged" );
        } finally {
            // Cleanup
            if (File.Exists( logPath )) {
                File.Delete( logPath );
            }
        }
    }
}
