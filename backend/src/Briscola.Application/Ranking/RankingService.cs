using Briscola.Application.Persistence;
using Briscola.Application.Ports;
using Briscola.Domain.Primitives;

namespace Briscola.Application.Ranking;

public sealed class RankingService(IRankingRepository rankings, IClock clock)
{
    private const int K = 24;
    private const int DefaultElo = 1500;

    public async Task ApplyResultAsync(GameRecord game, GameResultRecord result, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(result);

        if (await rankings.HasProcessedGameAsync(result.GameId, ct).ConfigureAwait(false))
        {
            return;
        }

        if (game.Mode == GameMode.TwoPlayer)
        {
            await ApplyTwoPlayerAsync(game, result, ct).ConfigureAwait(false);
        }
        else
        {
            await ApplyFourPlayerAsync(game, result, ct).ConfigureAwait(false);
        }

        await rankings.MarkProcessedGameAsync(result.GameId, ct).ConfigureAwait(false);
    }

    private async Task ApplyTwoPlayerAsync(GameRecord game, GameResultRecord result, CancellationToken ct)
    {
        Guid playerZero = RequiredUser(game, 0);
        Guid playerOne = RequiredUser(game, 1);
        RankingRecord a = await rankings.GetAsync(playerZero, ct).ConfigureAwait(false);
        RankingRecord b = await rankings.GetAsync(playerOne, ct).ConfigureAwait(false);

        double scoreA = ScoreFor(result, sideKey: 0);
        int delta = Delta(a.Elo, b.Elo, scoreA);

        await rankings.UpdateAsync(Updated(a, delta, scoreA), ct).ConfigureAwait(false);
        await rankings.UpdateAsync(Updated(b, -delta, 1 - scoreA), ct).ConfigureAwait(false);
    }

    private async Task ApplyFourPlayerAsync(GameRecord game, GameResultRecord result, CancellationToken ct)
    {
        Guid[] teamZero = [RequiredUser(game, 0), RequiredUser(game, 2)];
        Guid[] teamOne = [RequiredUser(game, 1), RequiredUser(game, 3)];
        RankingRecord[] zeroRecords = await LoadManyAsync(teamZero, ct).ConfigureAwait(false);
        RankingRecord[] oneRecords = await LoadManyAsync(teamOne, ct).ConfigureAwait(false);

        double zeroAverage = zeroRecords.Average(static r => r.Elo);
        double oneAverage = oneRecords.Average(static r => r.Elo);
        double scoreZero = ScoreFor(result, sideKey: 0);
        int delta = Delta(zeroAverage, oneAverage, scoreZero);

        foreach (RankingRecord record in zeroRecords)
        {
            await rankings.UpdateAsync(Updated(record, delta, scoreZero), ct).ConfigureAwait(false);
        }

        foreach (RankingRecord record in oneRecords)
        {
            await rankings.UpdateAsync(Updated(record, -delta, 1 - scoreZero), ct).ConfigureAwait(false);
        }
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
