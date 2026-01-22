using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Infrastructure.Queue;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Integration;

/// <summary>
/// Integration tests for saga polling functionality using the shared Redis container.
/// These tests verify that the polling fallback mechanism correctly discovers and
/// processes sagas that may have been missed by Pub/Sub.
/// Requires Docker to be running on the host machine.
/// </summary>
[TestClass]
[TestCategory( "Integration" )]
[TestCategory( "Docker" )]
[DoNotParallelize] // Shares Redis saga:* keys with RedisSagaStateManagerTests
public class SagaPollingIntegrationTests {

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
    /// Verifies that <see cref="ISagaStateManager.GetCompletedButUnfinalizedAsync"/> returns sagas
    /// that are complete but have not had their final result URI set.
    /// </summary>
    [TestMethod]
    public async Task GetCompletedButUnfinalizedAsync_ReturnsCompleteSagasWithNoFinalUri( ) {
        // Arrange - Create a saga that is complete but not finalized
        string lookupKey = "isrc:USRC12345678";
        string sagaId = ISagaStateManager.GenerateSagaId( lookupKey );

        _ = await _sagaManager.GetOrCreateAsync(
            sagaId,
            lookupKey,
            LookupRequestType.IsrcLookup,
            "USRC12345678"
        );

        // Mark provider as complete
        await _sagaManager.UpdateProviderStateAsync( sagaId, new ProviderLookupState(
            Provider: SupportedProviders.Spotify,
            IsComplete: true,
            IsSuccess: true,
            ResultJson: """{"trackId": "abc123"}""",
            CompletedAt: DateTimeOffset.UtcNow,
            ErrorMessage: null
        ) );

        // Backdate the saga by modifying the createdAt directly in Redis
        IDatabase db = s_redis!.GetDatabase( );
        _ = await db.HashSetAsync(
            $"saga:{sagaId}",
            "createdAt",
            DateTimeOffset.UtcNow.AddSeconds( -30 ).ToString( "O" )
        );

        // Act - Query for unfinalized sagas with minimum age of 0 seconds
        IReadOnlyList<LookupSagaState> results = await _sagaManager.GetCompletedButUnfinalizedAsync(
            minimumAge: TimeSpan.Zero,
            limit: 100
        );

        // Assert
        Assert.HasCount( 1, results );
        Assert.AreEqual( sagaId, results[0].SagaId );
        Assert.IsTrue( results[0].IsComplete );
        Assert.IsNull( results[0].FinalResultUri );
    }

    /// <summary>
    /// Verifies that <see cref="ISagaStateManager.GetCompletedButUnfinalizedAsync"/> excludes sagas
    /// that have already had their final result URI set.
    /// </summary>
    [TestMethod]
    public async Task GetCompletedButUnfinalizedAsync_ExcludesSagasWithFinalUri( ) {
        // Arrange - Create a finalized saga
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

        // Set final result URI - this should exclude it from polling
        await _sagaManager.SetFinalResultUriAsync( sagaId, "at://did:plc:test/link/abc" );

        // Act
        IReadOnlyList<LookupSagaState> results = await _sagaManager.GetCompletedButUnfinalizedAsync(
            minimumAge: TimeSpan.Zero,
            limit: 100
        );

        // Assert - Should be empty since saga was finalized
        Assert.IsEmpty( results );
    }

    /// <summary>
    /// Verifies that <see cref="ISagaStateManager.GetCompletedButUnfinalizedAsync"/> excludes sagas
    /// that have not yet completed all provider lookups.
    /// </summary>
    [TestMethod]
    public async Task GetCompletedButUnfinalizedAsync_ExcludesIncompleteSagas( ) {
        // Arrange - Create an incomplete saga
        string lookupKey = "isrc:USRC12345678";
        string sagaId = ISagaStateManager.GenerateSagaId( lookupKey );

        _ = await _sagaManager.GetOrCreateAsync(
            sagaId,
            lookupKey,
            LookupRequestType.IsrcLookup,
            "USRC12345678"
        );

        // Add provider but don't complete it
        await _sagaManager.UpdateProviderStateAsync( sagaId, new ProviderLookupState(
            SupportedProviders.Spotify, false, false, null, null, null
        ) );

        // Act
        IReadOnlyList<LookupSagaState> results = await _sagaManager.GetCompletedButUnfinalizedAsync(
            minimumAge: TimeSpan.Zero,
            limit: 100
        );

        // Assert - Should be empty since saga is incomplete
        Assert.IsEmpty( results );
    }

    /// <summary>
    /// Verifies that <see cref="ISagaStateManager.GetCompletedButUnfinalizedAsync"/> respects the
    /// minimum age parameter to avoid processing very recently created sagas.
    /// </summary>
    [TestMethod]
    public async Task GetCompletedButUnfinalizedAsync_RespectsMinimumAge( ) {
        // Arrange - Create a saga that was just created
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

        // Act - Query with 15-second minimum age (saga is too young)
        IReadOnlyList<LookupSagaState> results = await _sagaManager.GetCompletedButUnfinalizedAsync(
            minimumAge: TimeSpan.FromSeconds( 15 ),
            limit: 100
        );

        // Assert - Should be empty since saga is too young
        Assert.IsEmpty( results );
    }

    /// <summary>
    /// Verifies that <see cref="ISagaStateManager.GetCompletedButUnfinalizedAsync"/> respects the
    /// limit parameter to control the number of results returned.
    /// </summary>
    [TestMethod]
    public async Task GetCompletedButUnfinalizedAsync_RespectsLimit( ) {
        // Arrange - Create multiple sagas
        for (int i = 0; i < 5; i++) {
            string lookupKey = $"isrc:USRC{i:D10}";
            string sagaId = ISagaStateManager.GenerateSagaId( lookupKey );

            _ = await _sagaManager.GetOrCreateAsync(
                sagaId,
                lookupKey,
                LookupRequestType.IsrcLookup,
                $"USRC{i:D10}"
            );

            await _sagaManager.UpdateProviderStateAsync( sagaId, new ProviderLookupState(
                SupportedProviders.Spotify, true, true, "{}", DateTimeOffset.UtcNow, null
            ) );

            // Backdate the saga
            IDatabase db = s_redis!.GetDatabase( );
            _ = await db.HashSetAsync(
                $"saga:{sagaId}",
                "createdAt",
                DateTimeOffset.UtcNow.AddSeconds( -30 ).ToString( "O" )
            );
        }

        // Act - Query with limit of 3
        IReadOnlyList<LookupSagaState> results = await _sagaManager.GetCompletedButUnfinalizedAsync(
            minimumAge: TimeSpan.Zero,
            limit: 3
        );

        // Assert - Should only return 3 despite 5 being available
        Assert.HasCount( 3, results );
    }

    /// <summary>
    /// Verifies that <see cref="ISagaStateManager.GetCompletedButUnfinalizedAsync"/> cleans up
    /// orphaned saga IDs from the pending index when the underlying saga data is missing.
    /// </summary>
    [TestMethod]
    public async Task GetCompletedButUnfinalizedAsync_CleansUpExpiredSagasFromIndex( ) {
        // Arrange - Add a saga ID to the pending index but don't create the saga itself
        IDatabase db = s_redis!.GetDatabase( );
        _ = await db.SetAddAsync( "saga:pending", "expired-saga-id" );

        // Verify it's in the index
        RedisValue[] pendingBefore = await db.SetMembersAsync( "saga:pending" );
        Assert.IsTrue( pendingBefore.Any( v => v.ToString( ) == "expired-saga-id" ) );

        // Act - Query should clean up the orphaned index entry
        IReadOnlyList<LookupSagaState> results = await _sagaManager.GetCompletedButUnfinalizedAsync(
            minimumAge: TimeSpan.Zero,
            limit: 100
        );

        // Assert - Orphaned entry should be removed from index
        RedisValue[] pendingAfter = await db.SetMembersAsync( "saga:pending" );
        Assert.IsFalse( pendingAfter.Any( v => v.ToString( ) == "expired-saga-id" ) );
    }

    /// <summary>
    /// Verifies that <see cref="ISagaStateManager.AddToPendingIndexAsync"/> adds the saga ID
    /// to the pending index set in Redis.
    /// </summary>
    [TestMethod]
    public async Task AddToPendingIndexAsync_AddsSagaToSet( ) {
        // Arrange
        string sagaId = "test-saga-id";

        // Act
        await _sagaManager.AddToPendingIndexAsync( sagaId );

        // Assert
        IDatabase db = s_redis!.GetDatabase( );
        RedisValue[] members = await db.SetMembersAsync( "saga:pending" );
        Assert.IsTrue( members.Any( m => m.ToString( ) == sagaId ) );
    }

    /// <summary>
    /// Verifies that <see cref="ISagaStateManager.RemoveFromPendingIndexAsync"/> removes the saga ID
    /// from the pending index set in Redis.
    /// </summary>
    [TestMethod]
    public async Task RemoveFromPendingIndexAsync_RemovesSagaFromSet( ) {
        // Arrange
        string sagaId = "test-saga-id";
        IDatabase db = s_redis!.GetDatabase( );
        _ = await db.SetAddAsync( "saga:pending", sagaId );

        // Act
        await _sagaManager.RemoveFromPendingIndexAsync( sagaId );

        // Assert
        RedisValue[] members = await db.SetMembersAsync( "saga:pending" );
        Assert.IsFalse( members.Any( m => m.ToString( ) == sagaId ) );
    }

    /// <summary>
    /// Verifies that <see cref="ISagaStateManager.GetOrCreateAsync"/> automatically adds newly
    /// created sagas to the pending index for polling discovery.
    /// </summary>
    [TestMethod]
    public async Task GetOrCreateAsync_AutomaticallyAddsToPendingIndex( ) {
        // Arrange
        string lookupKey = "isrc:USRC12345678";
        string sagaId = ISagaStateManager.GenerateSagaId( lookupKey );

        // Act
        _ = await _sagaManager.GetOrCreateAsync(
            sagaId,
            lookupKey,
            LookupRequestType.IsrcLookup,
            "USRC12345678"
        );

        // Assert - Should be in pending index
        IDatabase db = s_redis!.GetDatabase( );
        RedisValue[] members = await db.SetMembersAsync( "saga:pending" );
        Assert.IsTrue( members.Any( m => m.ToString( ) == sagaId ) );
    }

    /// <summary>
    /// Verifies that <see cref="ISagaStateManager.SetFinalResultUriAsync"/> removes the saga
    /// from the pending index since it no longer needs polling.
    /// </summary>
    [TestMethod]
    public async Task SetFinalResultUriAsync_RemovesFromPendingIndex( ) {
        // Arrange
        string lookupKey = "isrc:USRC12345678";
        string sagaId = ISagaStateManager.GenerateSagaId( lookupKey );

        _ = await _sagaManager.GetOrCreateAsync(
            sagaId,
            lookupKey,
            LookupRequestType.IsrcLookup,
            "USRC12345678"
        );

        // Verify it's in the index
        IDatabase db = s_redis!.GetDatabase( );
        RedisValue[] membersBefore = await db.SetMembersAsync( "saga:pending" );
        Assert.IsTrue( membersBefore.Any( m => m.ToString( ) == sagaId ) );

        // Act
        await _sagaManager.SetFinalResultUriAsync( sagaId, "at://did:plc:test/link/abc" );

        // Assert - Should be removed from pending index
        RedisValue[] membersAfter = await db.SetMembersAsync( "saga:pending" );
        Assert.IsFalse( membersAfter.Any( m => m.ToString( ) == sagaId ) );
    }

    /// <summary>
    /// Verifies that <see cref="ISagaStateManager.DeleteAsync"/> removes the saga from the pending
    /// index when deleting the saga state.
    /// </summary>
    [TestMethod]
    public async Task DeleteAsync_RemovesFromPendingIndex( ) {
        // Arrange
        string lookupKey = "isrc:USRC12345678";
        string sagaId = ISagaStateManager.GenerateSagaId( lookupKey );

        _ = await _sagaManager.GetOrCreateAsync(
            sagaId,
            lookupKey,
            LookupRequestType.IsrcLookup,
            "USRC12345678"
        );

        // Verify it's in the index
        IDatabase db = s_redis!.GetDatabase( );
        RedisValue[] membersBefore = await db.SetMembersAsync( "saga:pending" );
        Assert.IsTrue( membersBefore.Any( m => m.ToString( ) == sagaId ) );

        // Act
        _ = await _sagaManager.DeleteAsync( sagaId );

        // Assert - Should be removed from pending index
        RedisValue[] membersAfter = await db.SetMembersAsync( "saga:pending" );
        Assert.IsFalse( membersAfter.Any( m => m.ToString( ) == sagaId ) );
    }
}
