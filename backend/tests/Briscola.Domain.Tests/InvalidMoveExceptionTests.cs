using Briscola.Domain.Errors;

namespace Briscola.Domain.Tests;

public sealed class InvalidMoveExceptionTests
{
    [Fact]
    public void Default_message_is_code_name()
    {
        var ex = new InvalidMoveException(InvalidMoveCode.NotYourTurn);

        ex.Code.Should().Be(InvalidMoveCode.NotYourTurn);
        ex.Message.Should().Be("NotYourTurn");
    }

    [Fact]
    public void Custom_message_is_preserved()
    {
        var ex = new InvalidMoveException(InvalidMoveCode.CardNotInHand, "Asso di Bastoni");

        ex.Code.Should().Be(InvalidMoveCode.CardNotInHand);
        ex.Message.Should().Be("Asso di Bastoni");
    }
}
