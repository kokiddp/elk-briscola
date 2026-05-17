using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Briscola.Api.Dtos;
using Briscola.Application.Persistence;
using Briscola.Domain.Primitives;
using Briscola.Infrastructure.Persistence;
using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Briscola.Api.IntegrationTests.Rest;

/// <summary>
/// Black-box coverage for <c>GET /api/v1/me/history</c>. Seeds Finished
/// games directly into the DB (skipping the slow play-through), then
/// asserts paging, ordering, and result-relative scoring on the wire.
/// </summary>
public sealed class MeHistoryTests : RestTestBase
{
    [Fact]
    public async Task Unauthenticated_returns_401()
    {
        using HttpClient client = Factory.CreateClient();
        HttpResponseMessage res = await client.GetAsync("/api/v1/me/history");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Empty_when_user_has_no_finished_games()
    {
        using HttpClient client = Factory.CreateClient();
        await AuthFlowHelpers.RegisterAsync(client, "alice", "alice@example.com", "Strong-Pass-123");
        await AuthFlowHelpers.LoginAndAttachAsync(client, "alice", "Strong-Pass-123");

        MatchHistoryPageDto page = (await client.GetFromJsonAsync<MatchHistoryPageDto>(
            "/api/v1/me/history", TestJsonOptions.Default))!;

        page.Items.Should().BeEmpty();
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(20);
        page.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Returns_seeded_games_newest_first_with_correct_player_view()
    {
        using HttpClient client = Factory.CreateClient();
        await AuthFlowHelpers.RegisterAsync(client, "alice", "alice@example.com", "Strong-Pass-123");
        MeResponse me = await AuthenticatedMeAsync(client, "alice", "Strong-Pass-123");
        Guid alice = me.Id;
        Guid bob = Guid.NewGuid();

        // Seed two finished games: old loss + new win.
        await SeedFinishedAsync(NewGuid(seed: 1),
            createdAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            endedAt: new DateTimeOffset(2026, 1, 1, 0, 30, 0, TimeSpan.Zero),
            aliceSeat: 0, alice: alice, bob: bob, winnerKey: 1, seatScores: "[40,80]");
        await SeedFinishedAsync(NewGuid(seed: 2),
            createdAt: new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            endedAt: new DateTimeOffset(2026, 2, 1, 0, 30, 0, TimeSpan.Zero),
            aliceSeat: 1, alice: alice, bob: bob, winnerKey: 1, seatScores: "[20,90]");

        MatchHistoryPageDto page = (await client.GetFromJsonAsync<MatchHistoryPageDto>(
            "/api/v1/me/history?page=1&size=10", TestJsonOptions.Default))!;

        page.Items.Should().HaveCount(2);
        page.TotalCount.Should().Be(2);
        // Newest first.
        page.Items[0].EndedAt!.Value.Year.Should().Be(2026);
        page.Items[0].EndedAt!.Value.Month.Should().Be(2);
        page.Items[1].EndedAt!.Value.Month.Should().Be(1);
        // Player-relative seat index is correct for each entry.
        page.Items[0].MySeatIndex.Should().Be(1);
        page.Items[1].MySeatIndex.Should().Be(0);
        // Scores arrive parsed (not as JSON strings).
        page.Items[0].SeatScores.Should().Equal(20, 90);
        page.Items[1].SeatScores.Should().Equal(40, 80);
        // Enums round-trip as strings.
        page.Items[0].OutcomeKind.Should().Be("Win");
        page.Items[0].Reason.Should().Be("Normal");
    }

    [Fact]
    public async Task Pagination_skips_pages_and_clamps_size()
    {
        using HttpClient client = Factory.CreateClient();
        await AuthFlowHelpers.RegisterAsync(client, "alice", "alice@example.com", "Strong-Pass-123");
        MeResponse me = await AuthenticatedMeAsync(client, "alice", "Strong-Pass-123");
        Guid alice = me.Id;
        Guid bob = Guid.NewGuid();

        for (int i = 0; i < 7; i++)
        {
            await SeedFinishedAsync(NewGuid(seed: 100 + i),
                createdAt: new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i),
                endedAt: new DateTimeOffset(2026, 3, 1, 1, 0, 0, TimeSpan.Zero).AddMinutes(i),
                aliceSeat: 0, alice: alice, bob: bob, winnerKey: 0, seatScores: "[80,40]");
        }

        // Page 1, size 3 → first 3 newest.
        MatchHistoryPageDto p1 = (await client.GetFromJsonAsync<MatchHistoryPageDto>(
            "/api/v1/me/history?page=1&size=3", TestJsonOptions.Default))!;
        p1.Items.Should().HaveCount(3);
        p1.TotalCount.Should().Be(7);

        // Page 3 of 3 → only 1 item.
        MatchHistoryPageDto p3 = (await client.GetFromJsonAsync<MatchHistoryPageDto>(
            "/api/v1/me/history?page=3&size=3", TestJsonOptions.Default))!;
        p3.Items.Should().HaveCount(1);

        // Distinct sets across pages — pagination did not duplicate.
        IEnumerable<Guid> idsP1 = p1.Items.Select(x => x.GameId);
        p3.Items.Select(x => x.GameId).Should().NotIntersectWith(idsP1);

        // Out-of-range size > 100 is clamped server-side.
        MatchHistoryPageDto pBig = (await client.GetFromJsonAsync<MatchHistoryPageDto>(
            "/api/v1/me/history?page=1&size=9999", TestJsonOptions.Default))!;
        pBig.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task Other_users_games_are_not_visible()
    {
        using HttpClient client = Factory.CreateClient();
        await AuthFlowHelpers.RegisterAsync(client, "alice", "alice@example.com", "Strong-Pass-123");
        await AuthFlowHelpers.LoginAndAttachAsync(client, "alice", "Strong-Pass-123");
        Guid stranger1 = Guid.NewGuid();
        Guid stranger2 = Guid.NewGuid();

        await SeedFinishedAsync(NewGuid(seed: 200),
            createdAt: new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            endedAt: new DateTimeOffset(2026, 4, 1, 0, 30, 0, TimeSpan.Zero),
            aliceSeat: 0, alice: stranger1, bob: stranger2, winnerKey: 0, seatScores: "[60,60]");

        MatchHistoryPageDto page = (await client.GetFromJsonAsync<MatchHistoryPageDto>(
            "/api/v1/me/history", TestJsonOptions.Default))!;
        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
    }

    private static async Task<MeResponse> AuthenticatedMeAsync(HttpClient client, string username, string password)
    {
        _ = await AuthFlowHelpers.LoginAndAttachAsync(client, username, password);
        return (await client.GetFromJsonAsync<MeResponse>("/api/v1/me", TestJsonOptions.Default))!;
    }

    /// <summary>
    /// Inserts a Finished 2p game with a result row in one transaction.
    /// </summary>
    private async Task SeedFinishedAsync(
        Guid gameId,
        DateTimeOffset createdAt,
        DateTimeOffset endedAt,
        int aliceSeat,
        Guid alice,
        Guid bob,
        int winnerKey,
        string seatScores)
    {
        Guid seatZeroUser = aliceSeat == 0 ? alice : bob;
        Guid seatOneUser = aliceSeat == 0 ? bob : alice;
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        BriscolaDbContext db = scope.ServiceProvider.GetRequiredService<BriscolaDbContext>();
        db.Games.Add(new GameEntity
        {
            Id = gameId,
            Mode = GameMode.TwoPlayer,
            Name = "seeded",
            Status = GameStatus.Finished,
            CreatedByUserId = seatZeroUser,
            CreatedAt = createdAt,
            StartedAt = createdAt,
            EndedAt = endedAt,
            ShuffleSeed = 42,
            StateSnapshotJson = "{}",
            BriscolaSuit = Suit.Denari,
            IsPrivate = false,
            Version = 1,
            Seats =
            [
                new GameSeatEntity { GameId = gameId, SeatIndex = 0, UserId = seatZeroUser, JoinedAt = createdAt },
                new GameSeatEntity { GameId = gameId, SeatIndex = 1, UserId = seatOneUser, JoinedAt = createdAt },
            ],
        });
        db.GameResults.Add(new GameResultEntity
        {
            GameId = gameId,
            Kind = GameOutcomeKind.Win,
            WinnerKey = winnerKey,
            SeatScoresJson = seatScores,
            TeamScoresJson = null,
            Reason = EndedReason.Normal,
        });
        await db.SaveChangesAsync();
    }

    private static Guid NewGuid(int seed)
    {
        // Deterministic GUIDs so ToString-based ordering is predictable
        // across runs — avoids flakiness on timestamp ties.
        byte[] bytes = new byte[16];
        BitConverter.GetBytes(seed).CopyTo(bytes, 0);
        return new Guid(bytes);
    }
}
