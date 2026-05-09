namespace Briscola.Domain.Errors;

/// <summary>
/// Stable error codes surfaced to REST and SignalR clients.
/// These string names are part of the public API contract — do NOT rename
/// without coordinating with the API and frontend layers.
/// </summary>
public enum InvalidMoveCode
{
    NotYourTurn,
    CardNotInHand,
    GameFinished,
    WrongPhase,
    PileViewNotAllowed,
}
