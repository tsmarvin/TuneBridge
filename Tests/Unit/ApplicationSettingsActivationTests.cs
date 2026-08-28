using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Settings;

namespace BridgeBeats.Tests.Unit;

/// <summary>Tests the activation gate used before AppHost composes the full runtime.</summary>
[TestClass]
public sealed class ApplicationSettingsActivationTests {
    /// <summary>Verifies defaults do not activate the full runtime.</summary>
    [TestMethod]
    public void HasRunnableProvider_WithEmptySettings_ReturnsFalse( ) {
        bool result = ApplicationSettingsActivation.HasRunnableProvider(
            new ApplicationSettingsValues( ),
            new ApplicationSettingsSecrets( )
        );

        Assert.IsFalse( result );
    }

    /// <summary>Verifies an identifier without its protected credential is not runnable.</summary>
    [TestMethod]
    public void HasRunnableProvider_WithPartialProvider_ReturnsFalse( ) {
        ApplicationSettingsValues values = new( ) { SpotifyClientId = "spotify-client" };

        bool result = ApplicationSettingsActivation.HasRunnableProvider(
            values,
            new ApplicationSettingsSecrets( )
        );

        Assert.IsFalse( result );
    }

    /// <summary>Verifies a complete provider activates direct-provider mode.</summary>
    [TestMethod]
    public void HasRunnableProvider_InDirectModeWithCompleteProvider_ReturnsTrue( ) {
        ApplicationSettingsValues values = new( ) { SpotifyClientId = "spotify-client" };
        ApplicationSettingsSecrets secrets = new( ) {
            SpotifyClientSecret = ApplicationSecretValue.FromPlaintext( "spotify-secret" )
        };

        bool result = ApplicationSettingsActivation.HasRunnableProvider( values, secrets );

        Assert.IsTrue( result );
    }

    /// <summary>Verifies worker mode does not activate a configured but disabled worker.</summary>
    [TestMethod]
    public void HasRunnableProvider_InWorkerModeWithDisabledProvider_ReturnsFalse( ) {
        ApplicationSettingsValues values = new( ) {
            SpotifyClientId = "spotify-client",
            Workers = new ApplicationWorkerSettings {
                UseWorkerServices = true,
                SpotifyWorkerEnabled = false
            }
        };
        ApplicationSettingsSecrets secrets = new( ) {
            SpotifyClientSecret = ApplicationSecretValue.FromPlaintext( "spotify-secret" )
        };

        bool result = ApplicationSettingsActivation.HasRunnableProvider( values, secrets );

        Assert.IsFalse( result );
    }

    /// <summary>Verifies worker mode activates a configured and enabled worker.</summary>
    [TestMethod]
    public void HasRunnableProvider_InWorkerModeWithEnabledProvider_ReturnsTrue( ) {
        ApplicationSettingsValues values = new( ) {
            SpotifyClientId = "spotify-client",
            Workers = new ApplicationWorkerSettings {
                UseWorkerServices = true,
                SpotifyWorkerEnabled = true
            }
        };
        ApplicationSettingsSecrets secrets = new( ) {
            SpotifyClientSecret = ApplicationSecretValue.FromPlaintext( "spotify-secret" )
        };

        bool result = ApplicationSettingsActivation.HasRunnableProvider( values, secrets );

        Assert.IsTrue( result );
    }

    /// <summary>A provider alone cannot activate Web without the API-key hashing salt.</summary>
    [TestMethod]
    public void CanStartWeb_WithoutApiKeySalt_ReturnsFalse( ) {
        ApplicationSettingsValues values = new( ) { SpotifyClientId = "spotify-client" };
        ApplicationSettingsSecrets secrets = new( ) {
            SpotifyClientSecret = ApplicationSecretValue.FromPlaintext( "spotify-secret" )
        };

        Assert.IsFalse( ApplicationSettingsActivation.CanStartWeb( values, secrets ) );
    }

    /// <summary>A complete provider and API-key salt satisfy Web's activation invariants.</summary>
    [TestMethod]
    public void CanStartWeb_WithCompleteRuntime_ReturnsTrue( ) {
        ApplicationSettingsValues values = new( ) { SpotifyClientId = "spotify-client" };
        ApplicationSettingsSecrets secrets = new( ) {
            SpotifyClientSecret = ApplicationSecretValue.FromPlaintext( "spotify-secret" ),
            ApiKeySalt = ApplicationSecretValue.FromPlaintext( "activation-test-api-key-salt-000000000" )
        };

        Assert.IsTrue( ApplicationSettingsActivation.CanStartWeb( values, secrets ) );
    }

    /// <summary>Direct-provider mode never starts queue workers.</summary>
    [TestMethod]
    public void CanStartQueueTopology_InDirectMode_ReturnsFalse( ) {
        ApplicationSettingsValues values = new( ) { SpotifyClientId = "spotify-client" };
        ApplicationSettingsSecrets secrets = new( ) {
            SpotifyClientSecret = ApplicationSecretValue.FromPlaintext( "spotify-secret" )
        };

        Assert.IsFalse( ApplicationSettingsActivation.CanStartQueueTopology( values, secrets ) );
    }

    /// <summary>ATProto workers require both an enabled provider worker and service account.</summary>
    [TestMethod]
    public void CanStartAtProtoWorkerTopology_WithoutAtProtoCredentials_ReturnsFalse( ) {
        ApplicationSettingsValues values = new( ) {
            SpotifyClientId = "spotify-client",
            Workers = new ApplicationWorkerSettings {
                UseWorkerServices = true,
                SpotifyWorkerEnabled = true
            }
        };
        ApplicationSettingsSecrets secrets = new( ) {
            SpotifyClientSecret = ApplicationSecretValue.FromPlaintext( "spotify-secret" )
        };

        Assert.IsFalse( ApplicationSettingsActivation.CanStartAtProtoWorkerTopology( values, secrets ) );
    }
}
