namespace Briscola.Domain.Primitives;

// PlayerId is nullable because the engine itself is identity-agnostic;
// the application layer fills it in. The engine only cares about Index.
public readonly record struct Seat(int Index, Guid? PlayerId);
