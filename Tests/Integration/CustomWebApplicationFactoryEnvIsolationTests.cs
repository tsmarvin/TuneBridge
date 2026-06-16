namespace BridgeBeats.Tests.Integration;

/// <summary>
/// Verifies that <see cref="CustomWebApplicationFactory"/> captures the prior process values of the
/// three ATProto environment variables in its constructor and fully restores them on dispose,
/// with no residual state after dispose — including when the keys started unset (null, not empty string).
/// </summary>
/// <remarks>
/// [DoNotParallelize]: the factory constructor sets process-wide environment variables
/// (BridgeBeats__ATProtoIdentifier, BridgeBeats__ATProtoPassword, BridgeBeats__ATProtoUserDID)
/// and restores them in Dispose. Running in parallel with another class that mutates overlapping
/// keys would cause order-dependent contamination. This class must run in the serial phase.
/// </remarks>
[TestClass]
[DoNotParallelize]
[TestCategory( "Integration" )]
[TestCategory( "Docker" )]
public class CustomWebApplicationFactoryEnvIsolationTests {

    private const string IdentifierKey = "BridgeBeats__ATProtoIdentifier";
    private const string PasswordKey   = "BridgeBeats__ATProtoPassword";
    private const string UserDidKey    = "BridgeBeats__ATProtoUserDID";

    private static readonly string[] s_atProtoKeys = [ IdentifierKey, PasswordKey, UserDidKey ];

    // Ambient env values captured before each test; restored after each test so this class
    // does not itself contaminate the suite.
    private readonly Dictionary<string, string?> _ambientSnapshot = [];

    /// <summary>Snapshot the ambient env values before each test.</summary>
    [TestInitialize]
    public void SnapshotAmbient( ) {
        foreach (string key in s_atProtoKeys) {
            _ambientSnapshot[key] = Environment.GetEnvironmentVariable( key );
        }
    }

    /// <summary>Restore the ambient env values after each test.</summary>
    [TestCleanup]
    public void RestoreAmbient( ) {
        foreach (string key in s_atProtoKeys) {
            Environment.SetEnvironmentVariable( key, _ambientSnapshot.TryGetValue( key, out string? v ) ? v : null );
        }
    }

    /// <summary>
    /// After construction, the three ATProto keys are forced to the empty string; after dispose,
    /// they are restored to their pre-construction values (non-empty sentinel case).
    /// The mid-construction check ensures the ctor actually set the keys, ruling out a vacuous restore.
    /// </summary>
    [TestMethod]
    public void DisposedFactory_RestoresPriorValue_WhenKeysHadNonEmptyValues( ) {
        // Arrange: plant known non-empty baseline values
        Environment.SetEnvironmentVariable( IdentifierKey, "QA_PRIOR_SENTINEL_Identifier" );
        Environment.SetEnvironmentVariable( PasswordKey, "QA_PRIOR_SENTINEL_Password" );
        Environment.SetEnvironmentVariable( UserDidKey, "QA_PRIOR_SENTINEL_UserDID" );

        CustomWebApplicationFactory factory = new( );

        // Positive control: verify the ctor forced each key empty
        Assert.AreEqual( "", Environment.GetEnvironmentVariable( IdentifierKey ), "ctor must force key to \"\" (IdentifierKey)" );
        Assert.AreEqual( "", Environment.GetEnvironmentVariable( PasswordKey ), "ctor must force key to \"\" (PasswordKey)" );
        Assert.AreEqual( "", Environment.GetEnvironmentVariable( UserDidKey ), "ctor must force key to \"\" (UserDidKey)" );

        // Act
        factory.Dispose( );

        // Assert: keys are restored to their pre-construction sentinel values
        Assert.AreEqual( "QA_PRIOR_SENTINEL_Identifier", Environment.GetEnvironmentVariable( IdentifierKey ) );
        Assert.AreEqual( "QA_PRIOR_SENTINEL_Password", Environment.GetEnvironmentVariable( PasswordKey ) );
        Assert.AreEqual( "QA_PRIOR_SENTINEL_UserDID", Environment.GetEnvironmentVariable( UserDidKey ) );
    }

    /// <summary>
    /// After construction, the three ATProto keys are forced to the empty string; after dispose,
    /// they are restored to null (absent) — not to the empty string — when the keys started unset.
    /// This is the null-correctness discriminator: a wrong implementation that coerces null to ""
    /// will pass the sentinel test above but fail here.
    /// </summary>
    [TestMethod]
    public void DisposedFactory_RestoresAbsent_WhenKeysWereUnset( ) {
        // Arrange: ensure the keys are unset before construction
        foreach (string key in s_atProtoKeys) {
            Environment.SetEnvironmentVariable( key, null );
        }

        CustomWebApplicationFactory factory = new( );

        // Positive control: verify the ctor forced each key empty
        Assert.AreEqual( "", Environment.GetEnvironmentVariable( IdentifierKey ), "ctor must force key to \"\" (IdentifierKey)" );
        Assert.AreEqual( "", Environment.GetEnvironmentVariable( PasswordKey ), "ctor must force key to \"\" (PasswordKey)" );
        Assert.AreEqual( "", Environment.GetEnvironmentVariable( UserDidKey ), "ctor must force key to \"\" (UserDidKey)" );

        // Act
        factory.Dispose( );

        // Assert: keys are absent (null), not empty-string sentinels
        Assert.IsNull( Environment.GetEnvironmentVariable( IdentifierKey ), "key must be absent (null) after dispose — not coerced to \"\"" );
        Assert.IsNull( Environment.GetEnvironmentVariable( PasswordKey ), "key must be absent (null) after dispose — not coerced to \"\"" );
        Assert.IsNull( Environment.GetEnvironmentVariable( UserDidKey ), "key must be absent (null) after dispose — not coerced to \"\"" );
    }

    /// <summary>
    /// Two factories constructed and disposed sequentially leave the keys absent when they started
    /// absent. Catches static/once-only capture, A-residue capture (factory A failing to restore
    /// before factory B captures), and coerce-to-"" across two lifetimes simultaneously.
    /// </summary>
    [TestMethod]
    public void TwoSequentialFactories_LeaveAbsentBaseline_WhenKeysStartedUnset( ) {
        // Arrange: ensure the keys are unset
        foreach (string key in s_atProtoKeys) {
            Environment.SetEnvironmentVariable( key, null );
        }

        // Factory A lifetime
        CustomWebApplicationFactory factoryA = new( );

        // Positive control: A's ctor forced the keys empty
        Assert.AreEqual( "", Environment.GetEnvironmentVariable( IdentifierKey ), "A's ctor must force key to \"\" (IdentifierKey)" );
        Assert.AreEqual( "", Environment.GetEnvironmentVariable( PasswordKey ), "A's ctor must force key to \"\" (PasswordKey)" );
        Assert.AreEqual( "", Environment.GetEnvironmentVariable( UserDidKey ), "A's ctor must force key to \"\" (UserDidKey)" );

        factoryA.Dispose( );

        // After A's dispose the absent baseline is restored
        Assert.IsNull( Environment.GetEnvironmentVariable( IdentifierKey ), "keys must be absent after A's dispose" );
        Assert.IsNull( Environment.GetEnvironmentVariable( PasswordKey ), "keys must be absent after A's dispose" );
        Assert.IsNull( Environment.GetEnvironmentVariable( UserDidKey ), "keys must be absent after A's dispose" );

        // Factory B lifetime — B's captured-prior must be null (restored by A), not ""
        CustomWebApplicationFactory factoryB = new( );

        // Positive control: B's ctor forced the keys empty
        Assert.AreEqual( "", Environment.GetEnvironmentVariable( IdentifierKey ), "B's ctor must force key to \"\" (IdentifierKey)" );
        Assert.AreEqual( "", Environment.GetEnvironmentVariable( PasswordKey ), "B's ctor must force key to \"\" (PasswordKey)" );
        Assert.AreEqual( "", Environment.GetEnvironmentVariable( UserDidKey ), "B's ctor must force key to \"\" (UserDidKey)" );

        factoryB.Dispose( );

        // Final assertion: still absent after both lifetimes
        Assert.IsNull( Environment.GetEnvironmentVariable( IdentifierKey ) );
        Assert.IsNull( Environment.GetEnvironmentVariable( PasswordKey ) );
        Assert.IsNull( Environment.GetEnvironmentVariable( UserDidKey ) );
    }

    /// <summary>
    /// Two factories constructed and disposed sequentially preserve a non-empty sentinel baseline.
    /// Confirms the value-present path is lifetime-stable across two factory instances.
    /// </summary>
    [TestMethod]
    public void TwoSequentialFactories_PreserveSentinelBaseline_WhenKeysHadNonEmptyValues( ) {
        // Arrange: plant known sentinel values
        Environment.SetEnvironmentVariable( IdentifierKey, "QA_PRIOR_SENTINEL_Identifier" );
        Environment.SetEnvironmentVariable( PasswordKey, "QA_PRIOR_SENTINEL_Password" );
        Environment.SetEnvironmentVariable( UserDidKey, "QA_PRIOR_SENTINEL_UserDID" );

        // Factory A lifetime
        CustomWebApplicationFactory factoryA = new( );
        factoryA.Dispose( );

        // Factory B lifetime
        CustomWebApplicationFactory factoryB = new( );
        factoryB.Dispose( );

        // Both lifetimes must leave the sentinel intact
        Assert.AreEqual( "QA_PRIOR_SENTINEL_Identifier", Environment.GetEnvironmentVariable( IdentifierKey ) );
        Assert.AreEqual( "QA_PRIOR_SENTINEL_Password", Environment.GetEnvironmentVariable( PasswordKey ) );
        Assert.AreEqual( "QA_PRIOR_SENTINEL_UserDID", Environment.GetEnvironmentVariable( UserDidKey ) );
    }
}
