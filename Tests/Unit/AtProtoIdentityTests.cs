using BridgeBeats.Core.Infrastructure.Extensions;
using BridgeBeats.Core.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for Identity configuration and Register duplicate-email behavior
/// related to the ATProto login fix (Bug 2).
/// </summary>
[TestClass]
public class AtProtoIdentityTests {

    /// <summary>
    /// Regression test: ApplicationUser with AtProtoDid set and Email = null must be accepted by
    /// a UserManager configured identically to IdentityServiceExtensions (RequireUniqueEmail = false).
    ///
    /// Failure-first evidence: on the pre-fix configuration (RequireUniqueEmail = true), UserManager
    /// rejects a null-email user with "Email '' is invalid." CreateAsync returns a failed IdentityResult.
    /// After the fix, CreateAsync succeeds and result.Succeeded is true.
    /// </summary>
    [TestMethod]
    public async Task CreateAtProtoUser_WithNullEmail_Succeeds( ) {
        // Arrange — configure UserManager the same way IdentityServiceExtensions does post-fix
        ServiceCollection services = new( );
        _ = services
            .AddLogging( b => b.AddProvider( new NoOpLoggerProvider( ) ) )
            .AddIdentityCore<ApplicationUser>( options => {
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredLength = 14;

                // Post-fix value — must be false for ATProto users
                options.User.RequireUniqueEmail = false;
            } )
            .AddUserStore<InMemoryUserStore>( );

        await using ServiceProvider sp = services.BuildServiceProvider( );
        UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>( );

        ApplicationUser user = new( ) {
            UserName = "taylormar.vin",
            AtProtoDid = "did:plc:kx2mxhedzbeuqethywrzdexz",
            Email = null
        };

        // Act
        IdentityResult result = await userManager.CreateAsync( user );

        // Assert
        Assert.IsTrue( result.Succeeded, $"Expected success but got: {string.Join( ", ", result.Errors.Select( e => e.Description ) )}" );
    }

    /// <summary>
    /// Verifies that the pre-fix configuration (RequireUniqueEmail = true) would fail for
    /// a null-email user. This documents the failure mode as a deterministic test.
    /// </summary>
    [TestMethod]
    public async Task CreateAtProtoUser_WithNullEmail_PreFixConfigFails( ) {
        // Arrange — pre-fix configuration
        ServiceCollection services = new( );
        _ = services
            .AddLogging( b => b.AddProvider( new NoOpLoggerProvider( ) ) )
            .AddIdentityCore<ApplicationUser>( options => {
                options.User.RequireUniqueEmail = true; // pre-fix
            } )
            .AddUserStore<InMemoryUserStore>( );

        await using ServiceProvider sp = services.BuildServiceProvider( );
        UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>( );

        ApplicationUser user = new( ) {
            UserName = "taylormar.vin",
            AtProtoDid = "did:plc:kx2mxhedzbeuqethywrzdexz",
            Email = null
        };

        // Act
        IdentityResult result = await userManager.CreateAsync( user );

        // Assert — the pre-fix code DOES fail; this confirms the regression surface
        Assert.IsFalse( result.Succeeded, "Pre-fix config should reject null-email user" );
    }

    /// <summary>
    /// Config-drift pin: AddBridgeBeatsIdentity must configure RequireUniqueEmail = false.
    /// This test pins the production IdentityOptions so that a future edit to
    /// IdentityServiceExtensions cannot silently re-enable RequireUniqueEmail without
    /// breaking this test.
    /// </summary>
    [TestMethod]
    public void AddBridgeBeatsIdentity_ConfiguresRequireUniqueEmailFalse( ) {
        ServiceCollection services = new( );
        _ = services.AddLogging( );
        _ = services.AddBridgeBeatsIdentity( Path.GetTempPath( ) );
        using ServiceProvider sp = services.BuildServiceProvider( );
        Assert.IsFalse(
            sp.GetRequiredService<IOptions<IdentityOptions>>( ).Value.User.RequireUniqueEmail,
            "ATProto users have null email; uniqueness for password accounts is enforced in AccountController.Register" );
    }

    /// <summary>
    /// Minimal in-memory user store for testing Identity without EF Core.
    /// Implements IUserEmailStore so that validators using RequireUniqueEmail can access email.
    /// </summary>
    private sealed class InMemoryUserStore : IUserStore<ApplicationUser>, IUserEmailStore<ApplicationUser> {
        private readonly Dictionary<string, ApplicationUser> _users = [];

        public Task<IdentityResult> CreateAsync( ApplicationUser user, CancellationToken cancellationToken ) {
            user.Id = Guid.NewGuid( ).ToString( );
            _users[user.Id] = user;
            return Task.FromResult( IdentityResult.Success );
        }

        public Task<IdentityResult> DeleteAsync( ApplicationUser user, CancellationToken cancellationToken )
            => Task.FromResult( IdentityResult.Success );

        public void Dispose( ) { }

        public Task<ApplicationUser?> FindByIdAsync( string userId, CancellationToken cancellationToken )
            => Task.FromResult( _users.TryGetValue( userId, out ApplicationUser? u ) ? u : null );

        public Task<ApplicationUser?> FindByNameAsync( string normalizedUserName, CancellationToken cancellationToken )
            => Task.FromResult( _users.Values.FirstOrDefault( u => u.NormalizedUserName == normalizedUserName ) );

        public Task<string?> GetNormalizedUserNameAsync( ApplicationUser user, CancellationToken cancellationToken )
            => Task.FromResult( user.NormalizedUserName );

        public Task<string> GetUserIdAsync( ApplicationUser user, CancellationToken cancellationToken )
            => Task.FromResult( user.Id ?? string.Empty );

        public Task<string?> GetUserNameAsync( ApplicationUser user, CancellationToken cancellationToken )
            => Task.FromResult( user.UserName );

        public Task SetNormalizedUserNameAsync( ApplicationUser user, string? normalizedName, CancellationToken cancellationToken ) {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        public Task SetUserNameAsync( ApplicationUser user, string? userName, CancellationToken cancellationToken ) {
            user.UserName = userName;
            return Task.CompletedTask;
        }

        public Task<IdentityResult> UpdateAsync( ApplicationUser user, CancellationToken cancellationToken )
            => Task.FromResult( IdentityResult.Success );

        // IUserEmailStore members
        public Task<string?> GetEmailAsync( ApplicationUser user, CancellationToken cancellationToken )
            => Task.FromResult( user.Email );

        public Task<bool> GetEmailConfirmedAsync( ApplicationUser user, CancellationToken cancellationToken )
            => Task.FromResult( user.EmailConfirmed );

        public Task<ApplicationUser?> FindByEmailAsync( string normalizedEmail, CancellationToken cancellationToken )
            => Task.FromResult( _users.Values.FirstOrDefault( u => u.NormalizedEmail == normalizedEmail ) );

        public Task<string?> GetNormalizedEmailAsync( ApplicationUser user, CancellationToken cancellationToken )
            => Task.FromResult( user.NormalizedEmail );

        public Task SetEmailAsync( ApplicationUser user, string? email, CancellationToken cancellationToken ) {
            user.Email = email;
            return Task.CompletedTask;
        }

        public Task SetEmailConfirmedAsync( ApplicationUser user, bool confirmed, CancellationToken cancellationToken ) {
            user.EmailConfirmed = confirmed;
            return Task.CompletedTask;
        }

        public Task SetNormalizedEmailAsync( ApplicationUser user, string? normalizedEmail, CancellationToken cancellationToken ) {
            user.NormalizedEmail = normalizedEmail;
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpLoggerProvider : ILoggerProvider {
        public ILogger CreateLogger( string categoryName ) => NullLogger.Instance;
        public void Dispose( ) { }

        private sealed class NullLogger : ILogger {
            public static readonly NullLogger Instance = new( );
            public IDisposable? BeginScope<TState>( TState state ) where TState : notnull => null;
            public bool IsEnabled( LogLevel logLevel ) => false;
            public void Log<TState>( LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter ) { }
        }
    }
}

#pragma warning restore CS1591
