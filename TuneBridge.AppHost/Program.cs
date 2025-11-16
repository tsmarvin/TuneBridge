using System.Diagnostics;

namespace TuneBridge.AppHost {
    /// <summary>
    /// Entry point for the TuneBridge AppHost, which provides unified startup
    /// for the TuneBridge application with Aspire Dashboard integration.
    /// </summary>
    public class Program {
        /// <summary>
        /// Main entry point for the AppHost. Starts the TuneBridge web application
        /// with unified configuration and telemetry.
        /// </summary>
        /// <param name="args">Command-line arguments for configuration overrides.</param>
        public static async Task<int> Main( string[] args ) {
            Console.WriteLine( "========================================" );
            Console.WriteLine( "TuneBridge AppHost starting..." );
            Console.WriteLine( $"Working directory: {Environment.CurrentDirectory}" );
            Console.WriteLine( $"Base directory: {AppContext.BaseDirectory}" );
            Console.WriteLine( "========================================\n" );

            // Locate the TuneBridge executable
            string tunebridgePath = Path.Combine( AppContext.BaseDirectory, "TuneBridge" );
            if (!File.Exists( tunebridgePath )) {
                tunebridgePath = Path.Combine( AppContext.BaseDirectory, "TuneBridge.dll" );
            }

            if (!File.Exists( tunebridgePath )) {
                Console.Error.WriteLine( $"ERROR: Could not find TuneBridge executable at {tunebridgePath}" );
                return 1;
            }

            Console.WriteLine( $"Starting TuneBridge from: {tunebridgePath}\n" );

            // Start the TuneBridge application as a child process
            ProcessStartInfo startInfo = new( ) {
                FileName = tunebridgePath.EndsWith( ".dll" ) ? "dotnet" : tunebridgePath,
                Arguments = tunebridgePath.EndsWith( ".dll" ) ? $"\"{tunebridgePath}\"" : string.Empty,
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                WorkingDirectory = AppContext.BaseDirectory
            };

            // Pass through all arguments
            if (args.Length > 0) {
                startInfo.Arguments += " " + string.Join( " ", args );
            }

            using Process? process = Process.Start( startInfo );
            if (process == null) {
                Console.Error.WriteLine( "ERROR: Failed to start TuneBridge process" );
                return 1;
            }

            Console.WriteLine( $"TuneBridge started with PID: {process.Id}" );
            Console.WriteLine( "AppHost will now wait for TuneBridge to complete...\n" );

            // Wait for the process to exit
            await process.WaitForExitAsync( );

            Console.WriteLine( $"\nTuneBridge exited with code: {process.ExitCode}" );
            return process.ExitCode;
        }
    }
}
