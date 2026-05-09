using Briscola.Domain.Primitives;

namespace Briscola.Domain.Tests;

public sealed class CardTablesTests
{
    [Fact]
    public void Points_summed_over_full_deck_equals_120()
    {
        var sum = CardTables.FullDeck.Sum(c => CardTables.Points(c.Rank));
        sum.Should().Be(CardTables.TotalDeckPoints).And.Be(120);
    }

    [Fact]
    public void Strength_returns_distinct_values_per_rank()
    {
        var allRanks = Enum.GetValues<Rank>();
        var strengths = allRanks.Select(CardTables.Strength).ToArray();

        strengths.Should().HaveCount(10);
        strengths.Distinct().Should().HaveCount(10, "every rank must have a unique strength");
    }

    [Fact]
    public void Strength_ordering_matches_canonical_order()
    {
        // Canonical order strongest-to-weakest: Asso, Tre, Re, Cavallo, Fante, 7, 6, 5, 4, 2.
        var canonical = new[]
        {
            Rank.Asso,
            Rank.Tre,
            Rank.Re,
            Rank.Cavallo,
            Rank.Fante,
            Rank.Sette,
            Rank.Sei,
            Rank.Cinque,
            Rank.Quattro,
            Rank.Due,
        };

        for (int i = 0; i < canonical.Length - 1; i++)
        {
            var stronger = canonical[i];
            var weaker = canonical[i + 1];
            CardTables.Strength(stronger).Should().BeGreaterThan(
                CardTables.Strength(weaker),
                "{0} must outrank {1}",
                stronger,
                weaker);
        }
    }

    [Fact]
    public void FullDeck_has_40_distinct_cards()
    {
        CardTables.FullDeck.Should().HaveCount(40);
        CardTables.FullDeck.Distinct().Should().HaveCount(40);
    }

    [Fact]
    public void Strength_is_not_derived_from_enum_underlying_int()
    {
        // Regression guard against the original buggy spec which encoded
        // strength in the Rank enum's underlying int (Rank.Asso == 14, etc.).
        // The current Rank enum starts at 0 (Asso), so if anyone ever rewrites
        // Strength to `(int)r` the assertion below will fail.
        ((int)Rank.Asso).Should().Be(0);
        CardTables.Strength(Rank.Asso).Should().NotBe((int)Rank.Asso);
        CardTables.Strength(Rank.Asso).Should().Be(10);
    }
}
