namespace Briscola.Application.Configuration;

public sealed class GameOptions
{
    public const string SectionName = "Game";

    public int ReconnectGraceSeconds { get; init; } = 120;
    public int IdleWarnSeconds { get; init; } = 90;
    public int IdleForfeitSeconds { get; init; } = 180;
    /// <summary>
    /// Maximum lifetime of an Open game with empty seats before
    /// <c>OpenLobbyJanitor</c> abandons it. Five minutes is the user-
    /// facing default: "I created a game and nobody came, I expected
    /// the lobby to clean it up after a while." Deployments running a
    /// quieter cluster can raise it via the <c>Game:OpenLobbyTtlMinutes</c>
    /// config knob.
    /// </summary>
    public int OpenLobbyTtlMinutes { get; init; } = 5;
    public string FourPlayerForfeitMode { get; init; } = "TeamForfeit";
}
