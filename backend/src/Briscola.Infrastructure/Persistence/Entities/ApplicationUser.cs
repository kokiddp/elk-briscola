using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Identity;

namespace Briscola.Infrastructure.Persistence.Entities;

/// <summary>
/// ASP.NET Identity user with the elk-briscola-specific extensions.
/// Identity's own columns (UserName, Email, PasswordHash, SecurityStamp,
/// ConcurrencyStamp, LockoutEnd, etc.) are inherited from
/// <see cref="IdentityUser{TKey}"/>.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
    public string ActiveCardSetId { get; set; } = "placeholder";
    public DateTimeOffset CreatedAt { get; set; }
}
