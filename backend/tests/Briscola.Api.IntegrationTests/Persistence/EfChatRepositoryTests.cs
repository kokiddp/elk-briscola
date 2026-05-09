using Briscola.Application.Persistence;
using Briscola.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Briscola.Api.IntegrationTests.Persistence;

public sealed class EfChatRepositoryTests : IDisposable
{
    private readonly SqliteRepositoryFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    [Fact]
    public async Task Append_then_Read_returns_messages_newest_first()
    {
        var gameId = Guid.NewGuid();
        var userA = Guid.NewGuid();

        using (var scope = _fx.NewScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<EfChatRepository>();
            await repo.AppendAsync(new ChatMessageRecord(
                Guid.NewGuid(), ChatScope.Game, gameId, userA, "first", _fx.Clock.UtcNow),
                CancellationToken.None);
            _fx.Clock.Advance(TimeSpan.FromSeconds(1));
            await repo.AppendAsync(new ChatMessageRecord(
                Guid.NewGuid(), ChatScope.Game, gameId, userA, "second", _fx.Clock.UtcNow),
                CancellationToken.None);
        }

        using var read = _fx.NewScope();
        var messages = await read.ServiceProvider.GetRequiredService<EfChatRepository>()
            .ReadAsync(ChatScope.Game, gameId, take: 10, CancellationToken.None);
        messages.Select(m => m.Text).Should().Equal("second", "first");
    }

    [Fact]
    public async Task Read_with_lobby_scope_ignores_game_messages()
    {
        var gameId = Guid.NewGuid();
        var userA = Guid.NewGuid();

        using (var scope = _fx.NewScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<EfChatRepository>();
            await repo.AppendAsync(new ChatMessageRecord(
                Guid.NewGuid(), ChatScope.Game, gameId, userA, "in-game", _fx.Clock.UtcNow),
                CancellationToken.None);
            await repo.AppendAsync(new ChatMessageRecord(
                Guid.NewGuid(), ChatScope.Lobby, GameId: null, userA, "in-lobby", _fx.Clock.UtcNow),
                CancellationToken.None);
        }

        using var scope2 = _fx.NewScope();
        var lobby = await scope2.ServiceProvider.GetRequiredService<EfChatRepository>()
            .ReadAsync(ChatScope.Lobby, gameId: null, take: 10, CancellationToken.None);
        lobby.Should().ContainSingle().Which.Text.Should().Be("in-lobby");
    }

    [Fact]
    public async Task Read_with_zero_take_returns_empty()
    {
        using var scope = _fx.NewScope();
        var messages = await scope.ServiceProvider.GetRequiredService<EfChatRepository>()
            .ReadAsync(ChatScope.Lobby, null, take: 0, CancellationToken.None);
        messages.Should().BeEmpty();
    }
}
