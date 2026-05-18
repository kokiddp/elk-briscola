using System.Net;
using System.Net.Http.Json;
using Briscola.Api.Dtos;
using Briscola.Application.Background;
using Briscola.Application.Configuration;
using Briscola.Application.Ports;
using Briscola.Domain.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Briscola.Api.IntegrationTests.Rest;

/// <summary>
/// Wire-level coverage for the open-lobby cleanup path: a created game
/// that never fills up gets abandoned + the open-list endpoint stops
/// returning it. Drives the same <c>OpenLobbyJanitor</c> the background
/// service runs every minute in production, but invokes its
/// <c>RunOnceAsync</c> directly so the test doesn't have to wait on a
/// timer.
/// </summary>
public sealed class OpenLobbyJanitorIntegrationTests : RestTestBase
{
    [Fact]
    public async Task A_game_with_an_empty_seat_after_the_TTL_is_abandoned_and_disappears_from_listOpen()
    {
        // Use a 0-minute TTL so any created game is immediately eligible
        // for the janitor's cleanup on the very next run.
        Factory.ExtraSettings["Game:OpenLobbyTtlMinutes"] = "0";

        using HttpClient alice = Factory.CreateClient();
        await AuthFlowHelpers.RegisterAsync(alice, "alice", "alice@e.com", "Strong-Pass-123");
        await AuthFlowHelpers.LoginAndAttachAsync(alice, "alice", "Strong-Pass-123");

        // Alice creates a 2p game; never gets a second player.
        HttpResponseMessage create = await alice.PostAsJsonAsync(
            "/api/v1/games",
            new CreateGameRequestDto(GameMode.TwoPlayer, null, false, null));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        GameDetailDto created = (await create.Content.ReadFromJsonAsync<GameDetailDto>(TestJsonOptions.Default))!;
        created.Status.Should().Be(GameStatus.Open);

        // Pre-flight: the open list returns it.
        IEnumerable<GameSummaryDto> openBefore =
            (await alice.GetFromJsonAsync<IEnumerable<GameSummaryDto>>(
                $"/api/v1/games?status={GameStatus.Open}", TestJsonOptions.Default))!;
        openBefore.Should().Contain(g => g.Id == created.Id);

        // Drive the cleanup synchronously — the in-process janitor would
        // tick on its own PeriodicTimer eventually, but spinning up a
        // fresh instance from DI services is much faster for the test.
        OpenLobbyJanitor janitor = new(
            Factory.Services.GetRequiredService<IGameRepositoryFactory>(),
            Factory.Services.GetRequiredService<IClock>(),
            Factory.Services.GetRequiredService<IOptions<GameOptions>>(),
            Factory.Services.GetRequiredService<IGameEventBus>());
        await janitor.RunOnceAsync(CancellationToken.None);

        // The list no longer returns it; the abandoned status persisted.
        IEnumerable<GameSummaryDto> openAfter =
            (await alice.GetFromJsonAsync<IEnumerable<GameSummaryDto>>(
                $"/api/v1/games?status={GameStatus.Open}", TestJsonOptions.Default))!;
        openAfter.Should().NotContain(g => g.Id == created.Id);

        IEnumerable<GameSummaryDto> abandonedAfter =
            (await alice.GetFromJsonAsync<IEnumerable<GameSummaryDto>>(
                $"/api/v1/games?status={GameStatus.Abandoned}", TestJsonOptions.Default))!;
        abandonedAfter.Should().Contain(g => g.Id == created.Id);
    }
}
