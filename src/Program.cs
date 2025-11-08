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
        public static void Main( string[] args ) =>
            WebApplication.CreateBuilder( new WebApplicationOptions() {
                ApplicationName = "TuneBridge",
                Args = args,
                WebRootPath = "Web/wwwroot"
            })
            .ConfigureTuneBridgeServices( args )
            .ConfigureTuneBridge( )
            .Run( );
    }
}
