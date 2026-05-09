using System.Diagnostics.CodeAnalysis;
using Briscola.Application.Persistence;

namespace Briscola.Infrastructure.Persistence.Entities;

[ExcludeFromCodeCoverage]
public sealed class ChatMessageEntity
{
    public Guid Id { get; set; }
    public ChatScope Scope { get; set; }
    public Guid? GameId { get; set; }
    public Guid UserId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
