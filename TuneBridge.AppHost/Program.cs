namespace TuneBridge.AppHost {
    /// <summary>
    /// Entry point for the TuneBridge AppHost, which orchestrates the TuneBridge application
    /// and connects it to the Aspire Dashboard for observability.
    /// </summary>
    public class Program {
        /// <summary>
        /// Main entry point for the AppHost. Configures and starts the distributed application
        /// including the TuneBridge web application with Aspire Dashboard integration.
        /// </summary>
        /// <param name="args">Command-line arguments for configuration overrides.</param>
        public static void Main( string[] args ) {
            IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder( new DistributedApplicationOptions {
                Args = args,
                DisableDashboard = true
            } );

            // Add the TuneBridge application as an executable
            // The Aspire Dashboard runs as a separate container (defined in docker-compose.yml)
            // and TuneBridge connects to it via the OTLP endpoint
            string dotnetPath = Environment.GetEnvironmentVariable( "DOTNET_ROOT" ) ?? "/usr/share/dotnet/dotnet";
            string tunebridgeDll = Path.Combine( AppContext.BaseDirectory, "TuneBridge.dll" );

            _ = builder.AddExecutable( "tunebridge", dotnetPath, AppContext.BaseDirectory, tunebridgeDll )
                .WithHttpEndpoint( port: 10000, name: "http" );

            builder.Build( ).Run( );
        }
    }
}
