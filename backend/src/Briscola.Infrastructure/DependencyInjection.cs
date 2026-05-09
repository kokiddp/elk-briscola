using System.Diagnostics.CodeAnalysis;
using Briscola.Infrastructure.Persistence;
using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Briscola.Infrastructure;

[ExcludeFromCodeCoverage]
public static class DependencyInjection
{
    /// <summary>
    /// Registers EF Core (provider-switched), ASP.NET Identity, and the
    /// adapter-port implementations that Phase 2's Application layer expects.
    /// Call this from <c>Briscola.Api/Program.cs</c>.
    /// </summary>
    public static IServiceCollection AddBriscolaInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var provider = configuration["ConnectionStrings:Provider"] ?? "Sqlite";
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<BriscolaDbContext>(opts =>
        {
            switch (provider)
            {
                case "Sqlite":
                    opts.UseSqlite(connectionString, b =>
                        b.MigrationsAssembly("Briscola.Infrastructure.Sqlite.Migrations"));
                    break;
                case "Postgres":
                    opts.UseNpgsql(connectionString, b =>
                        b.MigrationsAssembly("Briscola.Infrastructure.Postgres.Migrations"));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown ConnectionStrings:Provider '{provider}'. Use 'Sqlite' or 'Postgres'.");
            }
        });

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // Length over complexity. Per Step 3.4 spec.
                options.Password.RequireDigit = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;

                options.User.AllowedUserNameCharacters =
                    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-";
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<BriscolaDbContext>()
            .AddDefaultTokenProviders();

        return services;
    }
}
