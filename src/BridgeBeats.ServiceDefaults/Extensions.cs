using System.Reflection;
using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.Exceptions;
using Microsoft.Extensions.Http.Resilience;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using Polly.Retry;
using Serilog;

namespace BridgeBeats.ServiceDefaults;

/// <summary>
/// Provides shared Aspire service defaults for BridgeBeats.
/// </summary>
public static class Extensions {
    private const string HealthPath = "/health";

    /// <summary>
    /// Adds service discovery, HTTP resilience defaults, health checks, and OpenTelemetry exporters.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The configured builder.</returns>
    public static WebApplicationBuilder AddServiceDefaults( this WebApplicationBuilder builder ) {
        _ = builder.Services.AddServiceDiscovery( );
        _ = builder.Services.AddHealthChecks( );

        // Read resilience configuration with defaults
        int maxRetryAttempts = builder.Configuration.GetValue( "BridgeBeats:Resilience:MaxRetryAttempts", 5 );
        int totalTimeoutMinutes = builder.Configuration.GetValue( "BridgeBeats:Resilience:TotalTimeoutMinutes", 10 );
        int attemptTimeoutSeconds = builder.Configuration.GetValue( "BridgeBeats:Resilience:AttemptTimeoutSeconds", 10 );

        _ = builder.Services.ConfigureHttpClientDefaults( http => {
            _ = http.AddServiceDiscovery( );

            IHttpStandardResiliencePipelineBuilder resilienceBuilder = http.AddStandardResilienceHandler( options => {
                options.Retry.BackoffType = DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                options.Retry.MaxRetryAttempts = maxRetryAttempts;
                options.Retry.Delay = TimeSpan.FromSeconds( 3 );
                options.Retry.MaxDelay = TimeSpan.FromMinutes( 5 );
                options.Retry.ShouldRetryAfterHeader = true;
                options.Retry.DisableForUnsafeHttpMethods( );

                // Exclude RetryAfterExceededException from retry logic - this is thrown intentionally
                // to fail fast when Retry-After headers exceed the configured threshold
                Func<RetryPredicateArguments<HttpResponseMessage>, ValueTask<bool>> originalShouldHandle = options.Retry.ShouldHandle;
                options.Retry.ShouldHandle = args => {
                    // If the exception is RetryAfterExceededException, do not retry
                    return args.Outcome.Exception is RetryAfterExceededException ? ValueTask.FromResult( false ) : originalShouldHandle( args );
                };

                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes( totalTimeoutMinutes );
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds( attemptTimeoutSeconds );
            } );

            _ = resilienceBuilder.SelectPipelineByAuthority( ).Configure( ( options, sp ) => {
                Microsoft.Extensions.Logging.ILogger logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("HttpResilience");
                options.Retry.OnRetry = args => {
                    TimeSpan? retryAfter = args.Outcome.Result?.Headers.RetryAfter?.Delta;
                    double retryAfterSeconds = retryAfter?.TotalSeconds ?? 0;

                    if (retryAfterSeconds > 0) {
                        logger.LogWarning(
                            "HTTP request failed (Attempt {AttemptNumber}/{MaxAttempts}). Retrying after {RetryAfterSeconds} seconds due to Retry-After header. Uri: {Uri}",
                            args.AttemptNumber,
                            options.Retry.MaxRetryAttempts,
                            retryAfterSeconds,
                            args.Outcome.Result?.RequestMessage?.RequestUri
                        );
                    } else {
                        logger.LogWarning(
                            "HTTP request failed (Attempt {AttemptNumber}/{MaxAttempts}). Retrying with exponential backoff. Uri: {Uri}",
                            args.AttemptNumber,
                            options.Retry.MaxRetryAttempts,
                            args.Outcome.Result?.RequestMessage?.RequestUri
                        );
                    }

                    return default;
                };
            } );
        } );

        ConfigureOpenTelemetry( builder );

        return builder;
    }

    /// <summary>
    /// Maps default liveness endpoints for Aspire.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The configured application.</returns>
    public static WebApplication MapDefaultEndpoints( this WebApplication app ) {
        _ = app.MapHealthChecks( HealthPath, new HealthCheckOptions {
            Predicate = _ => false
        } );

        return app;
    }

    private static void ConfigureOpenTelemetry( WebApplicationBuilder builder ) {
        string? otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
        string? otlpHeaders = builder.Configuration["OpenTelemetry:OtlpHeaders"];
        bool enableTracing = builder.Configuration.GetValue( "OpenTelemetry:EnableTracing", true );
        bool enableMetrics = builder.Configuration.GetValue( "OpenTelemetry:EnableMetrics", true );

        bool hasValidEndpoint = TryGetOtlpEndpoint( otlpEndpoint, out Uri? otlpUri );

        ResourceBuilder resourceBuilder = ResourceBuilder.CreateDefault( )
            .AddService(
                serviceName: builder.Environment.ApplicationName,
                serviceVersion: GetServiceVersion( )
            );

        if (hasValidEndpoint) {
            _ = builder.Logging.AddOpenTelemetry( options => {
                _ = options.SetResourceBuilder( resourceBuilder );
                _ = options.AddOtlpExporter( otlpOptions => {
                    otlpOptions.Endpoint = otlpUri!;
                    if (!string.IsNullOrWhiteSpace( otlpHeaders )) {
                        otlpOptions.Headers = otlpHeaders;
                    }
                } );
            } );
        }

        OpenTelemetryBuilder openTelemetryBuilder = builder.Services.AddOpenTelemetry( )
            .ConfigureResource( resource => resource.AddService( builder.Environment.ApplicationName ) );

        if (enableTracing) {
            _ = openTelemetryBuilder.WithTracing( tracing => {
                _ = tracing
                    .SetResourceBuilder( resourceBuilder )
                    .AddAspNetCoreInstrumentation( )
                    .AddHttpClientInstrumentation( );

                if (hasValidEndpoint) {
                    _ = tracing.AddOtlpExporter( otlpOptions => {
                        otlpOptions.Endpoint = otlpUri!;
                        if (!string.IsNullOrWhiteSpace( otlpHeaders )) {
                            otlpOptions.Headers = otlpHeaders;
                        }
                    } );
                }
            } );
        }

        if (enableMetrics) {
            _ = openTelemetryBuilder.WithMetrics( metrics => {
                _ = metrics
                    .SetResourceBuilder( resourceBuilder )
                    .AddAspNetCoreInstrumentation( )
                    .AddHttpClientInstrumentation( )
                    .AddRuntimeInstrumentation( )
                    .AddMeter( "BridgeBeats.Queue" )
                    .AddMeter( "BridgeBeats.Providers" );

                if (hasValidEndpoint) {
                    _ = metrics.AddOtlpExporter( otlpOptions => {
                        otlpOptions.Endpoint = otlpUri!;
                        if (!string.IsNullOrWhiteSpace( otlpHeaders )) {
                            otlpOptions.Headers = otlpHeaders;
                        }
                    } );
                }
            } );
        }
    }

    private static bool TryGetOtlpEndpoint( string? otlpEndpoint, out Uri? otlpUri ) {
        otlpUri = null;

        if (string.IsNullOrWhiteSpace( otlpEndpoint )) {
            return false;
        }

        if (!Uri.TryCreate( otlpEndpoint, UriKind.Absolute, out Uri? uri )) {
            Console.WriteLine( $"Warning: Invalid OTLP endpoint URL '{otlpEndpoint}' - OpenTelemetry export disabled" );
            return false;
        }

        otlpUri = uri;
        return true;
    }

    private static string GetServiceVersion( ) {
        Assembly? assembly = Assembly.GetEntryAssembly( );
        return assembly?.GetName( ).Version?.ToString( ) ?? "0.0.1";
    }

    /// <summary>
    /// Configures Serilog for file logging with rotation and retention.
    /// Writes logs to logs/{projectName}-.log relative to the application directory.
    /// </summary>
    /// <param name="builder">The web application builder to configure.</param>
    /// <param name="projectName">The name of the project (used for the log file name).</param>
    /// <returns>The configured builder.</returns>
    public static WebApplicationBuilder ConfigureFileLogging( this WebApplicationBuilder builder, string projectName ) {
        // Use LogDirPath from configuration or default to ./logs
        string logDir = builder.Configuration["BridgeBeats:LogDirPath"] ?? "./logs";
        string logPath = Path.Combine(logDir, $"{projectName}-.log");

        try {
            if (!string.IsNullOrEmpty( logDir ) && !Directory.Exists( logDir )) {
                _ = Directory.CreateDirectory( logDir );
            }
        } catch (IOException ex) {
            Console.WriteLine( $"Warning: Failed to validate/create log directory: {ex.Message}" );
            // Fall back to not configuring file logging
            return builder;
        } catch (UnauthorizedAccessException ex) {
            Console.WriteLine( $"Warning: Failed to validate/create log directory due to insufficient permissions: {ex.Message}" );
            // Fall back to not configuring file logging
            return builder;
        }

        Log.Logger = new LoggerConfiguration( )
            .ReadFrom.Configuration( builder.Configuration )
            .Filter.ByExcluding( logEvent => {
                // Exclude successful health check requests from logs (but keep failures)
                // This filters out Information level logs for /health endpoint
                if (logEvent.Level != Serilog.Events.LogEventLevel.Information) {
                    return false; // Don't exclude warnings, errors, etc.
                }

                // Check if this is a log event related to health endpoint
                string? sourceContext = logEvent.Properties.TryGetValue("SourceContext", out Serilog.Events.LogEventPropertyValue? sourceValue)
                    ? sourceValue.ToString().Trim('"')
                    : null;

                // Filter MVC controller action execution logs for health endpoint
                if (sourceContext != null &&
                    (sourceContext.Contains( "Microsoft.AspNetCore.Mvc" ) ||
                     sourceContext.Contains( "Microsoft.AspNetCore.Routing" ))) {

                    if (logEvent.Properties.TryGetValue( "ActionName", out Serilog.Events.LogEventPropertyValue? actionValue )) {
                        string actionName = actionValue.ToString();
                        if (actionName.Contains( "Health", StringComparison.OrdinalIgnoreCase )) {
                            return true; // Exclude health check related MVC logs
                        }
                    }

                    string messageText = logEvent.RenderMessage();
                    if (messageText.Contains( "Health", StringComparison.OrdinalIgnoreCase )) {
                        return true; // Exclude health check related MVC logs
                    }
                }

                // Filter HTTP request completion logs for successful health checks
                if (logEvent.Properties.TryGetValue( "RequestPath", out Serilog.Events.LogEventPropertyValue? pathValue ) &&
                    (pathValue.ToString( ).Trim( '"' ).Equals( EndpointPaths.Health, StringComparison.OrdinalIgnoreCase ) ||
                     pathValue.ToString( ).Trim( '"' ).Equals( EndpointPaths.Alive, StringComparison.OrdinalIgnoreCase )) &&
                    logEvent.Properties.TryGetValue( "StatusCode", out Serilog.Events.LogEventPropertyValue? statusValue ) &&
                    statusValue.ToString( ) == "200") {
                    return true; // Exclude successful health check HTTP logs
                }

                return false;
            } )
            .WriteTo.Console( )
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 50 * 1024 * 1024, // 50MB
                retainedFileCountLimit: 5, // 5 days retention
                rollOnFileSizeLimit: true,
                shared: false
            )
            .CreateLogger( );

        _ = builder.Host.UseSerilog( );

        return builder;
    }
}
