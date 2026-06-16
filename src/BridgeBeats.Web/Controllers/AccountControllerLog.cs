namespace BridgeBeats.Web.Controllers;

/// <summary>
/// Source-generated <see cref="Microsoft.Extensions.Logging.LoggerMessageAttribute"/> logging methods
/// for <see cref="AccountController"/>.
/// </summary>
public partial class AccountController {
    /// <summary>
    /// Logs that a user registered successfully.
    /// </summary>
    /// <param name="userId">The id of the newly registered user.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerRegistered,
        Level = LogLevel.Information,
        Message = "User registered successfully with ID: {UserId}" )]
    private partial void LogUserRegistered( string userId );

    /// <summary>
    /// Logs that a user signed in successfully.
    /// </summary>
    /// <param name="userId">The id of the user who signed in.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerLoggedIn,
        Level = LogLevel.Information,
        Message = "User logged in successfully with ID: {UserId}" )]
    private partial void LogUserLoggedIn( string userId );

    /// <summary>
    /// Logs that a user regenerated their API key.
    /// </summary>
    /// <param name="userId">The id of the user whose API key was regenerated.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerApiKeyRegenerated,
        Level = LogLevel.Information,
        Message = "User regenerated API key with ID: {UserId}" )]
    private partial void LogApiKeyRegenerated( string userId );

    /// <summary>
    /// Logs that a user exported their personal data.
    /// </summary>
    /// <param name="userId">The id of the user who downloaded their data.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerDataDownloaded,
        Level = LogLevel.Information,
        Message = "User downloaded personal data with ID: {UserId}" )]
    private partial void LogDataDownloaded( string userId );

    /// <summary>
    /// Logs that an account deletion attempt failed.
    /// </summary>
    /// <param name="userId">The id of the user whose deletion failed.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerDeleteFailed,
        Level = LogLevel.Error,
        Message = "Failed to delete account for user ID: {UserId}" )]
    private partial void LogDeleteFailed( string userId );

    /// <summary>
    /// Logs that a user account was deleted.
    /// </summary>
    /// <param name="userId">The id of the deleted user.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerDeleted,
        Level = LogLevel.Information,
        Message = "User account deleted with ID: {UserId}" )]
    private partial void LogAccountDeleted( string userId );

    /// <summary>
    /// Logs that ATProto OAuth authorization was started for a handle.
    /// </summary>
    /// <param name="handle">The Bluesky handle that began authorization.</param>
    /// <param name="authUrl">The host of the authorization URL the user is being redirected to.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerAtProtoOAuthStarted,
        Level = LogLevel.Information,
        Message = "Started ATProto OAuth for handle {Handle}, redirecting to {AuthUrl}" )]
    private partial void LogAtProtoOAuthStarted( string handle, string authUrl );

    /// <summary>
    /// Logs that starting ATProto OAuth authorization failed for a handle.
    /// </summary>
    /// <param name="ex">The exception that caused the failure.</param>
    /// <param name="handle">The Bluesky handle that failed to start authorization.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerAtProtoOAuthStartFailed,
        Level = LogLevel.Error,
        Message = "Failed to start ATProto OAuth for handle {Handle}" )]
    private partial void LogAtProtoOAuthStartFailed( Exception ex, string handle );

    /// <summary>
    /// Logs an error code returned by the provider on the ATProto OAuth callback.
    /// </summary>
    /// <param name="error">The OAuth error code.</param>
    /// <param name="description">The optional human-readable error description.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerAtProtoOAuthError,
        Level = LogLevel.Warning,
        Message = "ATProto OAuth error: {Error} - {Description}" )]
    private partial void LogAtProtoOAuthError( string error, string? description );

    /// <summary>
    /// Logs that creating a new user for an ATProto identity failed.
    /// </summary>
    /// <param name="errors">The concatenated identity error descriptions.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerAtProtoUserCreateFailed,
        Level = LogLevel.Error,
        Message = "Failed to create ATProto user: {Errors}" )]
    private partial void LogAtProtoUserCreateFailed( string errors );

    /// <summary>
    /// Logs that a new user was created for an ATProto identity.
    /// </summary>
    /// <param name="did">The ATProto DID the account was created for.</param>
    /// <param name="handle">The associated Bluesky handle.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerAtProtoUserCreated,
        Level = LogLevel.Information,
        Message = "Created new user for ATProto DID {Did}, handle {Handle}" )]
    private partial void LogAtProtoUserCreated( string did, string handle );

    /// <summary>
    /// Logs that stored tokens were refreshed for an existing ATProto user.
    /// </summary>
    /// <param name="did">The ATProto DID whose tokens were updated.</param>
    /// <param name="handle">The associated Bluesky handle.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerAtProtoTokensUpdated,
        Level = LogLevel.Information,
        Message = "Updated tokens for ATProto user DID {Did}, handle {Handle}" )]
    private partial void LogAtProtoTokensUpdated( string did, string handle );

    /// <summary>
    /// Logs that an ATProto user signed in successfully.
    /// </summary>
    /// <param name="userId">The id of the signed-in user.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerAtProtoLoggedIn,
        Level = LogLevel.Information,
        Message = "ATProto user logged in: {UserId}" )]
    private partial void LogAtProtoLoggedIn( string userId );

    /// <summary>
    /// Logs that completing the ATProto OAuth callback failed.
    /// </summary>
    /// <param name="ex">The exception that caused the failure.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerAtProtoCallbackFailed,
        Level = LogLevel.Error,
        Message = "Failed to complete ATProto OAuth callback" )]
    private partial void LogAtProtoCallbackFailed( Exception ex );
}
