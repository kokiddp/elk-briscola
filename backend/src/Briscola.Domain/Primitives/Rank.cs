namespace Briscola.Domain.Primitives;

// IMPORTANT: Rank carries no implicit numeric semantics.
// The enum's underlying integer (0..9 in declaration order) MUST NOT be
// used to derive trick-strength or point value. Use CardTables.Strength
// and CardTables.Points instead.
public enum Rank
{
    Asso,
    Tre,
    Re,
    Cavallo,
    Fante,
    Sette,
    Sei,
    Cinque,
    Quattro,
    Due,
}
