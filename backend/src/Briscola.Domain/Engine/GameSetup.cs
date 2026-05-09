using System.Collections.Immutable;
using Briscola.Domain.Primitives;

namespace Briscola.Domain.Engine;

/// <summary>
/// Inputs to <see cref="IBriscolaEngine.StartGame"/>.
/// <paramref name="PlayerIds"/> is positional: index = seat.
/// </summary>
public sealed record GameSetup(
    Guid GameId,
    GameMode Mode,
    int DealerSeat,
    ImmutableArray<Guid> PlayerIds);
