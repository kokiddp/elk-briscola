using System.Diagnostics.CodeAnalysis;
using Briscola.Application.Persistence;

namespace Briscola.Infrastructure.Persistence.Entities;

[ExcludeFromCodeCoverage]
public sealed class GameResultEntity
{
    public Guid GameId { get; set; }
    public GameOutcomeKind Kind { get; set; }
    public int? WinnerKey { get; set; }
    public string SeatScoresJson { get; set; } = string.Empty;
    public string? TeamScoresJson { get; set; }
    public EndedReason Reason { get; set; }
}
