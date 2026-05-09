using System.Diagnostics.CodeAnalysis;

namespace Briscola.Infrastructure.Persistence.Entities;

/// <summary>
/// Single-row marker that drives <c>IRankingRepository.HasProcessedGameAsync</c>
/// / <c>MarkProcessedGameAsync</c>. A row exists iff <see cref="GameId"/>'s
/// result has already been applied to player rankings — required for Elo
/// idempotency per Phase 2's <c>RankingService</c>.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class RankingProcessedGameEntity
{
    public Guid GameId { get; set; }
}
