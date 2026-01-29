using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BridgeBeats.Core.Infrastructure.Queue;

/// <summary>
/// Redis-based implementation of <see cref="ISagaStateManager"/> that stores saga state
/// with separate keys for provider states to enable efficient partial updates.
/// </summary>
/// <remarks>
/// <para>
/// Key patterns:
/// <list type="bullet">
///   <item><c>saga:{sagaId}</c> - Hash containing core saga state</item>
///   <item><c>saga:{sagaId}:provider:{provider}</c> - Hash containing provider-specific state</item>
/// </list>
/// </para>
/// <para>
/// The saga ID is deterministically derived from the lookup key using SHA256 hashing,
/// ensuring identical lookups map to the same saga (idempotent saga creation).
/// </para>
/// <para>
/// All saga keys have a TTL that is automatically extended on each update to prevent
/// expiration during active processing.
/// </para>
/// </remarks>
public sealed class RedisSagaStateManager : ISagaStateManager {

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisSagaStateManager> _logger;
    private readonly QueueSettings _settings;

    private const string SagaPrefix = "saga:";
    private const string ProviderSuffix = ":provider:";
    private const string PendingSagaSetKey = "saga:pending";

    // Hash field names for saga state
    private const string FieldLookupKey = "lookupKey";
    private const string FieldLookupType = "lookupType";
    private const string FieldLookupValue = "lookupValue";
    private const string FieldCreatedAt = "createdAt";
    private const string FieldPartialResultUri = "partialResultUri";
    private const string FieldFinalResultUri = "finalResultUri";
    private const string FieldIsPartial = "isPartial";
    private const string FieldInitialProvider = "initialProvider";
    private const string FieldRateLimitInfo = "rateLimitInfo";

    // Hash field names for provider state
    private const string FieldIsComplete = "isComplete";
    private const string FieldIsSuccess = "isSuccess";
    private const string FieldResultJson = "resultJson";
    private const string FieldCompletedAt = "completedAt";
    private const string FieldErrorMessage = "errorMessage";

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisSagaStateManager"/> class.
    /// </summary>
    /// <param name="redis">The Redis connection multiplexer.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="settings">Queue configuration settings.</param>
    public RedisSagaStateManager(
        IConnectionMultiplexer redis,
        ILogger<RedisSagaStateManager> logger,
        IOptions<QueueSettings> settings
    ) {
        _redis = redis ?? throw new ArgumentNullException( nameof( redis ) );
        _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
        _settings = settings?.Value ?? throw new ArgumentNullException( nameof( settings ) );
    }

    /// <inheritdoc/>
    public async Task<LookupSagaState> GetOrCreateAsync(
        string sagaId,
        string lookupKey,
        LookupRequestType lookupType,
        string lookupValue,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );
        ArgumentException.ThrowIfNullOrWhiteSpace( lookupKey );
        ArgumentException.ThrowIfNullOrWhiteSpace( lookupValue );

        string key = GetSagaKey( sagaId );
        IDatabase db = _redis.GetDatabase( );
        TimeSpan ttl = TimeSpan.FromMinutes( _settings.JobExpirationMinutes );

        // Check if saga already exists
        bool exists = await db.KeyExistsAsync( key );

        if (exists) {
            // Extend TTL and return existing state
            _ = await db.KeyExpireAsync( key, ttl );
            LookupSagaState? existingState = await GetAsync( sagaId, cancellationToken );

            if (existingState is not null) {
                _logger.LogDebug( "Resumed existing saga {SagaId}", sagaId );
                return existingState;
            }
        }

        // Create new saga
        HashEntry[] sagaHash = [
            new HashEntry( FieldLookupKey, lookupKey ),
            new HashEntry( FieldLookupType, lookupType.ToString() ),
            new HashEntry( FieldLookupValue, lookupValue ),
            new HashEntry( FieldCreatedAt, DateTimeOffset.UtcNow.ToString( "O" ) ),
            new HashEntry( FieldPartialResultUri, RedisValue.EmptyString ),
            new HashEntry( FieldFinalResultUri, RedisValue.EmptyString )
        ];

        await db.HashSetAsync( key, sagaHash );
        _ = await db.KeyExpireAsync( key, ttl );

        // Add to pending index for efficient polling
        await AddToPendingIndexAsync( sagaId, cancellationToken );

        _logger.LogInformation(
            "Created new saga {SagaId} for lookup {LookupType}:{LookupValue}",
            sagaId,
            lookupType,
            lookupValue
        );

        return new LookupSagaState {
            SagaId = sagaId,
            LookupKey = lookupKey,
            LookupType = lookupType,
            LookupValue = lookupValue,
            CreatedAt = DateTimeOffset.UtcNow,
            ProviderStates = [],
            PartialResultUri = null,
            FinalResultUri = null
        };
    }

    /// <inheritdoc/>
    public async Task<LookupSagaState?> GetAsync( string sagaId, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );

        string key = GetSagaKey( sagaId );
        IDatabase db = _redis.GetDatabase( );

        HashEntry[] sagaHash = await db.HashGetAllAsync( key );

        if (sagaHash.Length == 0) {
            return null;
        }

        Dictionary<string, string> fields = sagaHash.ToDictionary(
            h => h.Name.ToString( ),
            h => h.Value.ToString( )
        );

        // Parse core saga state
        if (!fields.TryGetValue( FieldLookupKey, out string? lookupKey ) ||
            !fields.TryGetValue( FieldLookupType, out string? lookupTypeStr ) ||
            !fields.TryGetValue( FieldLookupValue, out string? lookupValue )) {
            _logger.LogWarning( "Saga {SagaId} has incomplete core state", sagaId );
            return null;
        }

        if (!Enum.TryParse<LookupRequestType>( lookupTypeStr, out LookupRequestType lookupType )) {
            _logger.LogWarning( "Saga {SagaId} has invalid lookup type: {Type}", sagaId, lookupTypeStr );
            return null;
        }

        _ = fields.TryGetValue( FieldCreatedAt, out string? createdAtStr );
        _ = fields.TryGetValue( FieldPartialResultUri, out string? partialUri );
        _ = fields.TryGetValue( FieldFinalResultUri, out string? finalUri );
        _ = fields.TryGetValue( FieldIsPartial, out string? isPartialStr );
        _ = fields.TryGetValue( FieldInitialProvider, out string? initialProviderStr );
        _ = fields.TryGetValue( FieldRateLimitInfo, out string? rateLimitInfoJson );

        DateTimeOffset createdAt = !string.IsNullOrEmpty( createdAtStr )
            ? DateTimeOffset.Parse( createdAtStr )
            : DateTimeOffset.UtcNow;

        _ = bool.TryParse( isPartialStr, out bool isPartial );

        SupportedProviders? initialProvider = !string.IsNullOrEmpty( initialProviderStr ) &&
            Enum.TryParse<SupportedProviders>( initialProviderStr, out SupportedProviders parsedProvider )
                ? parsedProvider
                : null;

        List<ProviderRateLimitInfo>? rateLimitInfo = !string.IsNullOrEmpty( rateLimitInfoJson )
            ? System.Text.Json.JsonSerializer.Deserialize<List<ProviderRateLimitInfo>>( rateLimitInfoJson )
            : null;

        // Load provider states
        Dictionary<SupportedProviders, ProviderLookupState> providerStates = await LoadProviderStatesAsync( db, sagaId );

        return new LookupSagaState {
            SagaId = sagaId,
            LookupKey = lookupKey,
            LookupType = lookupType,
            LookupValue = lookupValue,
            CreatedAt = createdAt,
            ProviderStates = providerStates,
            PartialResultUri = string.IsNullOrEmpty( partialUri ) ? null : partialUri,
            FinalResultUri = string.IsNullOrEmpty( finalUri ) ? null : finalUri,
            IsPartial = isPartial,
            InitialProvider = initialProvider,
            RateLimitInfo = rateLimitInfo
        };
    }

    /// <inheritdoc/>
    public async Task UpdateProviderStateAsync(
        string sagaId,
        ProviderLookupState state,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );
        ArgumentNullException.ThrowIfNull( state );

        string key = GetProviderKey( sagaId, state.Provider );
        IDatabase db = _redis.GetDatabase( );
        TimeSpan ttl = TimeSpan.FromMinutes( _settings.JobExpirationMinutes );

        HashEntry[] providerHash = [
            new HashEntry( FieldIsComplete, state.IsComplete.ToString() ),
            new HashEntry( FieldIsSuccess, state.IsSuccess.ToString() ),
            new HashEntry( FieldResultJson, state.ResultJson ?? string.Empty ),
            new HashEntry( FieldCompletedAt, state.CompletedAt?.ToString( "O" ) ?? string.Empty ),
            new HashEntry( FieldErrorMessage, state.ErrorMessage ?? string.Empty )
        ];

        await db.HashSetAsync( key, providerHash );
        _ = await db.KeyExpireAsync( key, ttl );

        // Also extend the main saga key TTL
        _ = await db.KeyExpireAsync( GetSagaKey( sagaId ), ttl );

        _logger.LogDebug(
            "Updated provider state for saga {SagaId}, provider {Provider}: complete={Complete}, success={Success}",
            sagaId,
            state.Provider,
            state.IsComplete,
            state.IsSuccess
        );
    }

    /// <inheritdoc/>
    public async Task SetPartialResultUriAsync(
        string sagaId,
        string uri,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );
        ArgumentException.ThrowIfNullOrWhiteSpace( uri );

        string key = GetSagaKey( sagaId );
        IDatabase db = _redis.GetDatabase( );
        TimeSpan ttl = TimeSpan.FromMinutes( _settings.JobExpirationMinutes );

        _ = await db.HashSetAsync( key, FieldPartialResultUri, uri );
        _ = await db.KeyExpireAsync( key, ttl );

        _logger.LogInformation(
            "Set partial result URI for saga {SagaId}: {Uri}",
            sagaId,
            uri
        );
    }

    /// <inheritdoc/>
    public async Task SetFinalResultUriAsync(
        string sagaId,
        string uri,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );
        ArgumentException.ThrowIfNullOrWhiteSpace( uri );

        string key = GetSagaKey( sagaId );
        IDatabase db = _redis.GetDatabase( );
        TimeSpan ttl = TimeSpan.FromMinutes( _settings.JobExpirationMinutes );

        _ = await db.HashSetAsync( key, FieldFinalResultUri, uri );
        _ = await db.KeyExpireAsync( key, ttl );

        // Remove from pending index since saga is now finalized
        await RemoveFromPendingIndexAsync( sagaId, cancellationToken );

        _logger.LogInformation(
            "Set final result URI for saga {SagaId}: {Uri}",
            sagaId,
            uri
        );
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync( string sagaId, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );

        IDatabase db = _redis.GetDatabase( );

        // Remove from pending index
        await RemoveFromPendingIndexAsync( sagaId, cancellationToken );

        // Delete main saga key
        string sagaKey = GetSagaKey( sagaId );
        bool sagaDeleted = await db.KeyDeleteAsync( sagaKey );

        // Delete all provider state keys
        foreach (SupportedProviders provider in Enum.GetValues<SupportedProviders>( )) {
            string providerKey = GetProviderKey( sagaId, provider );
            _ = await db.KeyDeleteAsync( providerKey );
        }

        if (sagaDeleted) {
            _logger.LogInformation( "Deleted saga {SagaId}", sagaId );
        }

        return sagaDeleted;
    }

    /// <inheritdoc/>
    public async Task SetIsPartialAsync( string sagaId, bool isPartial, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );

        string key = GetSagaKey( sagaId );
        IDatabase db = _redis.GetDatabase( );
        TimeSpan ttl = TimeSpan.FromMinutes( _settings.JobExpirationMinutes );

        _ = await db.HashSetAsync( key, FieldIsPartial, isPartial.ToString( ) );
        _ = await db.KeyExpireAsync( key, ttl );

        _logger.LogDebug( "Set isPartial={IsPartial} for saga {SagaId}", isPartial, sagaId );
    }

    /// <inheritdoc/>
    public async Task SetInitialProviderAsync( string sagaId, SupportedProviders provider, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );

        string key = GetSagaKey( sagaId );
        IDatabase db = _redis.GetDatabase( );
        TimeSpan ttl = TimeSpan.FromMinutes( _settings.JobExpirationMinutes );

        _ = await db.HashSetAsync( key, FieldInitialProvider, provider.ToString( ) );
        _ = await db.KeyExpireAsync( key, ttl );

        _logger.LogDebug( "Set initialProvider={Provider} for saga {SagaId}", provider, sagaId );
    }

    /// <inheritdoc/>
    public async Task SetRateLimitInfoAsync( string sagaId, List<ProviderRateLimitInfo> rateLimitInfo, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );
        ArgumentNullException.ThrowIfNull( rateLimitInfo );

        string key = GetSagaKey( sagaId );
        IDatabase db = _redis.GetDatabase( );
        TimeSpan ttl = TimeSpan.FromMinutes( _settings.JobExpirationMinutes );

        string json = System.Text.Json.JsonSerializer.Serialize( rateLimitInfo );
        _ = await db.HashSetAsync( key, FieldRateLimitInfo, json );
        _ = await db.KeyExpireAsync( key, ttl );

        _logger.LogDebug(
            "Set rateLimitInfo for saga {SagaId} with {Count} rate-limited providers",
            sagaId,
            rateLimitInfo.Count
        );
    }

    /// <inheritdoc/>
    public async Task InitializeProviderStatesAsync( string sagaId, IEnumerable<SupportedProviders> providers, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );
        ArgumentNullException.ThrowIfNull( providers );

        IDatabase db = _redis.GetDatabase( );
        TimeSpan ttl = TimeSpan.FromMinutes( _settings.JobExpirationMinutes );

        foreach (SupportedProviders provider in providers) {
            // Create initial pending state for each provider
            ProviderLookupState pendingState = new(
                Provider: provider,
                IsComplete: false,
                IsSuccess: false,
                ResultJson: null,
                CompletedAt: null,
                ErrorMessage: null
            );

            string key = GetProviderKey( sagaId, provider );
            HashEntry[] providerHash = [
                new HashEntry( FieldIsComplete, pendingState.IsComplete.ToString() ),
                new HashEntry( FieldIsSuccess, pendingState.IsSuccess.ToString() ),
                new HashEntry( FieldResultJson, string.Empty ),
                new HashEntry( FieldCompletedAt, string.Empty ),
                new HashEntry( FieldErrorMessage, string.Empty )
            ];

            await db.HashSetAsync( key, providerHash );
            _ = await db.KeyExpireAsync( key, ttl );
        }

        _logger.LogDebug(
            "Initialized provider states for saga {SagaId} with {Count} providers",
            sagaId,
            providers.Count( )
        );
    }

    private async Task<Dictionary<SupportedProviders, ProviderLookupState>> LoadProviderStatesAsync(
        IDatabase db,
        string sagaId
    ) {
        Dictionary<SupportedProviders, ProviderLookupState> states = [];

        foreach (SupportedProviders provider in Enum.GetValues<SupportedProviders>( )) {
            string key = GetProviderKey( sagaId, provider );
            HashEntry[] hash = await db.HashGetAllAsync( key );

            if (hash.Length == 0) {
                continue;
            }

            Dictionary<string, string> fields = hash.ToDictionary(
                h => h.Name.ToString( ),
                h => h.Value.ToString( )
            );

            _ = bool.TryParse( fields.GetValueOrDefault( FieldIsComplete, "false" ), out bool isComplete );
            _ = bool.TryParse( fields.GetValueOrDefault( FieldIsSuccess, "false" ), out bool isSuccess );

            string? resultJson = fields.GetValueOrDefault( FieldResultJson );
            string? completedAtStr = fields.GetValueOrDefault( FieldCompletedAt );
            string? errorMessage = fields.GetValueOrDefault( FieldErrorMessage );

            DateTimeOffset? completedAt = !string.IsNullOrEmpty( completedAtStr )
                ? DateTimeOffset.Parse( completedAtStr )
                : null;

            states[provider] = new ProviderLookupState(
                Provider: provider,
                IsComplete: isComplete,
                IsSuccess: isSuccess,
                ResultJson: string.IsNullOrEmpty( resultJson ) ? null : resultJson,
                CompletedAt: completedAt,
                ErrorMessage: string.IsNullOrEmpty( errorMessage ) ? null : errorMessage
            );
        }

        return states;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LookupSagaState>> GetCompletedButUnfinalizedAsync(
        TimeSpan minimumAge,
        int limit = 100,
        CancellationToken cancellationToken = default
    ) {
        IDatabase db = _redis.GetDatabase( );
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - minimumAge;
        List<LookupSagaState> result = [];

        // Get all saga IDs from the pending set
        RedisValue[] pendingSagaIds = await db.SetMembersAsync( PendingSagaSetKey );

        foreach (RedisValue sagaIdValue in pendingSagaIds) {
            if (result.Count >= limit) {
                break;
            }

            string sagaId = sagaIdValue.ToString( );
            LookupSagaState? saga = await GetAsync( sagaId, cancellationToken );

            if (saga is null) {
                // Saga expired or was deleted - clean up the index
                _ = await db.SetRemoveAsync( PendingSagaSetKey, sagaId );
                continue;
            }

            // Skip if already finalized
            if (!string.IsNullOrEmpty( saga.FinalResultUri )) {
                // Already finalized - remove from pending index
                _ = await db.SetRemoveAsync( PendingSagaSetKey, sagaId );
                continue;
            }

            // Skip if too young (avoid race with Pub/Sub)
            if (saga.CreatedAt > cutoff) {
                continue;
            }

            // Check if all providers are complete
            if (!saga.IsComplete) {
                continue;
            }

            result.Add( saga );
        }

        if (result.Count > 0) {
            _logger.LogInformation(
                "Found {Count} completed but unfinalized sagas during polling",
                result.Count
            );
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task AddToPendingIndexAsync( string sagaId, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );

        IDatabase db = _redis.GetDatabase( );
        _ = await db.SetAddAsync( PendingSagaSetKey, sagaId );

        _logger.LogDebug( "Added saga {SagaId} to pending index", sagaId );
    }

    /// <inheritdoc/>
    public async Task RemoveFromPendingIndexAsync( string sagaId, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );

        IDatabase db = _redis.GetDatabase( );
        _ = await db.SetRemoveAsync( PendingSagaSetKey, sagaId );

        _logger.LogDebug( "Removed saga {SagaId} from pending index", sagaId );
    }

    private static string GetSagaKey( string sagaId ) => $"{SagaPrefix}{sagaId}";

    private static string GetProviderKey( string sagaId, SupportedProviders provider ) =>
        $"{SagaPrefix}{sagaId}{ProviderSuffix}{provider}";
}
