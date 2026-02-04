using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BridgeBeats.Web.Controllers;

/// <summary>
/// Controller for user authentication and account management.
/// Provides both API endpoints and web UI for user registration, login, and API key generation.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="AccountController"/> class.
/// </remarks>
/// <param name="userManager">User manager for ASP.NET Identity.</param>
/// <param name="signInManager">Sign-in manager for authentication.</param>
/// <param name="logger">Logger for diagnostic information.</param>
/// <param name="hasher">API key hasher for secure key generation and validation.</param>
public partial class AccountController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ILogger<AccountController> logger,
    ApiKeyHasher hasher
) : Controller {

    /// <summary>
    /// Displays the registration page.
    /// </summary>
    [HttpGet]
    [Route( "account/register" )]
    public IActionResult RegisterPage( ) {
        return View( "Register" );
    }

    /// <summary>
    /// Displays the login page.
    /// </summary>
    [HttpGet]
    [Route( "account/login" )]
    public IActionResult LoginPage( ) {
        return View( "Login" );
    }

    /// <summary>
    /// Displays the user settings page (requires authentication).
    /// </summary>
    [Authorize]
    [HttpGet]
    [Route( "account/user" )]
    public IActionResult UserPage( ) {
        return View( "User" );
    }

    /// <summary>
    /// Registers a new user account and automatically signs them in.
    /// </summary>
    /// <param name="request">Registration details including email and password.</param>
    /// <returns>User details with API key on success.</returns>
    /// <response code="200">User successfully registered.</response>
    /// <response code="400">Registration failed (validation errors or duplicate user).</response>
    [HttpPost]
    [Route( "account/register" )]
    public async Task<IActionResult> Register( [FromBody] RegisterRequest request ) {
        if (!ModelState.IsValid) {
            return BadRequest( ModelState );
        }

        // Generate API key
        string apiKey = ApiKeyHasher.GenerateApiKey( );
        string hashedApiKey = hasher.HashApiKey( apiKey );

        ApplicationUser user = new() {
            UserName = request.Email,
            Email = request.Email,
            ApiKeyHash = hashedApiKey,
            CreatedAt = DateTime.UtcNow
        };

        IdentityResult result = await userManager.CreateAsync( user, request.Password );

        if (!result.Succeeded) {
            foreach (IdentityError error in result.Errors) {
                ModelState.AddModelError( string.Empty, error.Description );
            }
            return BadRequest( ModelState );
        }

        // Sign in the user automatically with a cookie for web UI
        await signInManager.SignInAsync( user, isPersistent: false );

        LogUserRegistered( user.Id );

        return Ok( new {
            userId = user.Id,
            apiKey,
            message = "Registration successful. Save your API key - it will not be shown again."
        } );
    }

    /// <summary>
    /// Authenticates a user and signs them in with a cookie for web UI.
    /// </summary>
    /// <param name="request">Login credentials.</param>
    /// <returns>User details with API key on success.</returns>
    /// <response code="200">Login successful.</response>
    /// <response code="401">Invalid credentials.</response>
    [HttpPost]
    [Route( "account/login" )]
    public async Task<IActionResult> Login( [FromBody] LoginRequest request ) {
        if (!ModelState.IsValid) {
            return BadRequest( ModelState );
        }

        ApplicationUser? user = await userManager.FindByEmailAsync( request.Email );
        if (user == null) {
            return Unauthorized( new { message = "Invalid email or password" } );
        }

        Microsoft.AspNetCore.Identity.SignInResult result = await signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: false
        );

        if (!result.Succeeded) {
            return Unauthorized( new { message = "Invalid email or password" } );
        }

        // Sign in the user with a cookie for web UI
        await signInManager.SignInAsync( user, isPersistent: false );

        // Generate new API key only if user doesn't have one
        string apiKey;
        if (string.IsNullOrEmpty( user.ApiKeyHash )) {
            apiKey = ApiKeyHasher.GenerateApiKey( );
            user.ApiKeyHash = hasher.HashApiKey( apiKey );
            _ = await userManager.UpdateAsync( user );
        } else {
            // Return a message that the API key is already set
            // User should use regenerate-api-key endpoint if they need a new one
            apiKey = "***EXISTING_KEY***";
        }

        LogUserLoggedIn( user.Id );

        return Ok( new {
            userId = user.Id,
            apiKey,
            message = apiKey == "***EXISTING_KEY***"
                ? "Login successful. Use /account/regenerate-api-key to get a new API key if needed."
                : "Login successful. New API key generated."
        } );
    }

    /// <summary>
    /// Logs out the currently authenticated user.
    /// </summary>
    [HttpPost]
    [Route( "account/logout" )]
    public async Task<IActionResult> Logout( ) {
        await signInManager.SignOutAsync( );
        return Ok( new { message = "Logged out successfully" } );
    }

    /// <summary>
    /// Checks if the current user is authenticated (for web UI).
    /// </summary>
    [HttpGet]
    [Route( "account/status" )]
    public async Task<IActionResult> GetAuthStatus( ) {
        if (User.Identity?.IsAuthenticated == true) {
            ApplicationUser? user = await userManager.GetUserAsync( User );
            return Ok( new {
                isAuthenticated = true,
                userId = user?.Id,
                email = user?.Email
            } );
        }
        return Ok( new { isAuthenticated = false } );
    }

    /// <summary>
    /// Regenerates the API key for the authenticated user.
    /// </summary>
    /// <returns>New API key.</returns>
    /// <response code="200">API key regenerated successfully.</response>
    /// <response code="401">User not authenticated.</response>
    [Authorize]
    [HttpPost]
    [Route( "account/regenerate-api-key" )]
    public async Task<IActionResult> RegenerateApiKey( ) {
        ApplicationUser? user = await userManager.GetUserAsync( User );
        if (user == null) {
            return Unauthorized( );
        }

        string apiKey = ApiKeyHasher.GenerateApiKey( );
        user.ApiKeyHash = hasher.HashApiKey( apiKey );
        IdentityResult result = await userManager.UpdateAsync( user );

        if (!result.Succeeded) {
            return BadRequest( new { message = "Failed to regenerate API key" } );
        }

        LogApiKeyRegenerated( user.Id );

        return Ok( new {
            apiKey,
            message = "API key regenerated successfully. Update your applications with the new key."
        } );
    }

    /// <summary>
    /// Downloads all personal data associated with the authenticated user.
    /// Returns a JSON file containing account information, playlists, and metadata.
    /// </summary>
    /// <returns>JSON file with personal data.</returns>
    /// <response code="200">Personal data downloaded successfully.</response>
    /// <response code="401">User not authenticated.</response>
    [Authorize]
    [HttpPost]
    [Route( "account/download-data" )]
    public async Task<IActionResult> DownloadPersonalData( [FromServices] IPlaylistService? playlistService ) {
        ApplicationUser? user = await userManager.GetUserAsync( User );
        if (user == null) {
            return Unauthorized( );
        }

        // Get user's playlists
        List<PlaylistEntryDto> playlists = playlistService != null
            ? await playlistService.ExportUserDataAsync( user.Id )
            : [];

        var personalData = new {
            exportDate = DateTime.UtcNow,
            user = new {
                userId = user.Id,
                email = user.Email,
                createdAt = user.CreatedAt,
                emailConfirmed = user.EmailConfirmed
            },
            rateLimitingData = new {
                requestCount = user.RequestCount,
                rateLimitWindowStart = user.RateLimitWindowStart
            },
            thirdParty = new {
                appleMusicTokenExpiration = user.AppleMusicTokenExpiration
            },
            playlists = playlists.Select( p => new {
                playlistId = p.PlaylistId,
                title = p.Title,
                description = p.Description,
                cardIds = p.CardIds.Split( ',', StringSplitOptions.RemoveEmptyEntries ).ToList( ),
                createdAt = p.CreatedAt,
                url = $"https://{Request.Host}/playlist/{p.PlaylistId}"
            } )
        };

        LogDataDownloaded( user.Id );

        string json = JsonSerializer.Serialize(
            personalData,
            new JsonSerializerOptions { WriteIndented = true }
        );

        return File(
            System.Text.Encoding.UTF8.GetBytes( json ),
            "application/json",
            $"bridgebeats-personal-data-{DateTime.UtcNow:yyyy-MM-dd}.json"
        );
    }

    /// <summary>
    /// Deletes the authenticated user's account and all associated data.
    /// This action is permanent and cannot be undone.
    /// </summary>
    /// <returns>Confirmation of account deletion.</returns>
    /// <response code="200">Account deleted successfully.</response>
    /// <response code="401">User not authenticated.</response>
    /// <response code="400">Failed to delete account.</response>
    [Authorize]
    [HttpPost]
    [Route( "account/delete" )]
    public async Task<IActionResult> DeleteAccount( ) {
        ApplicationUser? user = await userManager.GetUserAsync( User );
        if (user == null) {
            return Unauthorized( );
        }

        // Sign out the user first
        await signInManager.SignOutAsync( );

        // Delete the user
        IdentityResult result = await userManager.DeleteAsync( user );

        if (!result.Succeeded) {
            LogDeleteFailed( user.Id );
            return BadRequest( new { message = "Failed to delete account. Please try again." } );
        }

        LogAccountDeleted( user.Id );

        return Ok( new {
            message = "Account deleted successfully. All your personal data has been removed."
        } );
    }

    #region ATProto OAuth

    /// <summary>
    /// Starts the ATProto OAuth login flow.
    /// Redirects the user to their authorization server for authentication.
    /// </summary>
    /// <param name="request">The login request containing the ATProto handle.</param>
    /// <param name="atProtoOAuth">The ATProto OAuth service.</param>
    /// <returns>Redirect to authorization server or error.</returns>
    [HttpPost]
    [Route( "account/login-atproto" )]
    public async Task<IActionResult> LoginWithAtProto(
        [FromBody] AtProtoLoginRequest request,
        [FromServices] IATProtoOAuthService? atProtoOAuth
    ) {
        if (atProtoOAuth is null) {
            return BadRequest( new { message = "ATProto OAuth is not configured on this server." } );
        }

        if (string.IsNullOrWhiteSpace( request.Handle )) {
            return BadRequest( new { message = "Please enter your Bluesky handle." } );
        }

        try {
            // Build the callback URL
            Uri callbackUri = new(
                $"{Request.Scheme}://{Request.Host}/account/atproto-callback"
            );

            (Uri authUrl, string state) = await atProtoOAuth.StartAuthorizationAsync(
                request.Handle,
                callbackUri
            );

            LogAtProtoOAuthStarted( request.Handle, authUrl.Host );

            // Return the authorization URL for the client to redirect to
            return Ok( new {
                authorizationUrl = authUrl.ToString( ),
                state
            } );
        } catch (Exception ex) {
            LogAtProtoOAuthStartFailed( ex, request.Handle );
            return BadRequest( new {
                message = $"Failed to start login: {ex.Message}"
            } );
        }
    }

    /// <summary>
    /// Handles the OAuth callback from the ATProto authorization server.
    /// </summary>
    /// <param name="code">The authorization code.</param>
    /// <param name="state">The state parameter for verification.</param>
    /// <param name="iss">The issuer (authorization server) URL.</param>
    /// <param name="error">Error code if authorization failed.</param>
    /// <param name="error_description">Error description if authorization failed.</param>
    /// <param name="atProtoOAuth">The ATProto OAuth service.</param>
    /// <returns>Redirect to home page on success, or error page on failure.</returns>
    [HttpGet]
    [Route( "account/atproto-callback" )]
    public async Task<IActionResult> AtProtoCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? iss,
        [FromQuery] string? error,
        [FromQuery] string? error_description,
        [FromServices] IATProtoOAuthService? atProtoOAuth
    ) {
        // Handle authorization errors
        if (!string.IsNullOrEmpty( error )) {
            LogAtProtoOAuthError( error, error_description );
            return RedirectToAction( nameof( LoginPage ), new {
                error = error_description ?? error
            } );
        }

        if (atProtoOAuth is null) {
            return RedirectToAction( nameof( LoginPage ), new {
                error = "ATProto OAuth is not configured on this server."
            } );
        }

        if (string.IsNullOrEmpty( code ) || string.IsNullOrEmpty( state ) || string.IsNullOrEmpty( iss )) {
            return RedirectToAction( nameof( LoginPage ), new {
                error = "Invalid OAuth callback parameters."
            } );
        }

        try {
            // Complete the OAuth flow
            ATProtoOAuthResult result = await atProtoOAuth.CompleteAuthorizationAsync( state, code, iss );

            // Find or create the user
            ApplicationUser? user = await userManager.Users
                .FirstOrDefaultAsync( u => u.AtProtoDid == result.Did );

            if (user is null) {
                // Create a new user for this ATProto account
                user = new ApplicationUser {
                    UserName = result.Handle,
                    AtProtoDid = result.Did,
                    AtProtoHandle = result.Handle,
                    AtProtoAccessToken = result.AccessToken,
                    AtProtoRefreshToken = result.RefreshToken,
                    EncryptedAtProtoDPoPKey = result.DPoPKeyJwk,
                    AtProtoTokenExpiration = result.TokenExpiration,
                    CreatedAt = DateTime.UtcNow
                };

                IdentityResult createResult = await userManager.CreateAsync( user );
                if (!createResult.Succeeded) {
                    string errors = string.Join( ", ", createResult.Errors.Select( e => e.Description ) );
                    LogAtProtoUserCreateFailed( errors );
                    return RedirectToAction( nameof( LoginPage ), new {
                        error = "Failed to create account. Please try again."
                    } );
                }

                LogAtProtoUserCreated( result.Did, result.Handle );
            } else {
                // Update tokens for existing user
                user.AtProtoHandle = result.Handle;
                user.AtProtoAccessToken = result.AccessToken;
                user.AtProtoRefreshToken = result.RefreshToken;
                user.EncryptedAtProtoDPoPKey = result.DPoPKeyJwk;
                user.AtProtoTokenExpiration = result.TokenExpiration;

                _ = await userManager.UpdateAsync( user );

                LogAtProtoTokensUpdated( result.Did, result.Handle );
            }

            // Sign in the user
            await signInManager.SignInAsync( user, isPersistent: false );

            LogAtProtoLoggedIn( user.Id );

            return Redirect( "/" );
        } catch (Exception ex) {
            LogAtProtoCallbackFailed( ex );
            return RedirectToAction( nameof( LoginPage ), new {
                error = $"Login failed: {ex.Message}"
            } );
        }
    }

    #endregion
}
