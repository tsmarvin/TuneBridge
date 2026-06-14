using BridgeBeats.Web.Configuration;
using Serilog;

namespace BridgeBeats.Web {
    /// <summary>
    /// The entry point and composition root for the BridgeBeats web host.
    /// </summary>
    public class Program {
        /// <summary>
        /// Builds and runs the web application: configures services, builds the request pipeline, runs the
        /// host, and flushes the logger on shutdown.
        /// </summary>
        /// <param name="args">The command-line arguments passed to the host.</param>
        /// <returns>A task that completes when the host has stopped.</returns>
        public static async Task Main( string[] args ) {
            WebApplicationBuilder builder = WebApplication.CreateBuilder( new WebApplicationOptions {
                Args = args,
                WebRootPath = "wwwroot"
            } );

            // Configure BridgeBeats services
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
