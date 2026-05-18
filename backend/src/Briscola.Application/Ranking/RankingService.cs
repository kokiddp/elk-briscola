using Briscola.Application.Persistence;
using Briscola.Application.Ports;
using Briscola.Domain.Primitives;

namespace Briscola.Application.Ranking;

public sealed class RankingService(IRankingRepository rankings, IClock clock)
{
    /// <summary>
    /// K-factor used for both 2p and 4p Elo deltas. In 4p (teams) every
    /// player on the winning team gains +K*(score-expected) and every
    /// loser pays the symmetric -K — so the team-level effective K is 2K.
    /// That's intentional: Elo here tracks the <em>player</em>, not the
    /// team, and we want a 4p win to move an individual rating with the
    /// same magnitude as a 2p win. See [Phase 2 follow-up in TODO.md][1].
    /// [1]: ../../../../TODO.md#phase-2-follow-up-items-deferred-for-later-phases
    /// </summary>
    private const int K = 24;

    private const int DefaultElo = 1500;

    /// <summary>
    /// Applies the post-game Elo deltas. Returns the updated
    /// <see cref="RankingRecord"/>s — one per affected user, in seat-index
    /// order — so callers can publish them downstream (e.g. via SignalR)
    /// without a second round-trip to read what they just wrote. Returns
    /// an empty list when the game has already been processed (idempotent
    /// on <c>result.GameId</c>).
    /// </summary>
    public async Task<IReadOnlyList<RankingRecord>> ApplyResultAsync(
        GameRecord game, GameResultRecord result, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(result);

        if (await rankings.HasProcessedGameAsync(result.GameId, ct).ConfigureAwait(false))
        {
            return [];
        }

        IReadOnlyList<RankingRecord> updated = game.Mode == GameMode.TwoPlayer
            ? await ApplyTwoPlayerAsync(game, result, ct).ConfigureAwait(false)
            : await ApplyFourPlayerAsync(game, result, ct).ConfigureAwait(false);

        await rankings.MarkProcessedGameAsync(result.GameId, ct).ConfigureAwait(false);
        return updated;
    }

    private async Task<IReadOnlyList<RankingRecord>> ApplyTwoPlayerAsync(
        GameRecord game, GameResultRecord result, CancellationToken ct)
    {
        Guid playerZero = RequiredUser(game, 0);
        Guid playerOne = RequiredUser(game, 1);
        RankingRecord a = await rankings.GetAsync(playerZero, ct).ConfigureAwait(false);
        RankingRecord b = await rankings.GetAsync(playerOne, ct).ConfigureAwait(false);

        double scoreA = ScoreFor(result, sideKey: 0);
        int delta = Delta(a.Elo, b.Elo, scoreA);

        RankingRecord nextA = Updated(a, delta, scoreA);
        RankingRecord nextB = Updated(b, -delta, 1 - scoreA);
        await rankings.UpdateAsync(nextA, ct).ConfigureAwait(false);
        await rankings.UpdateAsync(nextB, ct).ConfigureAwait(false);
        return [nextA, nextB];
    }

    private async Task<IReadOnlyList<RankingRecord>> ApplyFourPlayerAsync(
        GameRecord game, GameResultRecord result, CancellationToken ct)
    {
        // Seats are 0..3 in order; teams are {0,2} and {1,3}. We collect
        // results in seat order so the caller can correlate with
        // game.SeatUserIds[i] without a separate lookup.
        Guid[] seatUsers = [
            RequiredUser(game, 0),
            RequiredUser(game, 1),
            RequiredUser(game, 2),
            RequiredUser(game, 3),
        ];
        RankingRecord[] current = await LoadManyAsync(seatUsers, ct).ConfigureAwait(false);
        double zeroAverage = (current[0].Elo + current[2].Elo) / 2.0;
        double oneAverage = (current[1].Elo + current[3].Elo) / 2.0;
        double scoreZero = ScoreFor(result, sideKey: 0);
        int delta = Delta(zeroAverage, oneAverage, scoreZero);

        RankingRecord[] next = new RankingRecord[4];
        next[0] = Updated(current[0], delta, scoreZero);
        next[2] = Updated(current[2], delta, scoreZero);
        next[1] = Updated(current[1], -delta, 1 - scoreZero);
        next[3] = Updated(current[3], -delta, 1 - scoreZero);

        foreach (RankingRecord r in next)
        {
            await rankings.UpdateAsync(r, ct).ConfigureAwait(false);
        }
        return next;
    }

    private async Task<RankingRecord[]> LoadManyAsync(Guid[] userIds, CancellationToken ct)
    {
        List<RankingRecord> records = new(userIds.Length);
        foreach (Guid userId in userIds)
        {
            records.Add(await rankings.GetAsync(userId, ct).ConfigureAwait(false));
        }

        return [.. records];
    }

    private RankingRecord Updated(RankingRecord record, int delta, double score)
    {
        int wins = record.Wins + (score == 1 ? 1 : 0);
        int losses = record.Losses + (score == 0 ? 1 : 0);
        int draws = record.Draws + (score == 0.5 ? 1 : 0);
        return record with
        {
            Elo = record.Elo + delta,
            Wins = wins,
            Losses = losses,
            Draws = draws,
            GamesPlayed = record.GamesPlayed + 1,
            UpdatedAt = clock.UtcNow,
        };
    }

    private static int Delta(double ratingA, double ratingB, double scoreA)
    {
        double expected = 1 / (1 + Math.Pow(10, (ratingB - ratingA) / 400));
        return (int)Math.Round(K * (scoreA - expected), MidpointRounding.AwayFromZero);
    }

    private static double ScoreFor(GameResultRecord result, int sideKey)
    {
        if (result.Kind == GameOutcomeKind.Draw)
        {
            return 0.5;
        }

        return result.WinnerKey == sideKey ? 1 : 0;
    }

    private static Guid RequiredUser(GameRecord game, int seat)
    {
        Guid? userId = game.SeatUserIds[seat];
        return userId ?? throw new InvalidOperationException($"Seat {seat} has no user.");
    }

    public static RankingRecord NewUserRanking(Guid userId, DateTimeOffset now) =>
        new(userId, DefaultElo, Wins: 0, Losses: 0, Draws: 0, GamesPlayed: 0, now);
}
