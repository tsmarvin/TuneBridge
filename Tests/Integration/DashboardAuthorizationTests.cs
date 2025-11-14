using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TuneBridge.Domain.Models;
using TuneBridge.Domain.Types.Constants;

namespace TuneBridge.Tests.Integration;

/// <summary>
/// Integration tests for Aspire Dashboard authorization.
/// </summary>
[TestClass]
public class DashboardAuthorizationTests {
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private ApplicationDbContext? _dbContext;
    private const string TestUserEmail = "dashboardtest@example.com";
    private const string TestUserPassword = "Test@Password123!";

    [TestInitialize]
    public void Setup( ) {
        _factory = new WebApplicationFactory<Program>( )
            .WithWebHostBuilder( builder => {
                _ = builder.ConfigureServices( services => {
                    // Replace the database with an in-memory database for testing
                    ServiceDescriptor? descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof( DbContextOptions<ApplicationDbContext> )
                    );

                    if (descriptor != null) {
                        _ = services.Remove( descriptor );
                    }

                    _ = services.AddDbContext<ApplicationDbContext>( options => {
                        _ = options.UseInMemoryDatabase( "InMemoryDbForTesting" );
                    } );

                    // Add test authentication scheme with default scheme set
                    _ = services.AddAuthentication( options => { options.DefaultScheme = "TestScheme"; } )
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>( "TestScheme", options => { } );
                } );
            } );

        _client = _factory.CreateClient( );
        
        // Get and store DbContext for cleanup
        using IServiceScope scope = _factory.Services.CreateScope( );
        _dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>( );
    }

    [TestCleanup]
    public void Cleanup( ) {
        _client?.Dispose( );
        
        // Clean up the in-memory database
        if (_dbContext != null) {
            _dbContext.Database.EnsureDeleted( );
            _dbContext.Dispose( );
        }
        
        _factory?.Dispose( );
    }

    /// <summary>
    /// Tests that unauthenticated requests to dashboard authorization endpoint return 401.
    /// </summary>
    [TestMethod]
    public async Task DashboardAuthorize_UnauthenticatedUser_Returns401( ) {
        // Arrange
        HttpClient client = _factory!.CreateClient( );

        // Act
        HttpResponseMessage response = await client.GetAsync( "/api/dashboard/authorize" );

        // Assert
        _ = response.StatusCode.Should( ).Be( HttpStatusCode.Unauthorized );
    }

    /// <summary>
    /// Tests that authenticated users without the AspireDashboardAccess role return 403.
    /// </summary>
    [TestMethod]
    public async Task DashboardAuthorize_UserWithoutRole_Returns403( ) {
        // Arrange
        await CreateTestUserAsync( hasRole: false );
        _client!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "TestScheme" );

        // Act
        HttpResponseMessage response = await _client.GetAsync( "/api/dashboard/authorize" );

        // Assert
        _ = response.StatusCode.Should( ).Be( HttpStatusCode.Forbidden );
    }

    /// <summary>
    /// Tests that authenticated users with the AspireDashboardAccess role return 200.
    /// </summary>
    [TestMethod]
    public async Task DashboardAuthorize_UserWithRole_Returns200( ) {
        // Arrange
        await CreateTestUserAsync( hasRole: true );
        _client!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "TestScheme" );

        // Act
        HttpResponseMessage response = await _client.GetAsync( "/api/dashboard/authorize" );

        // Assert
        _ = response.StatusCode.Should( ).Be( HttpStatusCode.OK );
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
        _ = roleExists.Should( ).BeTrue( );
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
}
