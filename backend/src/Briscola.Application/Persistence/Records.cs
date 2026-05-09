using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Briscola.Domain.Primitives;

namespace Briscola.Application.Persistence;

[ExcludeFromCodeCoverage]
public sealed record GameRecord(
    Guid Id,
    GameMode Mode,
    string Name,
    GameStatus Status,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    long ShuffleSeed,
    string StateSnapshotJson,
    Suit BriscolaSuit,
    bool IsPrivate,
    string? PasswordHash,
    ImmutableArray<Guid?> SeatUserIds,
    long Version);

[ExcludeFromCodeCoverage]
public sealed record MoveRecord(
    Guid Id,
    Guid GameId,
    int MoveIndex,
    int SeatIndex,
    MoveType Type,
    string PayloadJson,
    DateTimeOffset CreatedAt);

public enum MoveType
{
    PlayCard,
    Forfeit,
    Disconnect,
    Reconnect,
    IdleTimeout,
}

[ExcludeFromCodeCoverage]
public sealed record GameResultRecord(
    Guid GameId,
    GameOutcomeKind Kind,
    int? WinnerKey,
    string SeatScoresJson,
    string? TeamScoresJson,
    EndedReason Reason);

public enum GameOutcomeKind
{
    Win,
    Draw,
}

public enum EndedReason
{
    Normal,
    ForfeitDisconnect,
    ForfeitIdle,
}

[ExcludeFromCodeCoverage]
public sealed record ChatMessageRecord(
    Guid Id,
    ChatScope Scope,
    Guid? GameId,
    Guid UserId,
    string Text,
    DateTimeOffset CreatedAt);

public enum ChatScope
{
    Lobby,
    Game,
}

[ExcludeFromCodeCoverage]
public sealed record RankingRecord(
    Guid UserId,
    int Elo,
    int Wins,
    int Losses,
    int Draws,
    int GamesPlayed,
    DateTimeOffset UpdatedAt);
