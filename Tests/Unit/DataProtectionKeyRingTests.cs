using BridgeBeats.Core.Infrastructure.Identity;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="DataProtectionKeyRing"/>.
/// </summary>
[TestClass]
public class DataProtectionKeyRingTests {

    private DataProtectionKeyRing _keyRing = null!;

    /// <summary>
    /// Initializes test resources before each test.
    /// </summary>
    [TestInitialize]
    public void Setup( ) {
        _keyRing = new DataProtectionKeyRing( );
    }

    /// <summary>
    /// Verifies that CurrentKeyId returns "v1".
    /// </summary>
    [TestMethod]
    public void CurrentKeyId_ReturnsV1( ) {
        // Act
        string keyId = _keyRing.CurrentKeyId;

        // Assert
        Assert.AreEqual( "v1", keyId );
    }

    /// <summary>
    /// Verifies that GetAllKeyIds contains the current key ID.
    /// </summary>
    [TestMethod]
    public void GetAllKeyIds_ContainsCurrentKeyId( ) {
        // Act
        IEnumerable<string> allKeys = _keyRing.GetAllKeyIds( );

        // Assert
        List<string> keyList = [.. allKeys];
        CollectionAssert.Contains( keyList, _keyRing.CurrentKeyId );
    }

    /// <summary>
    /// Verifies that GetAllKeyIds returns exactly one key (no stale keys in initial state).
    /// </summary>
    [TestMethod]
    public void GetAllKeyIds_ReturnsSingleKey( ) {
        // Act
        List<string> allKeys = [.. _keyRing.GetAllKeyIds( )];

        // Assert
        Assert.HasCount( 1, allKeys );
    }

    /// <summary>
    /// Verifies that the indexer returns the same key ID passed in.
    /// </summary>
    [TestMethod]
    public void Indexer_ReturnsPassedKeyId( ) {
        // Act
        string result = _keyRing["v1"];

        // Assert
        Assert.AreEqual( "v1", result );
    }

    /// <summary>
    /// Verifies that the indexer works with arbitrary key IDs (for future key rotation).
    /// </summary>
    [TestMethod]
    public void Indexer_ReturnsArbitraryKeyId( ) {
        // Act
        string result = _keyRing["v2"];

        // Assert
        Assert.AreEqual( "v2", result );
    }
}
