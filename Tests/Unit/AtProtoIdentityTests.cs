using BridgeBeats.Core.Infrastructure.Extensions;
using BridgeBeats.Core.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests that ASP.NET Identity is configured to accept ATProto-authenticated users, which carry no
/// email address, by disabling the <c>RequireUniqueEmail</c> validator.
/// </summary>
/// <remarks>
/// ATProto accounts are keyed by DID rather than email, so a null email must be a valid user. Email
/// uniqueness for first-party password accounts is enforced separately by the DB unique index and the
/// account controller's register action, not by Identity's own validator. These tests pin both the
/// post-fix behavior (null email succeeds) and the pre-fix behavior (uniqueness on forces rejection),
/// and assert the <c>AddBridgeBeatsIdentity</c> registration wires the option off.
/// </remarks>
[TestClass]
public class AtProtoIdentityTests {

    /// <summary>
    /// Verifies that, with <c>RequireUniqueEmail</c> disabled, creating an ATProto user (DID set, email
    /// null) through the user manager succeeds.
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
    /// Verifies the pre-fix configuration as a regression guard: with <c>RequireUniqueEmail</c> turned
    /// on, creating the same null-email ATProto user fails, demonstrating why the option must stay off.
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
    /// Verifies that <see cref="IdentityServiceExtensions.AddBridgeBeatsIdentity"/> registers
    /// <see cref="IdentityOptions"/> with <c>User.RequireUniqueEmail</c> set to <see langword="false"/>,
    /// so the application's own DI wiring permits null-email ATProto users.
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
    /// In-memory <see cref="IUserStore{TUser}"/> (with <see cref="IUserEmailStore{TUser}"/> support)
    /// backing the user manager under test, so user creation and email handling run without a database.
    /// Implements IUserEmailStore so that validators using RequireUniqueEmail can access email.
    /// </summary>
    private sealed class InMemoryUserStore : IUserStore<ApplicationUser>, IUserEmailStore<ApplicationUser> {
        /// <summary>Backing map of created users keyed by generated user id.</summary>
        private readonly Dictionary<string, ApplicationUser> _users = [];

        /// <summary>Assigns a new id to the user, stores it in the in-memory map, and reports success.</summary>
        /// <param name="user">The user to create.</param>
        /// <param name="cancellationToken">A cancellation token (unused).</param>
        /// <returns>A successful <see cref="IdentityResult"/>.</returns>
        public Task<IdentityResult> CreateAsync( ApplicationUser user, CancellationToken cancellationToken ) {
            user.Id = Guid.NewGuid( ).ToString( );
            _users[user.Id] = user;
            return Task.FromResult( IdentityResult.Success );
        }

        /// <summary>Reports a successful delete without removing anything from the map.</summary>
        /// <param name="user">The user to delete (unused).</param>
        /// <param name="cancellationToken">A cancellation token (unused).</param>
        /// <returns>A successful <see cref="IdentityResult"/>.</returns>
        public Task<IdentityResult> DeleteAsync( ApplicationUser user, CancellationToken cancellationToken )
            => Task.FromResult( IdentityResult.Success );

        /// <summary>No-op; the store holds no disposable resources.</summary>
        public void Dispose( ) { }

        /// <summary>Looks up a stored user by id.</summary>
        /// <param name="userId">The user id to find.</param>
        /// <param name="cancellationToken">A cancellation token (unused).</param>
        /// <returns>The matching user, or <see langword="null"/> when absent.</returns>
        public Task<ApplicationUser?> FindByIdAsync( string userId, CancellationToken cancellationToken )
            => Task.FromResult( _users.TryGetValue( userId, out ApplicationUser? u ) ? u : null );

        /// <summary>Looks up a stored user by normalized user name.</summary>
        /// <param name="normalizedUserName">The normalized user name to match.</param>
        /// <param name="cancellationToken">A cancellation token (unused).</param>
        /// <returns>The first matching user, or <see langword="null"/> when none match.</returns>
        public Task<ApplicationUser?> FindByNameAsync( string normalizedUserName, CancellationToken cancellationToken )
            => Task.FromResult( _users.Values.FirstOrDefault( u => u.NormalizedUserName == normalizedUserName ) );

        /// <summary>Returns the user's normalized user name.</summary>
        /// <param name="user">The user to read.</param>
        /// <param name="cancellationToken">A cancellation token (unused).</param>
        /// <returns>The normalized user name.</returns>
        public Task<string?> GetNormalizedUserNameAsync( ApplicationUser user, CancellationToken cancellationToken )
            => Task.FromResult( user.NormalizedUserName );

        /// <summary>Returns the user's id, or an empty string when unset.</summary>
        /// <param name="user">The user to read.</param>
        /// <param name="cancellationToken">A cancellation token (unused).</param>
        /// <returns>The user's id, or <see cref="string.Empty"/> when null.</returns>
        public Task<string> GetUserIdAsync( ApplicationUser user, CancellationToken cancellationToken )
            => Task.FromResult( user.Id ?? string.Empty );

        /// <summary>Returns the user's user name.</summary>
        /// <param name="user">The user to read.</param>
        /// <param name="cancellationToken">A cancellation token (unused).</param>
        /// <returns>The user name.</returns>
        public Task<string?> GetUserNameAsync( ApplicationUser user, CancellationToken cancellationToken )
            => Task.FromResult( user.UserName );

        /// <summary>Stores the normalized user name on the user instance.</summary>
        /// <param name="user">The user to mutate.</param>
        /// <param name="normalizedName">The normalized name to assign.</param>
        /// <param name="cancellationToken">A cancellation token (unused).</param>
        /// <returns>A completed task.</returns>
        public Task SetNormalizedUserNameAsync( ApplicationUser user, string? normalizedName, CancellationToken cancellationToken ) {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        /// <summary>Stores the user name on the user instance.</summary>
        /// <param name="user">The user to mutate.</param>
        /// <param name="userName">The user name to assign.</param>
        /// <param name="cancellationToken">A cancellation token (unused).</param>
        /// <returns>A completed task.</returns>
        public Task SetUserNameAsync( ApplicationUser user, string? userName, CancellationToken cancellationToken ) {
            user.UserName = userName;
            return Task.CompletedTask;
        }

        /// <summary>Reports a successful update without persisting anything.</summary>
        /// <param name="user">The user to update (unused).</param>
        /// <param name="cancellationToken">A cancellation token (unused).</param>
        /// <returns>A successful <see cref="IdentityResult"/>.</returns>
        public Task<IdentityResult> UpdateAsync( ApplicationUser user, CancellationToken cancellationToken )
            => Task.FromResult( IdentityResult.Success );

        // IUserEmailStore members
        /// <summary>Returns the user's email (which may be null for ATProto users).</summary>
        /// <param name="user">The user to read.</param>
        /// <param name="cancellationToken">A cancellation token (unused).</param>
        /// <returns>The email, or <see langword="null"/>.</returns>
        public Task<string?> GetEmailAsync( ApplicationUser user, CancellationToken cancellationToken )
            => Task.FromResult( user.Email );

        /// <summary>Returns whether the user's email is confirmed.</summary>
        /// <param name="user">The user to read.</param>
        /// <param name="cancellationToken">A cancellation token (unused).</param>
        /// <returns>The email-confirmed flag.</returns>
        public Task<bool> GetEmailConfirmedAsync( ApplicationUser user, CancellationToken cancellationToken )
            => Task.FromResult( user.EmailConfirmed );

        /// <summary>Looks up a stored user by normalized email.</summary>
        /// <param name="normalizedEmail">The normalized email to match.</param>
        /// <param name="cancellationToken">A cancellation token (unused).</param>
        /// <returns>The first matching user, or <see langword="null"/> when none match.</returns>
        public Task<ApplicationUser?> FindByEmailAsync( string normalizedEmail, CancellationToken cancellationToken )
            => Task.FromResult( _users.Values.FirstOrDefault( u => u.NormalizedEmail == normalizedEmail ) );

        /// <summary>Returns the user's normalized email.</summary>
        /// <param name="user">The user to read.</param>
        /// <param name="cancellationToken">A cancellation token (unused).</param>
        /// <returns>The normalized email, or <see langword="null"/>.</returns>
        public Task<string?> GetNormalizedEmailAsync( ApplicationUser user, CancellationToken cancellationToken )
            => Task.FromResult( user.NormalizedEmail );

        /// <summary>Stores the email on the user instance.</summary>
        /// <param name="user">The user to mutate.</param>
        /// <param name="email">The email to assign (may be null).</param>
        /// <param name="cancellationToken">A cancellation token (unused).</param>
        /// <returns>A completed task.</returns>
        public Task SetEmailAsync( ApplicationUser user, string? email, CancellationToken cancellationToken ) {
            user.Email = email;
            return Task.CompletedTask;
        }

        /// <summary>Stores the email-confirmed flag on the user instance.</summary>
        /// <param name="user">The user to mutate.</param>
        /// <param name="confirmed">The confirmation flag to assign.</param>
        /// <param name="cancellationToken">A cancellation token (unused).</param>
        /// <returns>A completed task.</returns>
        public Task SetEmailConfirmedAsync( ApplicationUser user, bool confirmed, CancellationToken cancellationToken ) {
            user.EmailConfirmed = confirmed;
            return Task.CompletedTask;
        }

        /// <summary>Stores the normalized email on the user instance.</summary>
        /// <param name="user">The user to mutate.</param>
        /// <param name="normalizedEmail">The normalized email to assign (may be null).</param>
        /// <param name="cancellationToken">A cancellation token (unused).</param>
        /// <returns>A completed task.</returns>
        public Task SetNormalizedEmailAsync( ApplicationUser user, string? normalizedEmail, CancellationToken cancellationToken ) {
            user.NormalizedEmail = normalizedEmail;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// <see cref="ILoggerProvider"/> that hands out a logger which discards everything, keeping the
    /// Identity stack quiet during the test.
    /// </summary>
    private sealed class NoOpLoggerProvider : ILoggerProvider {
        /// <summary>Returns the shared no-op logger for any category.</summary>
        /// <param name="categoryName">The logging category (ignored).</param>
        /// <returns>The shared discarding logger.</returns>
        public ILogger CreateLogger( string categoryName ) => NullLogger.Instance;

        /// <summary>No-op; the provider holds no disposable resources.</summary>
        public void Dispose( ) { }

        /// <summary><see cref="ILogger"/> that disables every level and discards all log calls.</summary>
        private sealed class NullLogger : ILogger {
            /// <summary>Shared singleton instance.</summary>
            public static readonly NullLogger Instance = new( );

            /// <summary>Returns no scope.</summary>
            /// <typeparam name="TState">The scope state type.</typeparam>
            /// <param name="state">The scope state (ignored).</param>
            /// <returns>Always <see langword="null"/>.</returns>
            public IDisposable? BeginScope<TState>( TState state ) where TState : notnull => null;

            /// <summary>Reports every level as disabled.</summary>
            /// <param name="logLevel">The level being queried (ignored).</param>
            /// <returns>Always <see langword="false"/>.</returns>
            public bool IsEnabled( LogLevel logLevel ) => false;

            /// <summary>Discards the log entry.</summary>
            /// <typeparam name="TState">The log state type.</typeparam>
            /// <param name="logLevel">The log level (ignored).</param>
            /// <param name="eventId">The event id (ignored).</param>
            /// <param name="state">The log state (ignored).</param>
            /// <param name="exception">The associated exception (ignored).</param>
            /// <param name="formatter">The message formatter (ignored).</param>
            public void Log<TState>( LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter ) { }
        }
    }
}

#pragma warning restore CS1591
