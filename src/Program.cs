using BridgeBeats.Configuration;

namespace BridgeBeats {
    /// <summary>
    /// Main entry point for the BridgeBeats web application.
    /// </summary>
    public class Program {
        /// <summary>
        /// Application entry point. Initializes the ASP.NET Core host, configures services,
        /// and starts the web server.
        /// </summary>
        /// <param name="args">Command-line arguments for configuration overrides.</param>
        public static async Task Main( string[] args ) {
            WebApplicationBuilder builder = WebApplication.CreateBuilder( new WebApplicationOptions( ) {
                ApplicationName = "BridgeBeats",
                Args = args,
                WebRootPath = "Web/wwwroot"
            } );

            // Configure BridgeBeats services (this loads configuration and sets up logging)
            _ = builder.ConfigureBridgeBeatsServices( args );

            WebApplication app = await builder.ConfigureBridgeBeatsAsync( );

            try {
                app.Run( );
            } finally {
                Log.CloseAndFlush( );
            }
        }
    }
}
