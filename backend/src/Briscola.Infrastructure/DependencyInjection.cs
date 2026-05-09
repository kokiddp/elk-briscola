using System.Diagnostics.CodeAnalysis;
using Briscola.Application.Ports;
using Briscola.Infrastructure.Auth;
using Briscola.Infrastructure.Codecs;
using Briscola.Infrastructure.Persistence;
using Briscola.Infrastructure.Persistence.Entities;
using Briscola.Infrastructure.Persistence.Repositories;
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

        // The provider/connection string are resolved lazily via the
        // IServiceProvider overload of AddDbContext so test fixtures can
        // override ConnectionStrings:* through ConfigureAppConfiguration
        // *after* this extension has run. Reading `configuration` eagerly
        // here would race the test override and silently fall back to
        // appsettings.json. (Same pattern as the JwtBearerOptions wiring
        // in Briscola.Api/Program.cs.)
        services.AddDbContext<BriscolaDbContext>((sp, opts) =>
        {
            IConfiguration cfg = sp.GetRequiredService<IConfiguration>();
            string provider = cfg["ConnectionStrings:Provider"] ?? "Postgres";
            string connectionString = cfg.GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

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

        // JWT issuance / refresh-token rotation. The actual JWT bearer
        // *validation* middleware lives in Briscola.Api (Phase 4) so the
        // Infrastructure project doesn't take a dependency on ASP.NET Core
        // pipeline types. JwtIssuer and RefreshTokenService are the
        // building blocks the Phase 4 controllers will call.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName));
        services.AddScoped<JwtIssuer>();
        services.AddScoped<RefreshTokenService>();

        // Adapter-port implementations of Briscola.Application.Ports.* — kept
        // out of Application by design (System.Text.Json + BCrypt are
        // infrastructure concerns).
        services.AddSingleton<IGameStateCodec, JsonGameStateCodec>();
        services.AddSingleton<IGamePasswordHasher, BCryptGamePasswordHasher>();

        // EF-backed repositories. Scoped because BriscolaDbContext is scoped.
        services.AddScoped<IGameRepository, EfGameRepository>();
        services.AddScoped<IChatRepository, EfChatRepository>();
        services.AddScoped<IRankingRepository, EfRankingRepository>();

        return services;
    }
}
