using Briscola.Application.Ports;
using Briscola.Domain.State;

namespace Briscola.Application.Tests.TestDoubles;

internal sealed class InMemoryGameStateCodec : IGameStateCodec
{
    private readonly Dictionary<string, GameState> _states = [];
    private int _next;

    public string Serialize(GameState state)
    {
        string key = $"snapshot-{state.GameId}-{_next++}";
        _states[key] = state;
        return key;
    }

    public GameState Deserialize(string snapshot) => _states[snapshot];
}
