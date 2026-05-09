using Briscola.Domain.Primitives;

namespace Briscola.Domain.Tests;

/// <summary>
/// Seat is currently unused inside the Domain layer (the engine works
/// with seat indices, not Seat values). It exists for the Application
/// layer to populate per Phase 2. This test exercises the type so it
/// doesn't sit at 0% coverage and to verify record-equality semantics.
/// </summary>
public sealed class SeatTests
{
    [Fact]
    public void Seat_value_equality_works()
    {
        var pid = Guid.NewGuid();
        var a = new Seat(0, pid);
        var b = new Seat(0, pid);
        var c = new Seat(1, pid);

        a.Should().Be(b);
        a.Should().NotBe(c);
        a.Index.Should().Be(0);
        a.PlayerId.Should().Be(pid);
    }

    [Fact]
    public void Seat_PlayerId_can_be_null()
    {
        var s = new Seat(2, PlayerId: null);
        s.PlayerId.Should().BeNull();
        s.Index.Should().Be(2);
    }
}
