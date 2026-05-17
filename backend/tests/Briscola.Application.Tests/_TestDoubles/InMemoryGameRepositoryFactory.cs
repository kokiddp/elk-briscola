using Briscola.Application.Ports;
using Briscola.Application.Ranking;

namespace Briscola.Application.Tests.TestDoubles;

/// <summary>
/// Hands out the same singleton in-memory repo on every Create() call.
/// The wrapping scope's Dispose is a no-op — the in-memory store has no
/// per-call lifetime concerns.
/// </summary>
internal sealed class InMemoryGameRepositoryFactory : IGameRepositoryFactory
{
    private readonly InMemoryGameRepository _repo;
    private readonly RankingService _ranking;

    public InMemoryGameRepositoryFactory(InMemoryGameRepository repo, RankingService ranking)
    {
        _repo = repo;
        _ranking = ranking;
    }

    public IGameRepositoryScope Create() => new Scope(_repo, _ranking);

    private sealed class Scope : IGameRepositoryScope
    {
        public Scope(InMemoryGameRepository repo, RankingService ranking)
        {
            Repository = repo;
            Ranking = ranking;
        }

        public IGameRepository Repository { get; }

        public RankingService Ranking { get; }

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
