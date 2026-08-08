using System.Net;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Web.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Unit;

/// <summary>Tests the deliberately restricted first-run Web process.</summary>
[TestClass]
public sealed class SetupRequiredStartupTests {
    /// <summary>Gets the MSTest context used for cooperative cancellation.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Verifies an empty settings store does not construct application services and exposes only a
    /// fixed configuration-required response outside the health endpoints.
    /// </summary>
    [TestMethod]
    public async Task SetupRequired_StartsRestrictedWebProcess( ) {
        WebApplicationBuilder builder = WebApplication.CreateBuilder( new WebApplicationOptions {
            EnvironmentName = "Testing"
        } );
        string artifactRoot = TestArtifacts.CreateDirectory( "setup-required" );
        _ = builder.Configuration.AddInMemoryCollection( new Dictionary<string, string?> {
            ["BridgeBeats:SetupRequired"] = "true",
            ["BridgeBeats:SettingsRevision"] = string.Empty,
            ["BridgeBeats:IdentityConnectionString"] = $"Data Source={Path.Combine( artifactRoot, "settings.db" )}",
            ["BridgeBeats:DataProtectionKeyPath"] = Path.Combine( artifactRoot, "keys" )
        } );
        _ = builder.WebHost.UseTestServer( );

        _ = builder.ConfigureBridgeBeatsServices( [] );

        Assert.DoesNotContain( typeof( IConnectionMultiplexer ), builder.Services.Select( descriptor => descriptor.ServiceType ) );
        Assert.Contains( typeof( IApplicationSettingsService ), builder.Services.Select( descriptor => descriptor.ServiceType ) );

        await using WebApplication app = await builder.ConfigureBridgeBeatsAsync( );
        await app.StartAsync( TestContext.CancellationToken );
        using HttpClient client = app.GetTestClient( );

        using HttpResponseMessage response = await client.GetAsync( "/", TestContext.CancellationToken );
        string body = await response.Content.ReadAsStringAsync( TestContext.CancellationToken );
        using HttpResponseMessage health = await client.GetAsync( "/health", TestContext.CancellationToken );

        Assert.AreEqual( HttpStatusCode.ServiceUnavailable, response.StatusCode );
        Assert.AreEqual( HttpStatusCode.ServiceUnavailable, health.StatusCode );
        Assert.Contains( "Configuration Required", body );
        Assert.DoesNotContain( "BridgeBeats:", body );
    }
}
