using System.Security.Claims;
using Briscola.Application.Ports;

namespace Briscola.Api.Authentication;

/// <summary>
/// Adapts the current <see cref="HttpContext"/>'s authenticated principal
/// into the application-layer <see cref="IUserContext"/> port. Throws on
/// unauthenticated access — consumers must be reached via [Authorize]
/// endpoints.
/// </summary>
public sealed class HttpUserContext : IUserContext
{
    private readonly IHttpContextAccessor _accessor;

    public HttpUserContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public Guid UserId
    {
        get
        {
            string? sub = Principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? Principal.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);
            return sub is not null && Guid.TryParse(sub, out Guid id)
                ? id
                : throw new InvalidOperationException("No authenticated user id on the current request.");
        }
    }

    public string UserName =>
        Principal.FindFirstValue(ClaimTypes.Name)
            ?? throw new InvalidOperationException("No authenticated user name on the current request.");

    private ClaimsPrincipal Principal =>
        _accessor.HttpContext?.User
            ?? throw new InvalidOperationException("No HttpContext on the current scope.");
}
