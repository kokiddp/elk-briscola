using Briscola.Domain.State;

namespace Briscola.Application.Ports;

public interface IGameStateCodec
{
    string Serialize(GameState state);
    GameState Deserialize(string snapshot);
}
