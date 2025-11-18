using Microsoft.EntityFrameworkCore;
using TuneBridge.Common;
using TuneBridge.JetStreamMonitor.Configuration;
using TuneBridge.JetStreamMonitor.Domain;
using TuneBridge.JetStreamMonitor.Infrastructure;

namespace TuneBridge.JetStreamMonitor {
    public class Program {
        public static void Main( string[] args ) {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
            AppSettings settings = new();
            builder.Configuration.GetRequiredSection( "JetStreamMonitor" ).Bind( settings );
            _ = builder.Services.AddDbContextFactory<JetstreamMonitorContext>( options =>
                options.UseSqlite( settings.DBConnectionString )
            );

            // Register HTTP client for TuneBridge API
            _ = builder.Services.AddHttpClient<TuneBridgeApiClient>(
                h => h.BaseAddress = new( "https+http://apiservice" )
            );

            UriBuilder uriBuilder = new( settings.JetstreamEndpoint ) {
                Query = "wantedCollections=app.bsky.feed.post"
            };

            // Register services
            _ = builder.Services.AddSingleton( uriBuilder );
            _ = builder.Services.AddSingleton<MusicLinkDetector>( );
            _ = builder.Services.AddHostedService<JetstreamMonitorService>( );

            IHost host = builder.Build();
            host.Run( );
        }
    }
}
