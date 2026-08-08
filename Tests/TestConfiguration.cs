using Microsoft.Extensions.Configuration;

namespace BridgeBeats.Tests;

/// <summary>
/// Loads configuration for tests that need real provider credentials from .NET user-secrets or
/// environment variables.
/// Also exposes helpers that report which provider credentials are present.
/// </summary>
public static class TestConfiguration {
    /// <summary>Guards lazy, thread-safe construction of the configuration root.</summary>
    private static readonly Lock s_lock = new( );

    /// <summary>
    /// The lazily built configuration root, shared across the test run.
    /// </summary>
    public static IConfiguration Configuration {
        get {
            if (field is null) {
                lock (s_lock) {
                    field ??= LoadConfiguration( );
                }
            }
            return field;
        }
    }

    /// <summary>
    /// Builds the configuration root using the same user-secrets identifier as Web/AppHost, with
    /// test-process environment variables taking precedence for CI.
    /// </summary>
    private static IConfiguration LoadConfiguration( ) {
        return new ConfigurationBuilder( )
            .AddUserSecrets<Web.Program>( optional: true )
            .AddEnvironmentVariables( )
            .Build( );
    }

    /// <summary>
    /// Reads a configuration value by key, returning <c>null</c> when the value is missing or blank.
    /// </summary>
    /// <param name="key">The configuration key to read.</param>
    /// <returns>The value, or <c>null</c> if absent or whitespace.</returns>
    public static string? GetValue( string key ) {
        string? value = Configuration[key];
        return string.IsNullOrWhiteSpace( value ) ? null : value;
    }

    /// <summary>
    /// Reports whether both the Spotify client id and secret are configured.
    /// </summary>
    /// <returns><c>true</c> when Spotify credentials are present.</returns>
    public static bool HasSpotifyCredentials( ) =>
        !string.IsNullOrWhiteSpace( GetValue( "BridgeBeats:SpotifyClientId" ) ) &&
        !string.IsNullOrWhiteSpace( GetValue( "BridgeBeats:SpotifyClientSecret" ) );

    /// <summary>
    /// Reports whether the Apple Music team id, key id, and private-key contents are all configured.
    /// </summary>
    /// <returns><c>true</c> when Apple Music credentials are present.</returns>
    public static bool HasAppleMusicCredentials( ) =>
        !string.IsNullOrWhiteSpace( GetValue( "BridgeBeats:AppleTeamId" ) ) &&
        !string.IsNullOrWhiteSpace( GetValue( "BridgeBeats:AppleKeyId" ) ) &&
        !string.IsNullOrWhiteSpace( GetValue( "BridgeBeats:ApplePrivateKey" ) );

    /// <summary>
    /// Reports whether both the Tidal client id and secret are configured.
    /// </summary>
    /// <returns><c>true</c> when Tidal credentials are present.</returns>
    public static bool HasTidalCredentials( ) =>
        !string.IsNullOrWhiteSpace( GetValue( "BridgeBeats:TidalClientId" ) ) &&
        !string.IsNullOrWhiteSpace( GetValue( "BridgeBeats:TidalClientSecret" ) );

}
