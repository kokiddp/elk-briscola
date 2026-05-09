using Briscola.Application.Ports;

namespace Briscola.Application.Tests.TestDoubles;

/// <summary>
/// Hands out the same singleton in-memory repo on every Create() call.
/// The wrapping scope's Dispose is a no-op — the in-memory store has no
/// per-call lifetime concerns.
/// </summary>
internal sealed class InMemoryGameRepositoryFactory : IGameRepositoryFactory
{
    private readonly InMemoryGameRepository _repo;

    public InMemoryGameRepositoryFactory(InMemoryGameRepository repo)
    {
        _repo = repo;
    }

    public IGameRepositoryScope Create() => new Scope(_repo);

    private sealed class Scope : IGameRepositoryScope
    {
        public Scope(InMemoryGameRepository repo) => Repository = repo;

        public IGameRepository Repository { get; }

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
