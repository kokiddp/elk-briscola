using Briscola.Application.Persistence;
using Briscola.Application.Ports;
using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Briscola.Infrastructure.Persistence.Repositories;

public sealed class EfChatRepository(BriscolaDbContext db) : IChatRepository
{
    public async Task AppendAsync(ChatMessageRecord m, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(m);
        db.ChatMessages.Add(new ChatMessageEntity
        {
            Id = m.Id,
            Scope = m.Scope,
            GameId = m.GameId,
            UserId = m.UserId,
            Text = m.Text,
            CreatedAt = m.CreatedAt,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChatMessageRecord>> ReadAsync(
        ChatScope scope,
        Guid? gameId,
        int take,
        CancellationToken ct)
    {
        if (take < 1)
        {
            return [];
        }

        var rows = await db.ChatMessages.AsNoTracking()
            .Where(m => m.Scope == scope && m.GameId == gameId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows
            .Select(e => new ChatMessageRecord(e.Id, e.Scope, e.GameId, e.UserId, e.Text, e.CreatedAt))
            .ToList();
    }
}
