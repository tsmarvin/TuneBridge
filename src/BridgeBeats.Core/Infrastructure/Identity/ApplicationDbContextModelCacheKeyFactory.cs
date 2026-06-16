using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BridgeBeats.Core.Infrastructure.Identity;

/// <summary>
/// A custom <see cref="IModelCacheKeyFactory"/> for <see cref="ApplicationDbContext"/> that
/// incorporates <see cref="ApplicationDbContext.TokenProtectorPresent"/> into the model-cache key.
/// </summary>
/// <remarks>
/// EF Core caches compiled models process-wide, keyed by the factory's return value. Without this
/// factory, a null-protector <see cref="ApplicationDbContext"/> built first (e.g. by the design-time
/// factory or a test harness) pins a no-converter model in the cache; any subsequent protector-bearing
/// context silently reuses that model and loses its value converters. By including the boolean flag in
/// the cache key, protector-absent and protector-present models occupy separate cache entries:
/// <list type="bullet">
/// <item><description>Design-time / no-protector path: key is <c>(ApplicationDbContext, false, true)</c> for design-time or <c>(ApplicationDbContext, false, false)</c> at runtime.</description></item>
/// <item><description>Runtime production path: key is always <c>(ApplicationDbContext, true, false)</c> — exactly one model built and cached, zero per-request overhead.</description></item>
/// </list>
/// This factory is registered intrinsically via <see cref="ApplicationDbContext.OnConfiguring"/> so
/// every construction path is covered without requiring explicit DI call-site registration.
/// </remarks>
internal sealed class ApplicationDbContextModelCacheKeyFactory : IModelCacheKeyFactory {
    /// <summary>
    /// Creates a model-cache key that incorporates the context type, the
    /// <see cref="ApplicationDbContext.TokenProtectorPresent"/> flag, and whether the model is
    /// being built for design-time use.
    /// </summary>
    /// <param name="context">The <see cref="DbContext"/> instance being configured.</param>
    /// <param name="designTime">
    /// <see langword="true"/> when the model is being built for design-time tooling (e.g.
    /// <c>dotnet ef migrations</c>); <see langword="false"/> at runtime.
    /// </param>
    /// <returns>
    /// A value-tuple key that uniquely identifies the model configuration. Two contexts that differ
    /// only in protector presence produce distinct keys and therefore distinct cached models.
    /// </returns>
    public object Create( DbContext context, bool designTime )
        => context is ApplicationDbContext c
            ? (context.GetType( ), c.TokenProtectorPresent, designTime)
            : context.GetType( );
}
