using Briscola.Domain.Primitives;

namespace Briscola.Domain.Tests;

public sealed class SeededRandomSourceTests
{
    [Fact]
    public void SameSeed_yields_same_sequence()
    {
        var a = new SeededRandomSource(42);
        var b = new SeededRandomSource(42);

        for (int i = 0; i < 1000; i++)
        {
            a.Next(1000).Should().Be(b.Next(1000));
        }

        a.Seed.Should().Be(42);
        b.Seed.Should().Be(42);
    }

    [Fact]
    public void DifferentSeeds_yield_different_sequences()
    {
        var a = new SeededRandomSource(1);
        var b = new SeededRandomSource(2);

        // Strong claim: at least one of the next 100 draws differs.
        var differs = false;
        for (int i = 0; i < 100; i++)
        {
            if (a.Next(int.MaxValue) != b.Next(int.MaxValue))
            {
                differs = true;
                break;
            }
        }
        differs.Should().BeTrue("two distinct seeds should not produce identical 100-element sequences");
    }
}
