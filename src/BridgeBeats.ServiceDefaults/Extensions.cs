using System.Reflection;
using Microsoft.Extensions.Http.Resilience;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;

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

        _ = builder.Services.ConfigureHttpClientDefaults( http => {
            _ = http.AddServiceDiscovery( );

            IHttpStandardResiliencePipelineBuilder resilienceBuilder = http.AddStandardResilienceHandler( options => {
                options.Retry.BackoffType = DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                options.Retry.MaxRetryAttempts = 5;
                options.Retry.Delay = TimeSpan.FromSeconds( 3 );
                options.Retry.MaxDelay = TimeSpan.FromMinutes( 5 );
                options.Retry.ShouldRetryAfterHeader = true;
                options.Retry.DisableForUnsafeHttpMethods( );

                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes( 10 );
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds( 10 );
            } );

            _ = resilienceBuilder.SelectPipelineByAuthority( ).Configure( ( options, sp ) => {
                ILogger logger = sp.GetRequiredService<ILoggerFactory>( ).CreateLogger( "HttpResilience" );
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
                    .AddRuntimeInstrumentation( );

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
}
