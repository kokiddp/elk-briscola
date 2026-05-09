using Briscola.Application.Persistence;

namespace Briscola.Application.Ports;

public interface IChatRepository
{
    Task AppendAsync(ChatMessageRecord m, CancellationToken ct);
    Task<IReadOnlyList<ChatMessageRecord>> ReadAsync(ChatScope scope, Guid? gameId, int take, CancellationToken ct);
}
