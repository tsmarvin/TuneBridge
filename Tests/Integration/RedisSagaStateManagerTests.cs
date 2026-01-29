using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Queue;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="RedisSagaStateManager"/> using the shared Redis container.
/// Requires Docker to be running on the host machine.
/// </summary>
[TestClass]
[TestCategory( "Integration" )]
[TestCategory( "Docker" )]
[DoNotParallelize] // Shares Redis saga:* keys with SagaPollingIntegrationTests
public class RedisSagaStateManagerTests {

    private static IConnectionMultiplexer? s_redis;

    private Mock<ILogger<RedisSagaStateManager>> _mockLogger = null!;
    private IOptions<QueueSettings> _settings = null!;
    private RedisSagaStateManager _sagaManager = null!;

    /// <summary>
    /// Initializes the Redis connection for all tests in the class.
    /// </summary>
    /// <param name="context">The test context provided by the test framework.</param>
    [ClassInitialize]
    [Obsolete]
    public static async Task ClassInitialize( TestContext context ) {
        SharedTestInfrastructure.RequireRedis( );
        s_redis = await ConnectionMultiplexer.ConnectAsync( SharedTestInfrastructure.RedisConnectionString );
    }

    /// <summary>
    /// Closes and disposes the Redis connection after all tests complete.
    /// </summary>
    [ClassCleanup]
    public static async Task ClassCleanup( ) {
        if (s_redis is not null) {
            await s_redis.CloseAsync( );
            s_redis.Dispose( );
        }
    }

    /// <summary>
    /// Clears saga-related Redis keys and initializes the saga manager before each test.
    /// </summary>
    [TestInitialize]
    public async Task TestInitialize( ) {
        // Clear only saga-related keys before each test
        IDatabase db = s_redis!.GetDatabase( );
        IServer server = s_redis.GetServer( s_redis.GetEndPoints( )[0] );
        await foreach (RedisKey key in server.KeysAsync( pattern: "saga:*" )) {
            _ = await db.KeyDeleteAsync( key );
        }

        _mockLogger = new Mock<ILogger<RedisSagaStateManager>>( );
        _settings = Options.Create( new QueueSettings { JobExpirationMinutes = 60 } );

        _sagaManager = new RedisSagaStateManager(
            s_redis,
            _mockLogger.Object,
            _settings
        );
    }

    /// <summary>
    /// Verifies that <see cref="RedisSagaStateManager.GetOrCreateAsync"/> creates a new saga
    /// with the correct initial state when the saga does not exist.
    /// </summary>
    [TestMethod]
    public async Task GetOrCreateAsync_CreatesNewSaga( ) {
        // Arrange
        string lookupKey = "isrc:USRC12345678";
        string sagaId = ISagaStateManager.GenerateSagaId( lookupKey );

        // Act
        LookupSagaState saga = await _sagaManager.GetOrCreateAsync(
            sagaId,
            lookupKey,
            LookupRequestType.IsrcLookup,
            "USRC12345678"
        );

        // Assert
        Assert.AreEqual( sagaId, saga.SagaId );
        Assert.AreEqual( lookupKey, saga.LookupKey );
        Assert.AreEqual( LookupRequestType.IsrcLookup, saga.LookupType );
        Assert.AreEqual( "USRC12345678", saga.LookupValue );
        Assert.IsEmpty( saga.ProviderStates );
        Assert.IsNull( saga.PartialResultUri );
        Assert.IsNull( saga.FinalResultUri );
        Assert.IsFalse( saga.IsComplete );
    }

    /// <summary>
    /// Verifies that <see cref="RedisSagaStateManager.GetOrCreateAsync"/> returns the existing saga
    /// when called with a saga ID that already exists.
    /// </summary>
    [TestMethod]
    public async Task GetOrCreateAsync_ReturnsExistingSaga_WhenAlreadyExists( ) {
        // Arrange
        string lookupKey = "isrc:USRC12345678";
        string sagaId = ISagaStateManager.GenerateSagaId( lookupKey );

        LookupSagaState original = await _sagaManager.GetOrCreateAsync(
            sagaId,
            lookupKey,
            LookupRequestType.IsrcLookup,
            "USRC12345678"
        );

        // Act - try to create again with different values
        LookupSagaState second = await _sagaManager.GetOrCreateAsync(
            sagaId,
            "different-key",
            LookupRequestType.UpcLookup,
            "DIFFERENT"
        );

        // Assert - should return original, not new values
        Assert.AreEqual( original.SagaId, second.SagaId );
        Assert.AreEqual( original.LookupKey, second.LookupKey );
        Assert.AreEqual( original.LookupType, second.LookupType );
    }

    /// <summary>
    /// Verifies that <see cref="RedisSagaStateManager.GetAsync"/> returns null when the saga does not exist.
    /// </summary>
    [TestMethod]
    public async Task GetAsync_ReturnsNull_WhenSagaDoesNotExist( ) {
        // Act
        LookupSagaState? saga = await _sagaManager.GetAsync( "nonexistent-saga-id" );

        // Assert
        Assert.IsNull( saga );
    }

    /// <summary>
    /// Verifies that <see cref="RedisSagaStateManager.UpdateProviderStateAsync"/> stores the provider
    /// lookup state in Redis and can be retrieved.
    /// </summary>
    [TestMethod]
    public async Task UpdateProviderStateAsync_StoresProviderState( ) {
        // Arrange
        string lookupKey = "isrc:USRC12345678";
        string sagaId = ISagaStateManager.GenerateSagaId( lookupKey );

        _ = await _sagaManager.GetOrCreateAsync(
            sagaId,
            lookupKey,
            LookupRequestType.IsrcLookup,
            "USRC12345678"
        );

        ProviderLookupState providerState = new(
            Provider: SupportedProviders.Spotify,
            IsComplete: true,
            IsSuccess: true,
            ResultJson: """{"trackId": "abc123"}""",
            CompletedAt: DateTimeOffset.UtcNow,
            ErrorMessage: null
        );

        // Act
        await _sagaManager.UpdateProviderStateAsync( sagaId, providerState );

        // Assert
        LookupSagaState? saga = await _sagaManager.GetAsync( sagaId );
        Assert.IsNotNull( saga );
        Assert.HasCount( 1, saga.ProviderStates );
        Assert.IsTrue( saga.ProviderStates.ContainsKey( SupportedProviders.Spotify ) );

        ProviderLookupState retrieved = saga.ProviderStates[SupportedProviders.Spotify];
        Assert.IsTrue( retrieved.IsComplete );
        Assert.IsTrue( retrieved.IsSuccess );
        Assert.AreEqual( """{"trackId": "abc123"}""", retrieved.ResultJson );
    }

    /// <summary>
    /// Verifies that <see cref="RedisSagaStateManager.UpdateProviderStateAsync"/> can store
    /// multiple provider states for the same saga.
    /// </summary>
    [TestMethod]
    public async Task UpdateProviderStateAsync_UpdatesMultipleProviders( ) {
        // Arrange
        string lookupKey = "isrc:USRC12345678";
        string sagaId = ISagaStateManager.GenerateSagaId( lookupKey );

        _ = await _sagaManager.GetOrCreateAsync(
            sagaId,
            lookupKey,
            LookupRequestType.IsrcLookup,
            "USRC12345678"
        );

        ProviderLookupState spotifyState = new(
            Provider: SupportedProviders.Spotify,
            IsComplete: true,
            IsSuccess: true,
            ResultJson: """{"spotifyId": "123"}""",
            CompletedAt: DateTimeOffset.UtcNow,
            ErrorMessage: null
        );

        ProviderLookupState appleState = new(
            Provider: SupportedProviders.AppleMusic,
            IsComplete: true,
            IsSuccess: false,
            ResultJson: null,
            CompletedAt: DateTimeOffset.UtcNow,
            ErrorMessage: "Rate limited"
        );

        // Act
        await _sagaManager.UpdateProviderStateAsync( sagaId, spotifyState );
        await _sagaManager.UpdateProviderStateAsync( sagaId, appleState );

        // Assert
        LookupSagaState? saga = await _sagaManager.GetAsync( sagaId );
        Assert.IsNotNull( saga );
        Assert.HasCount( 2, saga.ProviderStates );

        Assert.IsTrue( saga.ProviderStates[SupportedProviders.Spotify].IsSuccess );
        Assert.IsFalse( saga.ProviderStates[SupportedProviders.AppleMusic].IsSuccess );
        Assert.AreEqual( "Rate limited", saga.ProviderStates[SupportedProviders.AppleMusic].ErrorMessage );
    }

    /// <summary>
    /// Verifies that <see cref="RedisSagaStateManager.SetPartialResultUriAsync"/> stores the partial
    /// result URI without affecting the final result URI.
    /// </summary>
    [TestMethod]
    public async Task SetPartialResultUriAsync_StoresUri( ) {
        // Arrange
        string lookupKey = "isrc:USRC12345678";
        string sagaId = ISagaStateManager.GenerateSagaId( lookupKey );
        string partialUri = "at://did:plc:test/link.bridgebeats.lookup/track:USRC12345678";

        _ = await _sagaManager.GetOrCreateAsync(
            sagaId,
            lookupKey,
            LookupRequestType.IsrcLookup,
            "USRC12345678"
        );

        // Act
        await _sagaManager.SetPartialResultUriAsync( sagaId, partialUri );

        // Assert
        LookupSagaState? saga = await _sagaManager.GetAsync( sagaId );
        Assert.IsNotNull( saga );
        Assert.AreEqual( partialUri, saga.PartialResultUri );
        Assert.IsNull( saga.FinalResultUri );
    }

    /// <summary>
    /// Verifies that <see cref="RedisSagaStateManager.SetFinalResultUriAsync"/> stores the final
    /// result URI in the saga state.
    /// </summary>
    [TestMethod]
    public async Task SetFinalResultUriAsync_StoresUri( ) {
        // Arrange
        string lookupKey = "isrc:USRC12345678";
        string sagaId = ISagaStateManager.GenerateSagaId( lookupKey );
        string finalUri = "at://did:plc:test/link.bridgebeats.lookup/track:USRC12345678";

        _ = await _sagaManager.GetOrCreateAsync(
            sagaId,
            lookupKey,
            LookupRequestType.IsrcLookup,
            "USRC12345678"
        );

        // Act
        await _sagaManager.SetFinalResultUriAsync( sagaId, finalUri );

        // Assert
        LookupSagaState? saga = await _sagaManager.GetAsync( sagaId );
        Assert.IsNotNull( saga );
        Assert.AreEqual( finalUri, saga.FinalResultUri );
    }

    /// <summary>
    /// Verifies that <see cref="RedisSagaStateManager.DeleteAsync"/> removes both the saga state
    /// and all associated provider states from Redis.
    /// </summary>
    [TestMethod]
    public async Task DeleteAsync_RemovesSagaAndProviderStates( ) {
        // Arrange
        string lookupKey = "isrc:USRC12345678";
        string sagaId = ISagaStateManager.GenerateSagaId( lookupKey );

        _ = await _sagaManager.GetOrCreateAsync(
            sagaId,
            lookupKey,
            LookupRequestType.IsrcLookup,
            "USRC12345678"
        );

        await _sagaManager.UpdateProviderStateAsync( sagaId, new ProviderLookupState(
            SupportedProviders.Spotify, true, true, "{}", DateTimeOffset.UtcNow, null
        ) );

        // Verify exists
        LookupSagaState? beforeDelete = await _sagaManager.GetAsync( sagaId );
        Assert.IsNotNull( beforeDelete );

        // Act
        bool deleted = await _sagaManager.DeleteAsync( sagaId );

        // Assert
        Assert.IsTrue( deleted );

        LookupSagaState? afterDelete = await _sagaManager.GetAsync( sagaId );
        Assert.IsNull( afterDelete );
    }

    /// <summary>
    /// Verifies that <see cref="RedisSagaStateManager.DeleteAsync"/> returns false when attempting
    /// to delete a saga that does not exist.
    /// </summary>
    [TestMethod]
    public async Task DeleteAsync_ReturnsFalse_WhenSagaDoesNotExist( ) {
        // Act
        bool deleted = await _sagaManager.DeleteAsync( "nonexistent-saga" );

        // Assert
        Assert.IsFalse( deleted );
    }

    /// <summary>
    /// Verifies that <see cref="ISagaStateManager.GenerateSagaId"/> creates deterministic,
    /// case-insensitive IDs from lookup keys.
    /// </summary>
    [TestMethod]
    public void GenerateSagaId_CreatesDeterministicId( ) {
        // Arrange
        string lookupKey1 = "isrc:USRC12345678";
        string lookupKey2 = "isrc:usrc12345678"; // Different case
        string lookupKey3 = "isrc:DIFFERENTISRC";

        // Act
        string id1a = ISagaStateManager.GenerateSagaId( lookupKey1 );
        string id1b = ISagaStateManager.GenerateSagaId( lookupKey1 );
        string id2 = ISagaStateManager.GenerateSagaId( lookupKey2 );
        string id3 = ISagaStateManager.GenerateSagaId( lookupKey3 );

        // Assert - same key produces same ID
        Assert.AreEqual( id1a, id1b );

        // Case insensitive normalization
        Assert.AreEqual( id1a, id2 );

        // Different key produces different ID
        Assert.AreNotEqual( id1a, id3 );

        // ID is 32 hex chars (16 bytes of SHA256)
        Assert.AreEqual( 32, id1a.Length );
        Assert.IsTrue( id1a.All( c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') ) );
    }

    /// <summary>
    /// Verifies that <see cref="LookupSagaState.IsComplete"/> returns true only when all providers
    /// have completed their lookups.
    /// </summary>
    [TestMethod]
    public async Task Saga_IsComplete_WhenAllProvidersComplete( ) {
        // Arrange
        string lookupKey = "isrc:USRC12345678";
        string sagaId = ISagaStateManager.GenerateSagaId( lookupKey );

        _ = await _sagaManager.GetOrCreateAsync(
            sagaId,
            lookupKey,
            LookupRequestType.IsrcLookup,
            "USRC12345678"
        );

        // Add incomplete provider
        await _sagaManager.UpdateProviderStateAsync( sagaId, new ProviderLookupState(
            SupportedProviders.Spotify, false, false, null, null, null
        ) );

        LookupSagaState? incomplete = await _sagaManager.GetAsync( sagaId );
        Assert.IsNotNull( incomplete );
        Assert.IsFalse( incomplete.IsComplete );

        // Act - mark provider as complete
        await _sagaManager.UpdateProviderStateAsync( sagaId, new ProviderLookupState(
            SupportedProviders.Spotify, true, true, "{}", DateTimeOffset.UtcNow, null
        ) );

        // Assert
        LookupSagaState? complete = await _sagaManager.GetAsync( sagaId );
        Assert.IsNotNull( complete );
        Assert.IsTrue( complete.IsComplete );
    }
}
