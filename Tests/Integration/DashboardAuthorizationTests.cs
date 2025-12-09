using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BridgeBeats.Domain.Models;
using BridgeBeats.Domain.Types.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BridgeBeats.Tests.Integration;

/// <summary>
/// Integration tests for Aspire Dashboard authorization.
/// </summary>
[TestClass]
public class DashboardAuthorizationTests : IDisposable {
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private const string TestUserEmail = "dashboardtest@example.com";
    private const string TestUserPassword = "Test@Password123!";

    [TestInitialize]
    public void Setup( ) {
        Dictionary<string, string?> configData = new( ) {
            ["BridgeBeats:SpotifyClientId"] = "test",
            ["BridgeBeats:SpotifyClientSecret"] = "test",
            ["BridgeBeats:DiscordToken"] = null, // Explicitly null to prevent Discord service registration
            ["BridgeBeats:IdentityConnectionString"] = $"Data Source=Dashboard_Identity_{Guid.NewGuid():N};Mode=Memory;Cache=Shared",
            ["BridgeBeats:ApiKeySalt"] = "api_key_salt",
            ["BridgeBeats:ATProtoIdentifier"] = "",
            ["BridgeBeats:ATProtoPassword"] = "",
            ["BridgeBeats:LinkCacheConnectionString"] = $"Data Source=Dashboard_LinkCache_{Guid.NewGuid():N};Mode=Memory;Cache=Shared",
        };

        _factory = new DashboardTestFactory( configData );
        _client = _factory.CreateClient( );

        // Note: We don't need to store DbContext for cleanup as the factory will handle database disposal
    }

    [TestCleanup]
    public void Cleanup( ) {
        Dispose( );
    }

    public void Dispose( ) {
        _client?.Dispose( );
        _factory?.Dispose( );
        GC.SuppressFinalize( this );
    }

    /// <summary>
    /// Tests that unauthenticated requests to dashboard authorization endpoint return 401.
    /// </summary>
    [TestMethod]
    public async Task DashboardAuthorize_UnauthenticatedUser_Returns401( ) {
        // Arrange
        using HttpClient client = _factory!.CreateClient( );

        // Act
        HttpResponseMessage response = await client.GetAsync( "/api/dashboard/authorize" );

        // Assert
        Assert.AreEqual( HttpStatusCode.Unauthorized, response.StatusCode );
    }

    /// <summary>
    /// Tests that authenticated users without the AspireDashboardAccess role return 403.
    /// </summary>
    [TestMethod]
    public async Task DashboardAuthorize_UserWithoutRole_Returns403( ) {
        // Arrange
        await CreateTestUserAsync( hasRole: false );
        // Don't set Authorization header - let TestScheme handle authentication automatically

        // Act
        HttpResponseMessage response = await _client!.GetAsync( "/api/dashboard/authorize" );

        // Assert
        Assert.AreEqual( HttpStatusCode.Forbidden, response.StatusCode );
    }

    /// <summary>
    /// Tests that authenticated users with the AspireDashboardAccess role return 200.
    /// </summary>
    [TestMethod]
    public async Task DashboardAuthorize_UserWithRole_Returns200( ) {
        // Arrange
        await CreateTestUserAsync( hasRole: true );
        // Don't set Authorization header - let TestScheme handle authentication automatically

        // Act
        HttpResponseMessage response = await _client.GetAsync( "/api/dashboard/authorize" );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
    }

    /// <summary>
    /// Tests that the AspireDashboardAccess role is created on application startup.
    /// </summary>
    [TestMethod]
    public async Task DatabaseInitialization_CreatesAspireDashboardAccessRole( ) {
        // Arrange
        using IServiceScope scope = _factory!.Services.CreateScope( );
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>( );

        // Act
        bool roleExists = await roleManager.RoleExistsAsync( Roles.AspireDashboardAccess );

        // Assert
        Assert.IsTrue( roleExists );
    }

    /// <summary>
    /// Helper method to create a test user with or without the AspireDashboardAccess role.
    /// </summary>
    private async Task CreateTestUserAsync( bool hasRole ) {
        using IServiceScope scope = _factory!.Services.CreateScope( );
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>( );
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>( );

        // Create role if it doesn't exist
        if (!await roleManager.RoleExistsAsync( Roles.AspireDashboardAccess )) {
            _ = await roleManager.CreateAsync( new IdentityRole( Roles.AspireDashboardAccess ) );
        }

        // Create test user
        ApplicationUser user = new( ) {
            UserName = TestUserEmail,
            Email = TestUserEmail,
            EmailConfirmed = true
        };

        IdentityResult result = await userManager.CreateAsync( user, TestUserPassword );
        if (!result.Succeeded) {
            throw new InvalidOperationException( "Failed to create test user: " + string.Join( ", ", result.Errors.Select( e => e.Description ) ) );
        }

        // Assign role if requested
        if (hasRole) {
            _ = await userManager.AddToRoleAsync( user, Roles.AspireDashboardAccess );
        }
    }

    /// <summary>
    /// Test authentication handler for integration tests.
    /// </summary>
    public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions> {
        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder
        ) : base( options, logger, encoder ) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync( ) {
            Claim[] claims = [
                new Claim( ClaimTypes.Name, TestUserEmail ),
                new Claim( ClaimTypes.Email, TestUserEmail )
            ];
            ClaimsIdentity identity = new( claims, "TestScheme" );
            ClaimsPrincipal principal = new( identity );
            AuthenticationTicket ticket = new( principal, "TestScheme" );

            return Task.FromResult( AuthenticateResult.Success( ticket ) );
        }
    }

    /// <summary>
    /// Custom factory for dashboard tests that includes test authentication.
    /// </summary>
    private class DashboardTestFactory : CustomWebApplicationFactory {
        public DashboardTestFactory( Dictionary<string, string?> configOverrides ) : base( configOverrides ) { }

        protected override void ConfigureWebHost( Microsoft.AspNetCore.Hosting.IWebHostBuilder builder ) {
            // Call base to set up configuration
            base.ConfigureWebHost( builder );

            // Add additional test-specific services
            _ = builder.ConfigureServices( services => {
                // Replace the MultiScheme ForwardDefaultSelector to use our TestScheme
                _ = services.PostConfigure<Microsoft.AspNetCore.Authentication.PolicySchemeOptions>( "MultiScheme", options => {
                    options.ForwardDefaultSelector = context => "TestScheme";
                } );

                // Add test authentication scheme
                _ = services.AddAuthentication( )
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>( "TestScheme", options => { } );
            } );
        }
    }
}
