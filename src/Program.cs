using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using Serilog;
using TuneBridge.Configuration;

namespace TuneBridge {
    /// <summary>
    /// Main entry point for the TuneBridge web application.
    /// </summary>
    public class Program {
        /// <summary>
        /// Application entry point. Initializes the ASP.NET Core host, configures services,
        /// and starts the web server.
        /// </summary>
        /// <param name="args">Command-line arguments for configuration overrides.</param>
        public static async Task Main( string[] args ) {
            WebApplicationBuilder builder = WebApplication.CreateBuilder( new WebApplicationOptions( ) {
                ApplicationName = "TuneBridge",
                Args = args,
                WebRootPath = "Web/wwwroot"
            } );

            // Configure Serilog for file logging with rotation
            ConfigureSerilog( builder );

            // Configure OpenTelemetry for Aspire Dashboard integration
            ConfigureOpenTelemetry( builder );

            WebApplication app = await builder
                .ConfigureTuneBridgeServices( args )
                .ConfigureTuneBridgeAsync( );

            app.Run( );
        }

        private static void ConfigureSerilog( WebApplicationBuilder builder ) {
            string logPath = builder.Configuration["Logging:FilePath"] ?? "/app/data/logs/tunebridge-.log";

            Log.Logger = new LoggerConfiguration( )
                .ReadFrom.Configuration( builder.Configuration )
                .WriteTo.File(
                    path: logPath,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 10 * 1024 * 1024, // 10MB
                    retainedFileCountLimit: 5,
                    rollOnFileSizeLimit: true,
                    shared: false
                )
                .CreateLogger( );

            _ = builder.Host.UseSerilog( );
        }

        private static void ConfigureOpenTelemetry( WebApplicationBuilder builder ) {
            string? otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];

            // Only configure OpenTelemetry if endpoint is provided
            if (string.IsNullOrWhiteSpace( otlpEndpoint )) {
                return;
            }

            _ = builder.Logging.AddOpenTelemetry( options => {
                options.SetResourceBuilder(
                    ResourceBuilder.CreateDefault( )
                        .AddService( serviceName: "TuneBridge", serviceVersion: "0.0.1" )
                );

                options.AddOtlpExporter( otlpOptions => {
                    otlpOptions.Endpoint = new Uri( otlpEndpoint );
                } );
            } );
        }
    }
}
