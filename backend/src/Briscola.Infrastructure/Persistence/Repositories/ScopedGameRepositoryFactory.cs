using Briscola.Application.Ports;
using Briscola.Application.Ranking;
using Microsoft.Extensions.DependencyInjection;

namespace Briscola.Infrastructure.Persistence.Repositories;

/// <summary>
/// Production <see cref="IGameRepositoryFactory"/>. Each
/// <see cref="Create"/> call opens a fresh DI scope and resolves the
/// scoped <see cref="IGameRepository"/> (and the scoped
/// <c>BriscolaDbContext</c> backing it) from inside that scope. The
/// returned <see cref="IGameRepositoryScope"/> disposes the scope when
/// the caller's <c>using</c> block ends.
/// </summary>
public sealed class ScopedGameRepositoryFactory : IGameRepositoryFactory
{
    private readonly IServiceScopeFactory _scopes;

    public ScopedGameRepositoryFactory(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    public IGameRepositoryScope Create()
    {
        IServiceScope scope = _scopes.CreateScope();
        IGameRepository repo = scope.ServiceProvider.GetRequiredService<IGameRepository>();
        RankingService ranking = scope.ServiceProvider.GetRequiredService<RankingService>();
        return new ServiceScopedGameRepository(scope, repo, ranking);
    }

    private sealed class ServiceScopedGameRepository : IGameRepositoryScope
    {
        private readonly IServiceScope _scope;
        private bool _disposed;

        public ServiceScopedGameRepository(IServiceScope scope, IGameRepository repository, RankingService ranking)
        {
            _scope = scope;
            Repository = repository;
            Ranking = ranking;
        }

        public IGameRepository Repository { get; }

        public RankingService Ranking { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _scope.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            if (_scope is IAsyncDisposable async)
            {
                return async.DisposeAsync();
            }

            _scope.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
