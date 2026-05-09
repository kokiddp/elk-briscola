using System.Security.Claims;
using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;

namespace Briscola.Api.Authentication;

/// <summary>
/// Re-checks the <c>security_stamp</c> claim baked into an incoming access
/// token against the user's current stamp on every request. A password
/// change rotates the stamp, so any access token issued before the change
/// has a stale claim and is rejected here.
/// </summary>
public static class SecurityStampValidator
{
    public const string ClaimType = "security_stamp";

    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ClaimsPrincipal? principal = context.Principal;
        if (principal is null)
        {
            context.Fail("No principal on the validated token.");
            return;
        }

        string? sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(sub, out Guid userId))
        {
            context.Fail("Token has no parseable subject.");
            return;
        }

        UserManager<ApplicationUser> users =
            context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();

        ApplicationUser? user = await users.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        if (user is null)
        {
            context.Fail("User no longer exists.");
            return;
        }

        string? tokenStamp = principal.FindFirstValue(ClaimType);
        string? currentStamp = await users.GetSecurityStampAsync(user).ConfigureAwait(false);
        if (string.IsNullOrEmpty(tokenStamp) || tokenStamp != currentStamp)
        {
            context.Fail("Security stamp mismatch.");
        }
    }
}
