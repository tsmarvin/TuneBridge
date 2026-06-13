using System.Net;
using BridgeBeats.Core.Infrastructure.Logging;

namespace BridgeBeats.Core.Infrastructure.Storage;

/// <summary>
/// LoggerMessage methods for <see cref="RedisATProtoSessionManager"/>.
/// </summary>
public sealed partial class RedisATProtoSessionManager {
    /// <summary>
    /// Logs that force re-authentication was requested.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerForceReauthRequested,
        Level = LogLevel.Warning,
        Message = "Force re-authentication requested for {Identifier}" )]
    private partial void LogForceReauthRequested( string identifier );

    /// <summary>
    /// Logs that no stored credentials were found in Redis.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerNoStoredCredentials,
        Level = LogLevel.Debug,
        Message = "No stored credentials found in Redis for {Identifier}" )]
    private partial void LogNoStoredCredentials( string identifier );

    /// <summary>
    /// Logs that deserialization of stored credentials failed.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerDeserializeFailed,
        Level = LogLevel.Warning,
        Message = "Failed to deserialize stored credentials for {Identifier}" )]
    private partial void LogDeserializeFailed( string identifier );

    /// <summary>
    /// Logs that session restoration is being attempted.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerRestoringSession,
        Level = LogLevel.Information,
        Message = "Attempting to restore session for {Identifier} (persisted at {PersistedAt})" )]
    private partial void LogRestoringSession( string identifier, DateTimeOffset persistedAt );

    /// <summary>
    /// Logs that session was successfully restored.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerSessionRestored,
        Level = LogLevel.Information,
        Message = "Successfully restored session for {Identifier}" )]
    private partial void LogSessionRestored( string identifier );

    /// <summary>
    /// Logs that session restoration failed because refresh returned false.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerRestoreFailed,
        Level = LogLevel.Warning,
        Message = "Failed to restore session for {Identifier} - refresh returned false" )]
    private partial void LogRestoreFailed( string identifier );

    /// <summary>
    /// Logs that an exception occurred while restoring session.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerRestoreException,
        Level = LogLevel.Warning,
        Message = "Exception while restoring session for {Identifier}" )]
    private partial void LogRestoreException( Exception ex, string identifier );

    /// <summary>
    /// Logs that another instance holds the distributed lock and this process is waiting.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerWaitingForLock,
        Level = LogLevel.Debug,
        Message = "Another instance holds the credential-mutation lock for {Identifier}, waiting..." )]
    private partial void LogWaitingForLock( string identifier );

    /// <summary>
    /// Logs that the distributed lock wait timed out and the request is being rejected.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerLockWaitTimeout,
        Level = LogLevel.Error,
        Message = "Distributed lock wait timed out for {Identifier} — failing closed to protect shared refresh token" )]
    private partial void LogLockWaitTimeout( string identifier );

    /// <summary>
    /// Logs that a fresh login is being performed.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerFreshLogin,
        Level = LogLevel.Information,
        Message = "Performing fresh login for {Identifier}" )]
    private partial void LogFreshLogin( string identifier );

    /// <summary>
    /// Logs that authentication to PDS was successful.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerAuthenticated,
        Level = LogLevel.Information,
        Message = "Successfully authenticated to PDS for {Identifier}" )]
    private partial void LogAuthenticated( string identifier );

    /// <summary>
    /// Logs the agent authenticated event.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerAgentAuthenticated,
        Level = LogLevel.Debug,
        Message = "Agent authenticated event for {Identifier}" )]
    private partial void LogAgentAuthenticated( string identifier );

    /// <summary>
    /// Logs failure to persist credentials after authentication.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerPersistAfterAuthFailed,
        Level = LogLevel.Error,
        Message = "Failed to persist credentials after authentication for {Identifier}" )]
    private partial void LogPersistAfterAuthFailed( Exception ex, string identifier );

    /// <summary>
    /// Logs the agent credentials updated event.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerCredentialsUpdated,
        Level = LogLevel.Debug,
        Message = "Agent credentials updated event for {Identifier}" )]
    private partial void LogCredentialsUpdated( string identifier );

    /// <summary>
    /// Logs failure to persist updated credentials.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerPersistUpdateFailed,
        Level = LogLevel.Error,
        Message = "Failed to persist updated credentials for {Identifier}" )]
    private partial void LogPersistUpdateFailed( Exception ex, string identifier );

    /// <summary>
    /// Logs a token refresh failure. Demoted to Debug because with background refresh disabled
    /// this event fires only on explicit refresh calls; the relevant diagnostic is in the exception
    /// path logs, not here. StatusCode is included to aid triage of unrecoverable vs. transport failures.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerTokenRefreshFailed,
        Level = LogLevel.Debug,
        Message = "Token refresh failed for {Identifier} (StatusCode: {StatusCode})" )]
    private partial void LogTokenRefreshFailed( string identifier, HttpStatusCode? statusCode );

    /// <summary>
    /// Logs that a credential clear was suppressed by the cooldown guard.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerClearSuppressedByCooldown,
        Level = LogLevel.Debug,
        Message = "Credential clear suppressed by cooldown for {Identifier}" )]
    private partial void LogClearSuppressedByCooldown( string identifier );

    /// <summary>
    /// Logs failure to clear credentials after token refresh failure.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerClearAfterRefreshFailed,
        Level = LogLevel.Error,
        Message = "Failed to clear credentials after token refresh failure for {Identifier}" )]
    private partial void LogClearAfterRefreshFailed( Exception ex, string identifier );

    /// <summary>
    /// Logs the agent unauthenticated event.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerUnauthenticated,
        Level = LogLevel.Information,
        Message = "Agent unauthenticated event for {Identifier}" )]
    private partial void LogUnauthenticated( string identifier );

    /// <summary>
    /// Logs failure to clear credentials after unauthenticated event.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerClearAfterUnauthFailed,
        Level = LogLevel.Error,
        Message = "Failed to clear credentials after unauthenticated event for {Identifier}" )]
    private partial void LogClearAfterUnauthFailed( Exception ex, string identifier );

    /// <summary>
    /// Logs that agent is not authenticated and credentials cannot be persisted.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerCannotPersist,
        Level = LogLevel.Debug,
        Message = "Cannot persist credentials - agent not authenticated" )]
    private partial void LogCannotPersist( );

    /// <summary>
    /// Logs that credentials were persisted to Redis.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerPersisted,
        Level = LogLevel.Debug,
        Message = "Persisted credentials to Redis for {Identifier}" )]
    private partial void LogPersisted( string identifier );

    /// <summary>
    /// Logs that credentials were cleared from Redis.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerCleared,
        Level = LogLevel.Debug,
        Message = "Cleared stored credentials from Redis for {Identifier}" )]
    private partial void LogCleared( string identifier );

    /// <summary>
    /// Logs failure to release the distributed lock.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.RedisATProtoSessionManagerLockReleaseFailed,
        Level = LogLevel.Error,
        Message = "Failed to release distributed credential-mutation lock for {Identifier}" )]
    private partial void LogLockReleaseFailed( Exception ex, string identifier );
}
