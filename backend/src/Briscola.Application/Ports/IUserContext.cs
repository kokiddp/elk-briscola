namespace Briscola.Application.Ports;

public interface IUserContext
{
    Guid UserId { get; }
    string UserName { get; }
}
