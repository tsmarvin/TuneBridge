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

            // Configure TuneBridge services (this loads configuration and sets up logging)
            _ = builder.ConfigureTuneBridgeServices( args );

            WebApplication app = await builder.ConfigureTuneBridgeAsync( );

            try {
                app.Run( );
            } finally {
                Log.CloseAndFlush();
            }
        }
    }
}
