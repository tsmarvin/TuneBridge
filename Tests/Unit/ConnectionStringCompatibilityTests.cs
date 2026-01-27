using Microsoft.Extensions.Configuration;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests to verify that IdentityConnectionString and LinkCacheConnectionString can use the same value format.
/// </summary>
[TestClass]
public class ConnectionStringCompatibilityTests {
    /// <summary>
    /// Verifies that both IdentityConnectionString and LinkCacheConnectionString can use the same
    /// connection string format with a Data Source path.
    /// </summary>
    [TestMethod]
    public void BothConnectionStrings_CanUseSameValueFormat_WithDataSource( ) {
        // Arrange - Use the same connection string format for both databases
        string sharedConnectionString = "Data Source=shared.db";

        Dictionary<string, string?> configData = new( ) {
            ["BridgeBeats:IdentityConnectionString"] = sharedConnectionString,
            ["BridgeBeats:LinkCacheConnectionString"] = sharedConnectionString,
        };

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Act
        AppSettings settings = new();
        configuration.GetRequiredSection( "BridgeBeats" ).Bind( settings );

        // Assert
        Assert.AreEqual( sharedConnectionString, settings.IdentityConnectionString );
        Assert.AreEqual( sharedConnectionString, settings.LinkCacheConnectionString );
        Assert.AreEqual( settings.IdentityConnectionString, settings.LinkCacheConnectionString );
    }

    /// <summary>
    /// Verifies that both IdentityConnectionString and LinkCacheConnectionString can use the same
    /// in-memory connection string format with shared cache mode.
    /// </summary>
    [TestMethod]
    public void BothConnectionStrings_CanUseSameValueFormat_WithMemoryMode( ) {
        // Arrange - Use the same in-memory connection string format for both databases
        string sharedConnectionString = "Data Source=TestDb;Mode=Memory;Cache=Shared";

        Dictionary<string, string?> configData = new( ) {
            ["BridgeBeats:IdentityConnectionString"] = sharedConnectionString,
            ["BridgeBeats:LinkCacheConnectionString"] = sharedConnectionString,
        };

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Act
        AppSettings settings = new();
        configuration.GetRequiredSection( "BridgeBeats" ).Bind( settings );

        // Assert
        Assert.AreEqual( sharedConnectionString, settings.IdentityConnectionString );
        Assert.AreEqual( sharedConnectionString, settings.LinkCacheConnectionString );
        Assert.AreEqual( settings.IdentityConnectionString, settings.LinkCacheConnectionString );
    }

    /// <summary>
    /// Verifies that IdentityConnectionString and LinkCacheConnectionString can be configured
    /// with different values to use separate database files.
    /// </summary>
    [TestMethod]
    public void BothConnectionStrings_CanUseDifferentValues( ) {
        // Arrange - Use different connection strings for each database
        string identityConnectionString = "Data Source=identity.db";
        string cacheConnectionString = "Data Source=cache.db";

        Dictionary<string, string?> configData = new( ) {
            ["BridgeBeats:IdentityConnectionString"] = identityConnectionString,
            ["BridgeBeats:LinkCacheConnectionString"] = cacheConnectionString,
        };

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Act
        AppSettings settings = new();
        configuration.GetRequiredSection( "BridgeBeats" ).Bind( settings );

        // Assert
        Assert.AreEqual( identityConnectionString, settings.IdentityConnectionString );
        Assert.AreEqual( cacheConnectionString, settings.LinkCacheConnectionString );
        Assert.AreNotEqual( settings.IdentityConnectionString, settings.LinkCacheConnectionString );
    }

    /// <summary>
    /// Verifies that both connection string defaults use a consistent "Data Source=" format.
    /// </summary>
    [TestMethod]
    public void BothConnectionStrings_HaveConsistentDefaultFormat( ) {
        // Arrange & Act
        AppSettings settings = new();

        // Assert - Both defaults use "Data Source=" format
        Assert.StartsWith( "Data Source=", settings.IdentityConnectionString );
        Assert.StartsWith( "Data Source=", settings.LinkCacheConnectionString );
    }
}
