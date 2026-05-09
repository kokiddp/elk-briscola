using Briscola.Application.Persistence;
using Briscola.Application.Ports;

namespace Briscola.Application.Tests.TestDoubles;

internal sealed class InMemoryChatRepository : IChatRepository
{
    private readonly List<ChatMessageRecord> _messages = [];

    public Task AppendAsync(ChatMessageRecord m, CancellationToken ct)
    {
        _messages.Add(m);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ChatMessageRecord>> ReadAsync(
        ChatScope scope,
        Guid? gameId,
        int take,
        CancellationToken ct)
    {
        IReadOnlyList<ChatMessageRecord> messages = _messages
            .Where(m => m.Scope == scope && m.GameId == gameId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(take)
            .ToArray();
        return Task.FromResult(messages);
    }
}
