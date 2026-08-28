using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Settings;

namespace BridgeBeats.Tests.Unit;

/// <summary>Pins the database-to-Web child-process projection contract.</summary>
[TestClass]
public sealed class ApplicationSettingsRuntimeProjectorTests {
    /// <summary>Every security- and topology-critical Web key is projected from one revision.</summary>
    [TestMethod]
    public void ProjectWeb_ProjectsCompleteRuntimeContract( ) {
        ApplicationSettingsSnapshot snapshot = new(
            new ApplicationSettingsValues {
                SpotifyClientId = "spotify-id",
                Workers = new ApplicationWorkerSettings {
                    UseWorkerServices = true,
                    SpotifyWorkerEnabled = true
                }
            },
            new ApplicationSettingsSecrets {
                SpotifyClientSecret = ApplicationSecretValue.FromPlaintext( "spotify-secret" ),
                ApiKeySalt = ApplicationSecretValue.FromPlaintext( "projector-api-key-salt-00000000000000" )
            },
            "revision-123",
            DateTimeOffset.UnixEpoch
        );

        IReadOnlyDictionary<string, string?> projection = ApplicationSettingsRuntimeProjector.ProjectWeb(
            snapshot,
            "Data Source=settings.db",
            "/keys",
            "caddy.example"
        );

        Assert.AreEqual( "revision-123", projection["BridgeBeats:SettingsRevision"] );
        Assert.AreEqual( "spotify-secret", projection["BridgeBeats:SpotifyClientSecret"] );
        Assert.AreEqual( "projector-api-key-salt-00000000000000", projection["BridgeBeats:ApiKeySalt"] );
        Assert.AreEqual( "true", projection["BridgeBeats:Workers:UseWorkerServices"] );
        Assert.AreEqual( "true", projection["BridgeBeats:Workers:SpotifyWorkerEnabled"] );
        Assert.AreEqual( "caddy.example", projection["BridgeBeats:Domain"] );
        Assert.AreEqual( "120", projection["BridgeBeats:Resilience:AttemptTimeoutSeconds"] );
        Assert.IsFalse( string.IsNullOrWhiteSpace( projection["BridgeBeats:QueueSnapshot"] ) );
        Assert.IsFalse( string.IsNullOrWhiteSpace( projection["BridgeBeats:SpotifyBatchSnapshot"] ) );
    }
}
