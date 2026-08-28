namespace BridgeBeats.Tests.Unit;

/// <summary>Pins deployment values that are required before application code can run.</summary>
[TestClass]
public sealed class DeploymentConfigurationContractTests {
    /// <summary>Compose must allow its public hosts, healthcheck, and internal loopback callers.</summary>
    [TestMethod]
    public void Compose_AllowedHosts_PreservesPublicAndLoopbackCallers( ) {
        string compose = File.ReadAllText( RepositoryFile( "containers", "docker-compose.yml" ) );

        Assert.Contains(
            "AllowedHosts: localhost;127.0.0.1;bridgebeats.link;",
            compose
        );
        Assert.Contains( "http://localhost:10000/health", compose );
    }

    /// <summary>Tracked Compose must not contain syntactically usable shared placeholder secrets.</summary>
    [TestMethod]
    public void Compose_DoesNotShipChangeMeSecrets( ) {
        string compose = File.ReadAllText( RepositoryFile( "containers", "docker-compose.yml" ) );

        Assert.DoesNotContain( "CHANGE_ME_", compose );
    }

    private static string RepositoryFile( params string[] segments ) {
        DirectoryInfo? directory = new( AppContext.BaseDirectory );
        while (directory is not null && !File.Exists( Path.Combine( directory.FullName, "BridgeBeats.sln" ) )) {
            directory = directory.Parent;
        }

        Assert.IsNotNull( directory );
        return Path.Combine( [directory.FullName, .. segments] );
    }
}
