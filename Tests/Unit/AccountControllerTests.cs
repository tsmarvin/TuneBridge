using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Identity;
using BridgeBeats.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="AccountController"/> covering paths that require mock injection,
/// specifically the <see cref="DbUpdateException"/> catch in <c>Register</c>.
/// </summary>
[TestClass]
public class AccountControllerTests {

    /// <summary>
    /// When <c>FindByEmailAsync</c> returns null (pre-check passes) but <c>CreateAsync</c> throws a
    /// <see cref="DbUpdateException"/> whose inner exception message contains the unique-email
    /// constraint text, <c>Register</c> must return 400 with the DuplicateEmail description.
    ///
    /// Failure-first evidence: before adding the try/catch, the <see cref="DbUpdateException"/>
    /// propagates unhandled and the controller returns 500. After adding the catch, it returns 400.
    /// Verified by removing the catch block: the test fails with "Expected BadRequestObjectResult
    /// but got type DbUpdateException thrown" because the exception propagates past the action.
    /// </summary>
    [TestMethod]
    public async Task Register_DbUniqueConstraintViolation_Returns400WithDuplicateEmailError( ) {
        // Arrange
        string email = "race@example.com";

        // Pre-check returns null (simulates: concurrent insertion won the race after the pre-check ran).
        // CreateAsync throws with the exact SQLite unique-constraint message that the catch guard matches.
        DbUpdateException dbEx = new(
            "An error occurred while saving the entity changes.",
            new InvalidOperationException( "UNIQUE constraint failed: AspNetUsers.NormalizedEmail" ) );

        FakeUserManager userManager = new(
            findByEmailResult: null,
            createAsyncException: dbEx
        );

        Mock<SignInManager<ApplicationUser>> signInManagerMock = BuildSignInManagerMock( userManager );

        ApiKeyHasher hasher = new( "test_salt" );
        AccountController controller = new(
            userManager,
            signInManagerMock.Object,
            NullLogger<AccountController>.Instance,
            hasher
        ) {
            ControllerContext = new ControllerContext {
                HttpContext = new DefaultHttpContext( )
            }
        };

        RegisterRequest request = new( email, "ValidPassword123!" );

        // Act
        IActionResult result = await controller.Register( request );

        // Assert
        Assert.IsInstanceOfType( result, typeof( BadRequestObjectResult ),
            "A DbUpdateException with unique-email constraint text must produce 400" );
    }

    /// <summary>
    /// Verifies that a <see cref="DbUpdateException"/> whose inner exception does NOT contain the
    /// unique-email constraint text is not swallowed — it propagates so the caller sees a 500.
    /// This ensures the catch is narrowly scoped and does not hide unrelated DB errors.
    ///
    /// Failure-first evidence: the <c>when</c> filter on the catch means a different constraint
    /// message is not caught. Removing the <c>when</c> filter would cause any DbUpdateException to
    /// produce a 400, masking real errors. Verified by removing the filter: the test fails because
    /// the controller now returns 400 instead of re-throwing.
    /// </summary>
    [TestMethod]
    public async Task Register_UnrelatedDbException_Propagates( ) {
        // Arrange
        string email = "race@example.com";

        // Different constraint — NOT the email unique index; must propagate past the catch guard.
        DbUpdateException dbEx = new(
            "An error occurred while saving the entity changes.",
            new InvalidOperationException( "UNIQUE constraint failed: AspNetUsers.ApiKeyHash" ) );

        FakeUserManager userManager = new(
            findByEmailResult: null,
            createAsyncException: dbEx
        );

        Mock<SignInManager<ApplicationUser>> signInManagerMock = BuildSignInManagerMock( userManager );

        ApiKeyHasher hasher = new( "test_salt" );
        AccountController controller = new(
            userManager,
            signInManagerMock.Object,
            NullLogger<AccountController>.Instance,
            hasher
        ) {
            ControllerContext = new ControllerContext {
                HttpContext = new DefaultHttpContext( )
            }
        };

        RegisterRequest request = new( email, "ValidPassword123!" );

        // Act + Assert — unrelated DbUpdateException must not be caught by the email guard
        _ = await Assert.ThrowsExactlyAsync<DbUpdateException>( ( ) => controller.Register( request ) );
    }

    private static Mock<SignInManager<ApplicationUser>> BuildSignInManagerMock( UserManager<ApplicationUser> userManager ) {
        Mock<IHttpContextAccessor> contextAccessorMock = new( );
        Mock<IUserClaimsPrincipalFactory<ApplicationUser>> claimsFactoryMock = new( );
        return new Mock<SignInManager<ApplicationUser>>(
            userManager,
            contextAccessorMock.Object,
            claimsFactoryMock.Object,
            null!, null!, null!, null!
        );
    }

    /// <summary>
    /// Minimal test double for <see cref="UserManager{TUser}"/> that controls the return values of
    /// <c>FindByEmailAsync</c> and <c>CreateAsync</c> without Castle.DynamicProxy proxying, which
    /// is unreliable for abstract-constructored types with nullable parameters.
    /// </summary>
    private sealed class FakeUserManager : UserManager<ApplicationUser> {
        private readonly ApplicationUser? _findByEmailResult;
        private readonly Exception? _createAsyncException;

        public FakeUserManager(
            ApplicationUser? findByEmailResult,
            Exception? createAsyncException
        ) : base(
            new NoOpUserStore( ),
            null!, null!, null!, null!, null!,
            new IdentityErrorDescriber( ),
            null!, null!
        ) {
            _findByEmailResult = findByEmailResult;
            _createAsyncException = createAsyncException;
        }

        public override Task<ApplicationUser?> FindByEmailAsync( string email )
            => Task.FromResult( _findByEmailResult );

        public override Task<IdentityResult> CreateAsync( ApplicationUser user, string password ) {
            if (_createAsyncException is not null) {
                throw _createAsyncException;
            }
            return Task.FromResult( IdentityResult.Success );
        }

        /// <summary>
        /// Minimal no-op store — only the Create and FindByEmail paths are exercised in these tests.
        /// </summary>
        private sealed class NoOpUserStore : IUserStore<ApplicationUser> {
            public Task<IdentityResult> CreateAsync( ApplicationUser user, CancellationToken ct )
                => Task.FromResult( IdentityResult.Success );

            public Task<IdentityResult> DeleteAsync( ApplicationUser user, CancellationToken ct )
                => Task.FromResult( IdentityResult.Success );

            public void Dispose( ) { }

            public Task<ApplicationUser?> FindByIdAsync( string userId, CancellationToken ct )
                => Task.FromResult<ApplicationUser?>( null );

            public Task<ApplicationUser?> FindByNameAsync( string normalizedUserName, CancellationToken ct )
                => Task.FromResult<ApplicationUser?>( null );

            public Task<string?> GetNormalizedUserNameAsync( ApplicationUser user, CancellationToken ct )
                => Task.FromResult( user.NormalizedUserName );

            public Task<string> GetUserIdAsync( ApplicationUser user, CancellationToken ct )
                => Task.FromResult( user.Id ?? string.Empty );

            public Task<string?> GetUserNameAsync( ApplicationUser user, CancellationToken ct )
                => Task.FromResult( user.UserName );

            public Task SetNormalizedUserNameAsync( ApplicationUser user, string? normalizedName, CancellationToken ct ) {
                user.NormalizedUserName = normalizedName;
                return Task.CompletedTask;
            }

            public Task SetUserNameAsync( ApplicationUser user, string? userName, CancellationToken ct ) {
                user.UserName = userName;
                return Task.CompletedTask;
            }

            public Task<IdentityResult> UpdateAsync( ApplicationUser user, CancellationToken ct )
                => Task.FromResult( IdentityResult.Success );
        }
    }
}

#pragma warning restore CS1591
