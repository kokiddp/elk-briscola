namespace Briscola.Application.Configuration;

public sealed class GameOptions
{
    public const string SectionName = "Game";

    public int ReconnectGraceSeconds { get; init; } = 120;
    public int IdleWarnSeconds { get; init; } = 90;
    public int IdleForfeitSeconds { get; init; } = 180;
    public int OpenLobbyTtlMinutes { get; init; } = 60;
    public string FourPlayerForfeitMode { get; init; } = "TeamForfeit";
}
