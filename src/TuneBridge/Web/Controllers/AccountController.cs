using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TuneBridge.Domain.Implementations.Auth;
using TuneBridge.Domain.Models;

namespace TuneBridge.Web.Controllers;

/// <summary>
/// Controller for user authentication and account management.
/// Provides both API endpoints and web UI for user registration, login, and API key generation.
/// </summary>
public class AccountController : Controller {
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<AccountController> _logger;
    private readonly ApiKeyHasher _hasher;

    /// <summary>
    /// Initializes a new instance of the <see cref="AccountController"/> class.
    /// </summary>
    /// <param name="userManager">User manager for ASP.NET Identity.</param>
    /// <param name="signInManager">Sign-in manager for authentication.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="hasher">API key hasher for secure key generation and validation.</param>
    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<AccountController> logger,
        ApiKeyHasher hasher ) {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
        _hasher = hasher;
    }

    /// <summary>Request for user registration.</summary>
    /// <param name="Email">User email address (will be used as unique identifier).</param>
    /// <param name="Password">User password.</param>
    public record RegisterRequest( string Email, string Password );

    /// <summary>Request for user login.</summary>
    /// <param name="Email">User email address.</param>
    /// <param name="Password">User password.</param>
    public record LoginRequest( string Email, string Password );

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
        string hashedApiKey = _hasher.HashApiKey( apiKey );

        ApplicationUser user = new() {
            UserName = request.Email,
            Email = request.Email,
            ApiKeyHash = hashedApiKey,
            CreatedAt = DateTime.UtcNow
        };

        IdentityResult result = await _userManager.CreateAsync( user, request.Password );

        if (!result.Succeeded) {
            foreach (IdentityError error in result.Errors) {
                ModelState.AddModelError( string.Empty, error.Description );
            }
            return BadRequest( ModelState );
        }

        // Sign in the user automatically with a cookie for web UI
        await _signInManager.SignInAsync( user, isPersistent: false );

        _logger.LogInformation( "User registered successfully with ID: {UserId}", user.Id );

        return Ok( new {
            userId = user.Id,
            apiKey = apiKey,
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

        ApplicationUser? user = await _userManager.FindByEmailAsync( request.Email );
        if (user == null) {
            return Unauthorized( new { message = "Invalid email or password" } );
        }

        Microsoft.AspNetCore.Identity.SignInResult result = await _signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: false
        );

        if (!result.Succeeded) {
            return Unauthorized( new { message = "Invalid email or password" } );
        }

        // Sign in the user with a cookie for web UI
        await _signInManager.SignInAsync( user, isPersistent: false );

        // Generate new API key only if user doesn't have one
        string apiKey;
        if (string.IsNullOrEmpty( user.ApiKeyHash )) {
            apiKey = ApiKeyHasher.GenerateApiKey( );
            user.ApiKeyHash = _hasher.HashApiKey( apiKey );
            _ = await _userManager.UpdateAsync( user );
        } else {
            // Return a message that the API key is already set
            // User should use regenerate-api-key endpoint if they need a new one
            apiKey = "***EXISTING_KEY***";
        }

        _logger.LogInformation( "User logged in successfully with ID: {UserId}", user.Id );

        return Ok( new {
            userId = user.Id,
            apiKey = apiKey,
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
        await _signInManager.SignOutAsync( );
        return Ok( new { message = "Logged out successfully" } );
    }

    /// <summary>
    /// Checks if the current user is authenticated (for web UI).
    /// </summary>
    [HttpGet]
    [Route( "account/status" )]
    public async Task<IActionResult> GetAuthStatus( ) {
        if (User.Identity?.IsAuthenticated == true) {
            ApplicationUser? user = await _userManager.GetUserAsync( User );
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
        ApplicationUser? user = await _userManager.GetUserAsync( User );
        if (user == null) {
            return Unauthorized( );
        }

        string apiKey = ApiKeyHasher.GenerateApiKey( );
        user.ApiKeyHash = _hasher.HashApiKey( apiKey );
        IdentityResult result = await _userManager.UpdateAsync( user );

        if (!result.Succeeded) {
            return BadRequest( new { message = "Failed to regenerate API key" } );
        }

        _logger.LogInformation( "User regenerated API key with ID: {UserId}", user.Id );

        return Ok( new {
            apiKey = apiKey,
            message = "API key regenerated successfully. Update your applications with the new key."
        } );
    }

    /// <summary>
    /// Downloads all personal data associated with the authenticated user.
    /// Returns a JSON file containing account information and metadata.
    /// </summary>
    /// <returns>JSON file with personal data.</returns>
    /// <response code="200">Personal data downloaded successfully.</response>
    /// <response code="401">User not authenticated.</response>
    [Authorize]
    [HttpPost]
    [Route( "account/download-data" )]
    public async Task<IActionResult> DownloadPersonalData( ) {
        ApplicationUser? user = await _userManager.GetUserAsync( User );
        if (user == null) {
            return Unauthorized( );
        }

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
            }
        };

        _logger.LogInformation( "User downloaded personal data with ID: {UserId}", user.Id );

        string json = System.Text.Json.JsonSerializer.Serialize(
            personalData,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
        );

        return File(
            System.Text.Encoding.UTF8.GetBytes( json ),
            "application/json",
            $"tunebridge-personal-data-{DateTime.UtcNow:yyyy-MM-dd}.json"
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
        ApplicationUser? user = await _userManager.GetUserAsync( User );
        if (user == null) {
            return Unauthorized( );
        }

        // Sign out the user first
        await _signInManager.SignOutAsync( );

        // Delete the user
        IdentityResult result = await _userManager.DeleteAsync( user );

        if (!result.Succeeded) {
            _logger.LogError( "Failed to delete account for user ID: {UserId}", user.Id );
            return BadRequest( new { message = "Failed to delete account. Please try again." } );
        }

        _logger.LogInformation( "User account deleted with ID: {UserId}", user.Id );

        return Ok( new {
            message = "Account deleted successfully. All your personal data has been removed."
        } );
    }
}
