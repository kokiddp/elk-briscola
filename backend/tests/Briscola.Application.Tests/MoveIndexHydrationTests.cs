using System.Text.Json;
using Briscola.Application.Orchestration;
using Briscola.Application.Orchestration.Commands;
using Briscola.Application.Persistence;
using Briscola.Application.Tests.TestDoubles;
using Briscola.Domain.Primitives;

namespace Briscola.Application.Tests;

/// <summary>
/// Regression tests for the move-log persistence path. Both bugs were
/// surfaced during the Phase 2 review.
/// </summary>
public sealed class MoveIndexHydrationTests
{
    [Fact]
    public async Task Rehydrated_room_continues_move_indexing_from_repository()
    {
        TestGameFactory factory = new();
        (GameRoom firstRoom, GameRecord record, Guid[] users) = await factory.CreateRunningRoomAsync();
        Guid gameId = record.Id;

        // Play one card so MoveIndex 0 is recorded.
        int firstSeat = firstRoom.CurrentState.NextToPlaySeat;
        Card firstCard = firstRoom.CurrentState.Hands[firstSeat][0];
        await firstRoom.EnqueueAsync(new PlayCardCommand(gameId, users[firstSeat], firstCard));

        // Simulate a process restart: reload the latest record from the repo
        // and create a fresh room from it. The new room must NOT restart at 0.
        GameRecord? reloaded = await factory.Games.GetAsync(gameId, CancellationToken.None);
        reloaded.Should().NotBeNull();
        GameRoom secondRoom = GameRoom.FromRecord(
            reloaded!,
            factory.Games,
            factory.Codec,
            factory.Engine,
            factory.Bus,
            factory.Clock,
            factory.Timers,
            factory.OptionsWrapper());

        int nextSeat = secondRoom.CurrentState.NextToPlaySeat;
        Card nextCard = secondRoom.CurrentState.Hands[nextSeat][0];
        await secondRoom.EnqueueAsync(new PlayCardCommand(gameId, users[nextSeat], nextCard));

        // Two PlayCard moves should be in the log with strictly increasing
        // MoveIndex values, no duplicates.
        IReadOnlyList<MoveRecord> playCardMoves = factory.Games.Moves
            .Where(m => m.Type == MoveType.PlayCard)
            .OrderBy(m => m.MoveIndex)
            .ToList();
        playCardMoves.Select(m => m.MoveIndex).Should().Equal(0, 1);
    }

    [Fact]
    public async Task PlayCard_payload_is_canonical_json()
    {
        TestGameFactory factory = new();
        (GameRoom room, _, Guid[] users) = await factory.CreateRunningRoomAsync();
        int seat = room.CurrentState.NextToPlaySeat;
        Card card = room.CurrentState.Hands[seat][0];

        await room.EnqueueAsync(new PlayCardCommand(room.CurrentState.GameId, users[seat], card));

        MoveRecord move = factory.Games.Moves.Single(m => m.Type == MoveType.PlayCard);
        // Round-trip through JsonSerializer to prove we wrote canonical JSON.
        using JsonDocument doc = JsonDocument.Parse(move.PayloadJson);
        doc.RootElement.GetProperty("suit").GetString().Should().Be(card.Suit.ToString());
        doc.RootElement.GetProperty("rank").GetString().Should().Be(card.Rank.ToString());
    }
}
