namespace BridgeBeats.Web.Controllers;

/// <summary>
/// LoggerMessage methods for <see cref="AccountController"/>.
/// </summary>
public partial class AccountController {
    /// <summary>
    /// Logs successful user registration.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerRegistered,
        Level = LogLevel.Information,
        Message = "User registered successfully with ID: {UserId}" )]
    private partial void LogUserRegistered( string userId );

    /// <summary>
    /// Logs successful user login.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerLoggedIn,
        Level = LogLevel.Information,
        Message = "User logged in successfully with ID: {UserId}" )]
    private partial void LogUserLoggedIn( string userId );

    /// <summary>
    /// Logs API key regeneration.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerApiKeyRegenerated,
        Level = LogLevel.Information,
        Message = "User regenerated API key with ID: {UserId}" )]
    private partial void LogApiKeyRegenerated( string userId );

    /// <summary>
    /// Logs personal data download.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerDataDownloaded,
        Level = LogLevel.Information,
        Message = "User downloaded personal data with ID: {UserId}" )]
    private partial void LogDataDownloaded( string userId );

    /// <summary>
    /// Logs account deletion failure.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerDeleteFailed,
        Level = LogLevel.Error,
        Message = "Failed to delete account for user ID: {UserId}" )]
    private partial void LogDeleteFailed( string userId );

    /// <summary>
    /// Logs account deletion success.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerDeleted,
        Level = LogLevel.Information,
        Message = "User account deleted with ID: {UserId}" )]
    private partial void LogAccountDeleted( string userId );

    /// <summary>
    /// Logs ATProto OAuth start.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerAtProtoOAuthStarted,
        Level = LogLevel.Information,
        Message = "Started ATProto OAuth for handle {Handle}, redirecting to {AuthUrl}" )]
    private partial void LogAtProtoOAuthStarted( string handle, string authUrl );

    /// <summary>
    /// Logs ATProto OAuth start failure.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerAtProtoOAuthStartFailed,
        Level = LogLevel.Error,
        Message = "Failed to start ATProto OAuth for handle {Handle}" )]
    private partial void LogAtProtoOAuthStartFailed( Exception ex, string handle );

    /// <summary>
    /// Logs ATProto OAuth callback error.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerAtProtoOAuthError,
        Level = LogLevel.Warning,
        Message = "ATProto OAuth error: {Error} - {Description}" )]
    private partial void LogAtProtoOAuthError( string error, string? description );

    /// <summary>
    /// Logs ATProto user creation failure.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerAtProtoUserCreateFailed,
        Level = LogLevel.Error,
        Message = "Failed to create ATProto user: {Errors}" )]
    private partial void LogAtProtoUserCreateFailed( string errors );

    /// <summary>
    /// Logs ATProto new user creation success.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerAtProtoUserCreated,
        Level = LogLevel.Information,
        Message = "Created new user for ATProto DID {Did}, handle {Handle}" )]
    private partial void LogAtProtoUserCreated( string did, string handle );

    /// <summary>
    /// Logs ATProto token update for existing user.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerAtProtoTokensUpdated,
        Level = LogLevel.Information,
        Message = "Updated tokens for ATProto user DID {Did}, handle {Handle}" )]
    private partial void LogAtProtoTokensUpdated( string did, string handle );

    /// <summary>
    /// Logs ATProto user login success.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerAtProtoLoggedIn,
        Level = LogLevel.Information,
        Message = "ATProto user logged in: {UserId}" )]
    private partial void LogAtProtoLoggedIn( string userId );

    /// <summary>
    /// Logs ATProto OAuth callback failure.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AccountControllerAtProtoCallbackFailed,
        Level = LogLevel.Error,
        Message = "Failed to complete ATProto OAuth callback" )]
    private partial void LogAtProtoCallbackFailed( Exception ex );
}
