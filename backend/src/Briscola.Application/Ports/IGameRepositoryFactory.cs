using Briscola.Application.Ranking;

namespace Briscola.Application.Ports;

/// <summary>
/// Hands out short-lived <see cref="IGameRepository"/> handles. Long-lived
/// callers (the per-game <c>GameRoom</c>, the <c>OpenLobbyJanitor</c>
/// background service) take this factory rather than an
/// <see cref="IGameRepository"/> directly so they don't capture a Scoped
/// DI lifetime — see [AGENTS.md § Dependency injection][1].
///
/// Each <see cref="Create"/> call returns its own
/// <see cref="IGameRepositoryScope"/>; the caller disposes the scope when
/// the unit of work finishes. Production wires this to a service-scope
/// factory; tests use an in-memory pass-through.
///
/// [1]: ../../../../AGENTS.md#dependency-injection
/// </summary>
public interface IGameRepositoryFactory
{
    IGameRepositoryScope Create();
}

/// <summary>
/// Disposable handle around an <see cref="IGameRepository"/>. In
/// production the dispose drops the underlying DI scope (and therefore
/// the EF <c>DbContext</c>). In test doubles the dispose is a no-op.
/// </summary>
public interface IGameRepositoryScope : IAsyncDisposable, IDisposable
{
    IGameRepository Repository { get; }

    /// <summary>
    /// Ranking service resolved from the same DI scope as
    /// <see cref="Repository"/>, so per-match ranking updates can share the
    /// EF <c>DbContext</c> and run alongside <c>SaveResultAsync</c> without
    /// opening a second scope.
    /// </summary>
    RankingService Ranking { get; }
}
