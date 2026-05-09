using Briscola.Domain.Primitives;

namespace Briscola.Application.Ports;

/// <summary>
/// Produces a fresh seeded RNG for each game start. The seed is captured
/// in <c>GameState.ShuffleSeed</c> so games are deterministically replayable
/// from the seed plus the move log.
/// </summary>
public interface IRandomSourceFactory
{
    IRandomSource Create();
}
