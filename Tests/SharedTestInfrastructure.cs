using Testcontainers.Redis;

namespace BridgeBeats.Tests;

/// <summary>
/// Manages a shared Redis container for all tests in the assembly.
/// The container is started once per test run and shared across all test classes.
/// Uses lazy initialization to ensure the container is ready before any test runs.
/// </summary>
[TestClass]
public static class SharedTestInfrastructure {
    private static RedisContainer? s_redisContainer;
    private static readonly Lock s_lock = new( );
    private static bool s_initialized;
    private static string? s_dockerError;

    /// <summary>
    /// Gets the Redis connection string for tests.
    /// </summary>
    public static string RedisConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// Indicates whether Docker is available and the Redis container started successfully.
    /// </summary>
    public static bool IsRedisAvailable { get; private set; }

    /// <summary>
    /// Ensures the Redis container is started. This is idempotent and thread-safe.
    /// </summary>
    private static void EnsureInitialized( ) {
        if (s_initialized) {
            return;
        }

        lock (s_lock) {
            if (s_initialized) {
                return;
            }

            try {
                s_redisContainer = new RedisBuilder( "redis:7-alpine" )
                    .Build( );

                // Start synchronously to ensure container is ready
                s_redisContainer.StartAsync( ).GetAwaiter( ).GetResult( );
                RedisConnectionString = s_redisContainer.GetConnectionString( );
                IsRedisAvailable = true;
            } catch (DotNet.Testcontainers.Builders.DockerUnavailableException ex) {
                IsRedisAvailable = false;
                RedisConnectionString = string.Empty;
                s_dockerError = ex.Message;
            }

            s_initialized = true;
        }
    }

    /// <summary>
    /// Assembly-level initialization that ensures the Redis test infrastructure is started.
    /// </summary>
    /// <param name="_">The test context provided by the test framework (unused).</param>
    [AssemblyInitialize]
    public static void AssemblyInitialize( TestContext _ ) {
        // Trigger initialization early during assembly setup
        EnsureInitialized( );
    }

    /// <summary>
    /// Assembly-level cleanup that stops and disposes the Redis test container.
    /// </summary>
    [AssemblyCleanup]
    public static async Task AssemblyCleanup( ) {
        if (s_redisContainer is not null) {
            await s_redisContainer.StopAsync( CancellationToken.None );
            await s_redisContainer.DisposeAsync( );
        }
    }

    /// <summary>
    /// Ensures Redis is available. Call this at the start of tests that require Redis.
    /// This will trigger container startup if not already done.
    /// </summary>
    public static void RequireRedis( ) {
        EnsureInitialized( );

        if (!IsRedisAvailable) {
            Assert.Inconclusive(
                $"Docker is not available. These tests require Docker Desktop to be running. " +
                $"Please start Docker Desktop and re-run the tests. Error: {s_dockerError}"
            );
        }
    }
}
