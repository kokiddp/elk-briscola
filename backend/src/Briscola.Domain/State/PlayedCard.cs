using Briscola.Domain.Primitives;

namespace Briscola.Domain.State;

public readonly record struct PlayedCard(int SeatIndex, Card Card);
