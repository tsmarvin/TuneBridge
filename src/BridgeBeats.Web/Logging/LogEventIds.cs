using DashboardController = BridgeBeats.Web.Controllers.DashboardController;
using HealthEndpointAuthorizationMiddleware = BridgeBeats.Web.Middleware.HealthEndpointAuthorizationMiddleware;
using RateLimitingMiddleware = BridgeBeats.Web.Middleware.RateLimitingMiddleware;

namespace BridgeBeats.Web.Logging;

/// <summary>
/// EventIds for Web layer components (4000-4999).
/// Extends <see cref="Core.Infrastructure.Logging.LogEventIds"/> with Web-specific EventIds.
/// </summary>
/// <remarks>
/// Range Allocation:
/// <list type="bullet">
///   <item>4000-4499: Controllers</item>
///   <item>4500-4749: Middleware</item>
///   <item>4750-4899: Configuration</item>
///   <item>4900-4999: BackgroundServices</item>
/// </list>
/// </remarks>
public static class LogEventIds {
    /// <summary>
    /// EventIds for Controllers (4000-4499).
    /// </summary>
    public static class Controllers {
        // AccountController (4000-4049)

        /// <summary>EventId for AccountController user registration.</summary>
        public const int AccountControllerRegistered = 4000;

        /// <summary>EventId for AccountController user login.</summary>
        public const int AccountControllerLoggedIn = 4001;

        /// <summary>EventId for AccountController API key regeneration.</summary>
        public const int AccountControllerApiKeyRegenerated = 4002;

        /// <summary>EventId for AccountController data download.</summary>
        public const int AccountControllerDataDownloaded = 4003;

        /// <summary>EventId for AccountController delete failure.</summary>
        public const int AccountControllerDeleteFailed = 4004;

        /// <summary>EventId for AccountController account deleted.</summary>
        public const int AccountControllerDeleted = 4005;

        /// <summary>EventId for AccountController ATProto OAuth started.</summary>
        public const int AccountControllerAtProtoOAuthStarted = 4006;

        /// <summary>EventId for AccountController ATProto OAuth start failed.</summary>
        public const int AccountControllerAtProtoOAuthStartFailed = 4007;

        /// <summary>EventId for AccountController ATProto OAuth error.</summary>
        public const int AccountControllerAtProtoOAuthError = 4008;

        /// <summary>EventId for AccountController ATProto user create failed.</summary>
        public const int AccountControllerAtProtoUserCreateFailed = 4009;

        /// <summary>EventId for AccountController ATProto user created.</summary>
        public const int AccountControllerAtProtoUserCreated = 4010;

        /// <summary>EventId for AccountController ATProto tokens updated.</summary>
        public const int AccountControllerAtProtoTokensUpdated = 4011;

        /// <summary>EventId for AccountController ATProto logged in.</summary>
        public const int AccountControllerAtProtoLoggedIn = 4012;

        /// <summary>EventId for AccountController ATProto callback failed.</summary>
        public const int AccountControllerAtProtoCallbackFailed = 4013;

        // HomeController (4050-4074)

        /// <summary>EventId for HomeController cache invalid operation.</summary>
        public const int HomeControllerCacheInvalidOp = 4050;

        /// <summary>EventId for HomeController cache argument error.</summary>
        public const int HomeControllerCacheArgError = 4051;

        /// <summary>EventId for HomeController cache error.</summary>
        public const int HomeControllerCacheError = 4052;

        /// <summary>EventId for HomeController stream result error.</summary>
        public const int HomeControllerStreamResultError = 4053;

        /// <summary>EventId for HomeController stream error.</summary>
        public const int HomeControllerStreamError = 4054;

        // DashboardController (4075-4099)

        /// <summary>
        /// EventId for <see cref="DashboardController.LogAuthorizationFailedUserNotFound"/>.
        /// </summary>
        public const int DashboardControllerAuthorizationFailedUserNotFound = 4075;

        /// <summary>
        /// EventId for <see cref="DashboardController.LogAuthorizationDeniedMissingRole"/>.
        /// </summary>
        public const int DashboardControllerAuthorizationDeniedMissingRole = 4076;

        /// <summary>
        /// EventId for <see cref="DashboardController.LogAuthorizationGranted"/>.
        /// </summary>
        public const int DashboardControllerAuthorizationGranted = 4077;

        // MusicLookupController (4100-4149)

        // OpenGraphCardController (4150-4174)

        /// <summary>EventId for OpenGraphCardController cache invalid operation.</summary>
        public const int OpenGraphCardControllerCacheInvalidOp = 4150;

        /// <summary>EventId for OpenGraphCardController cache argument error.</summary>
        public const int OpenGraphCardControllerCacheArgError = 4151;

        /// <summary>EventId for OpenGraphCardController cache error.</summary>
        public const int OpenGraphCardControllerCacheError = 4152;

        // PlaylistController (4200-4249)

        /// <summary>EventId for PlaylistController create error.</summary>
        public const int PlaylistControllerCreateError = 4200;

        /// <summary>EventId for PlaylistController mismatched cards.</summary>
        public const int PlaylistControllerMismatchedCards = 4201;

        /// <summary>EventId for PlaylistController card regenerated.</summary>
        public const int PlaylistControllerCardRegenerated = 4202;

        /// <summary>EventId for PlaylistController card load failed.</summary>
        public const int PlaylistControllerCardLoadFailed = 4203;

        /// <summary>EventId for PlaylistController embed card regenerated.</summary>
        public const int PlaylistControllerEmbedCardRegenerated = 4204;

        /// <summary>EventId for PlaylistController embed card load failed.</summary>
        public const int PlaylistControllerEmbedCardLoadFailed = 4205;

        // PlaylistsController (4275-4299)

        /// <summary>EventId for PlaylistsController load error.</summary>
        public const int PlaylistsControllerLoadError = 4275;

        /// <summary>EventId for PlaylistsController delete error.</summary>
        public const int PlaylistsControllerDeleteError = 4276;

        // StatisticsController (4300-4324)

        /// <summary>EventId for StatisticsController not available.</summary>
        public const int StatisticsControllerNotAvailable = 4300;

        /// <summary>EventId for StatisticsController retrieve error.</summary>
        public const int StatisticsControllerRetrieveError = 4301;

        /// <summary>EventId for StatisticsController refresh error.</summary>
        public const int StatisticsControllerRefreshError = 4302;

        // AppleMusicController (4250-4274)

        /// <summary>EventId for AppleMusicController store token failed.</summary>
        public const int AppleMusicControllerStoreTokenFailed = 4250;

        /// <summary>EventId for AppleMusicController token stored.</summary>
        public const int AppleMusicControllerTokenStored = 4251;

        /// <summary>EventId for AppleMusicController get playlists failed.</summary>
        public const int AppleMusicControllerGetPlaylistsFailed = 4252;

        /// <summary>EventId for AppleMusicController get playlists error.</summary>
        public const int AppleMusicControllerGetPlaylistsError = 4253;

        /// <summary>EventId for AppleMusicController cache invalid operation.</summary>
        public const int AppleMusicControllerCacheInvalidOp = 4254;

        /// <summary>EventId for AppleMusicController cache argument error.</summary>
        public const int AppleMusicControllerCacheArgError = 4255;

        /// <summary>EventId for AppleMusicController cache error.</summary>
        public const int AppleMusicControllerCacheError = 4256;

        /// <summary>EventId for AppleMusicController get tracks failed.</summary>
        public const int AppleMusicControllerGetTracksFailed = 4257;

        /// <summary>EventId for AppleMusicController playlist too large.</summary>
        public const int AppleMusicControllerPlaylistTooLarge = 4258;

        /// <summary>EventId for AppleMusicController process playlist error.</summary>
        public const int AppleMusicControllerProcessPlaylistError = 4259;

        /// <summary>EventId for AppleMusicController process song error.</summary>
        public const int AppleMusicControllerProcessSongError = 4260;
    }

    /// <summary>
    /// EventIds for Middleware (4500-4749).
    /// </summary>
    public static class Middleware {
        // RateLimitingMiddleware (4500-4524)

        /// <summary>
        /// EventId for <see cref="RateLimitingMiddleware.LogRateLimitExceeded"/>.
        /// </summary>
        public const int RateLimitingMiddlewareRateLimitExceeded = 4500;

        // 4501 retired (concurrent-check path removed)

        // HealthEndpointAuthorizationMiddleware (4525-4549)

        /// <summary>
        /// EventId for <see cref="HealthEndpointAuthorizationMiddleware.LogBlockedExternalAccess"/>.
        /// </summary>
        public const int HealthEndpointAuthorizationMiddlewareBlockedExternalAccess = 4525;

        // ApiKeyAuthenticationMiddleware (4550-4574)

        // ExceptionHandlingMiddleware (4600-4624)
    }

    /// <summary>
    /// EventIds for Configuration (4750-4899).
    /// </summary>
    public static class Configuration {
        // StartupExtensions (4750-4774)

        /// <summary>EventId for StartupExtensions Redis not configured.</summary>
        public const int StartupExtensionsRedisNotConfigured = 4750;

        /// <summary>EventId for StartupExtensions Redis connected.</summary>
        public const int StartupExtensionsRedisConnected = 4751;

        /// <summary>EventId for StartupExtensions Redis failed.</summary>
        public const int StartupExtensionsRedisFailed = 4752;
    }
}
