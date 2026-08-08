namespace BridgeBeats.Tests.Integration;

/// <summary>Verifies the Web test host never mutates process-wide runtime configuration.</summary>
[TestClass]
[DoNotParallelize]
[TestCategory( "Integration" )]
[TestCategory( "Docker" )]
public class CustomWebApplicationFactoryEnvIsolationTests {
    private static readonly string[] s_atProtoKeys = [
        "BridgeBeats__ATProtoIdentifier",
        "BridgeBeats__ATProtoPassword",
        "BridgeBeats__ATProtoUserDID"
    ];

    private readonly Dictionary<string, string?> _ambientSnapshot = [];

    /// <summary>Snapshots ambient values before each test.</summary>
    [TestInitialize]
    public void SnapshotAmbient( ) {
        foreach (string key in s_atProtoKeys) {
            _ambientSnapshot[key] = Environment.GetEnvironmentVariable( key );
        }
    }

    /// <summary>Restores ambient values changed by this test's positive control.</summary>
    [TestCleanup]
    public void RestoreAmbient( ) {
        foreach (string key in s_atProtoKeys) {
            Environment.SetEnvironmentVariable( key, _ambientSnapshot[key] );
        }
    }

    /// <summary>The factory uses isolated configuration and leaves non-empty process values intact.</summary>
    [TestMethod]
    public void FactoryLifetime_DoesNotMutateAmbientConfiguration( ) {
        foreach (string key in s_atProtoKeys) {
            Environment.SetEnvironmentVariable( key, $"sentinel-{key}" );
        }

        using (CustomWebApplicationFactory factory = new( )) {
            foreach (string key in s_atProtoKeys) {
                Assert.AreEqual( $"sentinel-{key}", Environment.GetEnvironmentVariable( key ) );
            }
        }

        foreach (string key in s_atProtoKeys) {
            Assert.AreEqual( $"sentinel-{key}", Environment.GetEnvironmentVariable( key ) );
        }
    }
}
