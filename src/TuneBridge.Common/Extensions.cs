using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using TuneBridge.Common.Contracts.Constants;

namespace TuneBridge.Common;

public static class Extensions {
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    /// <summary>
    /// Configures the application settings by adding command line arguments, environment variables, and the appsettings.json file.
    /// </summary>
    /// <param name="config">The configuration builder to extend.</param>
    /// <param name="args">Command line arguments passed to the application.</param>
    /// <returns>The updated <see cref="IConfigurationBuilder"/>.</returns>
    public static IConfigurationBuilder ConfigureAppSettings(
        this IConfigurationBuilder config,
        string[] args,
        string appSettingsPath = "appsettings.json"
    ) => config.AddJsonFile(
            path: appSettingsPath,
            optional: false,
            reloadOnChange: false
        ).AddCommandLine( args )
        .AddEnvironmentVariables( );

    public static TBuilder AddCommon<TBuilder>(
        this TBuilder builder
    ) where TBuilder : IHostApplicationBuilder {
        _ = builder.ConfigureOpenTelemetry( );

        _ = builder.AddDefaultHealthChecks( );

        _ = builder.Services.AddServiceDiscovery( );

        _ = builder.Services.ConfigureHttpClientDefaults( http => {
            // Turn on resilience by default
            _ = http.AddStandardResilienceHandler( );

            // Turn on service discovery by default
            _ = http.AddServiceDiscovery( );
        } );
        builder.ConfigureSerilog( );
        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(
        this TBuilder builder
    ) where TBuilder : IHostApplicationBuilder {
        _ = builder.Logging.AddOpenTelemetry( logging => {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        } );

        _ = builder.Services.AddOpenTelemetry( )
            .WithMetrics( metrics => {
                _ = metrics.AddAspNetCoreInstrumentation( )
                    .AddHttpClientInstrumentation( )
                    .AddRuntimeInstrumentation( );
            } )
            .WithTracing( tracing => {
                _ = tracing.AddSource( builder.Environment.ApplicationName )
                    .AddAspNetCoreInstrumentation( tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments( HealthEndpointPath )
                            && !context.Request.Path.StartsWithSegments( AlivenessEndpointPath )
                    )
                    .AddGrpcClientInstrumentation( )
                    .AddHttpClientInstrumentation( );
            } );

        _ = builder.AddOpenTelemetryExporters( );

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>( this TBuilder builder ) where TBuilder : IHostApplicationBuilder {
        bool useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter) {
            _ = builder.Services.AddOpenTelemetry( ).UseOtlpExporter( );
        }
        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>( this TBuilder builder ) where TBuilder : IHostApplicationBuilder {
        _ = builder.Services.AddHealthChecks( )
            // Add a default liveness check to ensure app is responsive
            .AddCheck( "self", ( ) => HealthCheckResult.Healthy( ), ["live"] );

        return builder;
    }

    public static WebApplication MapDefaultEndpoints( this WebApplication app ) {
        if (app.Environment.IsDevelopment( )) {
            // All health checks must pass for app to be considered ready to accept traffic after starting
            _ = app.MapHealthChecks( HealthEndpointPath );

            // Only health checks tagged with the "live" tag must pass for app to be considered alive
            _ = app.MapHealthChecks( AlivenessEndpointPath, new HealthCheckOptions {
                Predicate = r => r.Tags.Contains( "live" )
            } );
        }

        return app;
    }

    /// <summary>
    /// Configures Serilog for file logging with rotation and retention.
    /// </summary>
    /// <param name="builder">The web application builder to configure.</param>
    private static void ConfigureSerilog<TBuilder>(
        this TBuilder builder
    ) where TBuilder : IHostApplicationBuilder {
        string logPath = builder.Configuration["TuneBridge:LogFilePath"] ?? "./logs/tunebridge-.log";

        try {
            string? logDir = Path.GetDirectoryName( logPath );
            if (!string.IsNullOrEmpty( logDir ) && !Directory.Exists( logDir )) {
                _ = Directory.CreateDirectory( logDir );
            }
        } catch (IOException ex) {
            Console.WriteLine( $"Warning: Failed to validate/create log directory: {ex.Message}" );
            // Fall back to not configuring file logging
            return;
        } catch (UnauthorizedAccessException ex) {
            Console.WriteLine( $"Warning: Failed to validate/create log directory due to insufficient permissions: {ex.Message}" );
            // Fall back to not configuring file logging
            return;
        }

        Log.Logger = new LoggerConfiguration( )
                        .ReadFrom.Configuration( builder.Configuration )
                        .Filter.ByExcluding(
            logEvent => {
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
            .WriteTo.Console( )
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 10 * 1024 * 1024, // 10MB
                retainedFileCountLimit: 5,
                rollOnFileSizeLimit: true,
                shared: false
            )
            .CreateLogger( );

        _ = builder.Services.AddLogging( b => b.AddSerilog( dispose: true ) );
    }

    /// <summary>
    /// Extracts all values of a named group from regex matches.
    /// </summary>
    /// <param name="regex">The regex to match against.</param>
    /// <param name="input">The input string to search.</param>
    /// <param name="groupName">The name of the group to extract.</param>
    /// <returns>An enumerable of all group values found.</returns>
    public static IEnumerable<string> GetGroupValues( this Regex regex, string input, string groupName ) {
        return regex.Matches( input )
            .Where( match => match.Groups.ContainsKey( groupName ) )
            .Select( match => match.Groups[groupName].Value );
    }

    /// <summary>
    /// Gets the description attribute value from an enum, or returns the enum's string representation if no description exists.
    /// </summary>
    /// <typeparam name="T">The enum type.</typeparam>
    /// <param name="enumValue">The enum value.</param>
    /// <returns>The description attribute value or the enum's string representation.</returns>
    /// <exception cref="ArgumentException">Thrown if T is not an enum type.</exception>
    public static string GetDescription<T>( this T enumValue )
        where T : struct {
        Type type = enumValue.GetType();
        if (!type.IsEnum) {
            throw new ArgumentException( "Must be an Enum!", nameof( enumValue ) );
        }

        //Tries to find a DescriptionAttribute for a potential friendly name
        //for the enum
        string? value = enumValue.ToString( );
        if (value != null) {
            MemberInfo[] memberInfo = type.GetMember(value);
            if (memberInfo.Length > 0) {
                object[] attrs = memberInfo[0].GetCustomAttributes(typeof(DescriptionAttribute), false);

                if (attrs != null && attrs.Length > 0) {
                    //Pull out the description value
                    return ((DescriptionAttribute)attrs[0]).Description;
                }
            }
            return value;
        }
        //If we have no description attribute, just return the ToString of the enum
        return string.Empty;
    }
}
