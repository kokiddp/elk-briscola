using System.Diagnostics.CodeAnalysis;

namespace Briscola.Infrastructure.Persistence.Entities;

[ExcludeFromCodeCoverage]
public sealed class RankingEntity
{
    public Guid UserId { get; set; }
    public int Elo { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public int GamesPlayed { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
