namespace TuneBridge.AppHost {
    public class Program {
        private static void Main( string[] args ) {
            IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

            IResourceBuilder<ProjectResource> apiService = builder
                                                .AddProject<Projects.TuneBridge_ApiService>( "apiservice" )
                                                .WithHttpHealthCheck( "/health" );

            _ = builder
                .AddProject<Projects.TuneBridge_JetstreamMonitor>( "jetstreammonitor" )
                .WithReference( apiService )
                .WaitFor( apiService );

            _ = builder
                .AddProject<Projects.TuneBridge_Web>( "webfrontend" )
                .WithExternalHttpEndpoints( )
                .WithHttpHealthCheck( "/health" )
                .WithReference( apiService )
                .WaitFor( apiService );

            builder
                .Build( )
                .Run( );
        }
    }
}
