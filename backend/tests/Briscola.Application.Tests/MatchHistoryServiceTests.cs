using Briscola.Application.History;
using Briscola.Application.Persistence;
using Briscola.Application.Tests.TestDoubles;

namespace Briscola.Application.Tests;

public sealed class MatchHistoryServiceTests
{
    [Fact]
    public async Task Save_result_delegates_to_game_repository()
    {
        InMemoryGameRepository games = new();
        MatchHistoryService service = new(games);
        GameResultRecord result = new(
            Guid.NewGuid(),
            GameOutcomeKind.Draw,
            WinnerKey: null,
            SeatScoresJson: "[60,60]",
            TeamScoresJson: null,
            EndedReason.Normal);

        await service.SaveResultAsync(result, CancellationToken.None);

        games.Results.Should().ContainSingle().Which.Should().Be(result);
    }
}
