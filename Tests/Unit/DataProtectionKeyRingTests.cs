using BridgeBeats.Core.Infrastructure.Identity;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests <see cref="DataProtectionKeyRing"/>, the single-version lookup-protector key ring. Verifies the
/// current key id is <c>"v1"</c>, that the key set contains exactly that one id, and that the indexer echoes
/// any requested key id unchanged (no key material is held here, only the active version name).
/// </summary>
[TestClass]
public class DataProtectionKeyRingTests {

    /// <summary>The key ring under test, recreated before each test.</summary>
    private DataProtectionKeyRing _keyRing = null!;

    /// <summary>
    /// Creates a fresh <see cref="DataProtectionKeyRing"/> before each test.
    /// </summary>
    [TestInitialize]
    public void Setup( ) {
        _keyRing = new DataProtectionKeyRing( );
    }

    /// <summary>
    /// Verifies <see cref="DataProtectionKeyRing.CurrentKeyId"/> is the single active version <c>"v1"</c>.
    /// </summary>
    [TestMethod]
    public void CurrentKeyId_ReturnsV1( ) {
        // Act
        string keyId = _keyRing.CurrentKeyId;

        // Assert
        Assert.AreEqual( "v1", keyId );
    }

    /// <summary>
    /// Verifies the set returned by <see cref="DataProtectionKeyRing.GetAllKeyIds"/> contains the current key id.
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
    /// Verifies <see cref="DataProtectionKeyRing.GetAllKeyIds"/> returns exactly one key id (no rotation versions).
    /// </summary>
    [TestMethod]
    public void GetAllKeyIds_ReturnsSingleKey( ) {
        // Act
        List<string> allKeys = [.. _keyRing.GetAllKeyIds( )];

        // Assert
        Assert.HasCount( 1, allKeys );
    }

    /// <summary>
    /// Verifies the indexer returns the current key id <c>"v1"</c> unchanged when queried with <c>"v1"</c>.
    /// </summary>
    [TestMethod]
    public void Indexer_ReturnsPassedKeyId( ) {
        // Act
        string result = _keyRing["v1"];

        // Assert
        Assert.AreEqual( "v1", result );
    }

    /// <summary>
    /// Verifies the indexer echoes back any requested key id unchanged, even one outside the known set (<c>"v2"</c>).
    /// </summary>
    [TestMethod]
    public void Indexer_ReturnsArbitraryKeyId( ) {
        // Act
        string result = _keyRing["v2"];

        // Assert
        Assert.AreEqual( "v2", result );
    }
}
