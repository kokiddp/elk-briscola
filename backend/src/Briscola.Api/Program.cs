using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Briscola.Api.Authentication;
using Briscola.Api.Configuration;
using Briscola.Api.Errors;
using Briscola.Api.Middleware;
using Briscola.Api.RateLimiting;
using Briscola.Api.Validation;
using Briscola.Application;
using Briscola.Application.Configuration;
using Briscola.Application.Ports;
using Briscola.Infrastructure;
using Briscola.Infrastructure.Auth;
using Briscola.Infrastructure.Logging;
using Briscola.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// 1) Options binding (read straight from configuration; sources are env vars,
//    appsettings.{Environment}.json, and command-line — built into the host).
builder.Services.Configure<GameOptions>(builder.Configuration.GetSection(GameOptions.SectionName));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection(CorsOptions.SectionName));
builder.Services.Configure<MigrationOptions>(builder.Configuration.GetSection(MigrationOptions.SectionName));

// 2) Serilog. Replaces the default ILogger* providers with Serilog's pipeline;
//    SerilogSetup encodes the dev-vs-prod sinks.
builder.Host.UseSerilog((ctx, lc) =>
    SerilogSetup.Configure(lc, ctx.HostingEnvironment, ctx.Configuration));

// 3) Application + Infrastructure DI modules (DbContext, Identity, JwtIssuer,
//    repositories, BriscolaEngine, GameOrchestrator, hosted janitor, etc.).
builder.Services.AddBriscolaApplication();
builder.Services.AddBriscolaInfrastructure(builder.Configuration);

// 4) HttpContext-backed IUserContext adapter for the Application layer.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContext, HttpUserContext>();

// 5) Authentication + Authorization. SecurityStampValidator re-checks the
//    user's current SecurityStamp on every request so password change
//    invalidates outstanding access tokens.
//
//    JwtBearerOptions are bound through IConfigureOptions so the validator
//    sees the same JwtOptions snapshot as JwtIssuer. Reading
//    builder.Configuration eagerly here would race the test factory's
//    in-memory config overrides (which apply during host build).
builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>>(sp =>
    new ConfigureNamedOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opts =>
    {
        IHostEnvironment env = sp.GetRequiredService<IHostEnvironment>();
        JwtOptions jwt = sp.GetRequiredService<IOptions<JwtOptions>>().Value;
        byte[] signingBytes = TryDecodeBase64(jwt.SigningKey)
            ?? Encoding.UTF8.GetBytes(jwt.SigningKey);

        opts.RequireHttpsMetadata = !env.IsDevelopment();
        opts.SaveToken = false;
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = signingBytes.Length >= 32,
            IssuerSigningKey = signingBytes.Length >= 32 ? new SymmetricSecurityKey(signingBytes) : null,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
        };
        opts.Events = new JwtBearerEvents
        {
            OnTokenValidated = SecurityStampValidator.ValidateAsync,
            OnMessageReceived = ctx =>
            {
                // SignalR clients can only attach the access token to the
                // initial WebSocket handshake via the query string — Phase 5
                // hubs live under /hubs/*.
                string? accessToken = ctx.Request.Query["access_token"];
                PathString path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    ctx.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    }));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddAuthorization();

// 6) Rate limiting (per-policy; opt-in via [EnableRateLimiting] on actions).
builder.Services.AddRateLimiter(RateLimitingPolicies.Configure);

// 7) CORS.
builder.Services.AddCors(opts =>
{
    CorsOptions cors = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>()
        ?? new CorsOptions();
    opts.AddPolicy(CorsOptions.PolicyName, policy =>
    {
        if (cors.AllowedOrigins.Length == 0)
        {
            policy.AllowAnyOrigin();
        }
        else
        {
            policy.WithOrigins(cors.AllowedOrigins).AllowCredentials();
        }

        policy
            .WithMethods("GET", "POST", "PATCH", "DELETE", "OPTIONS")
            .WithHeaders("Authorization", "Content-Type", "X-Correlation-Id");
    });
});

// 8) MVC + JSON + validation filter.
builder.Services
    .AddControllers(options => options.Filters.Add<FluentValidationFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

// 9) SignalR + the hosted GameEventDispatcher that bridges
//    IGameEventBus → IHubContext<GameHub, IGameClient>.
builder.Services.AddSignalR();
builder.Services.AddHostedService<Briscola.Api.Hubs.GameEventDispatcher>();

// 10) Card-set catalog (loaded from wwwroot/card-sets at startup; empty until Phase 9).
builder.Services.AddSingleton(sp =>
    CardSetCatalog.LoadFromWebRoot(sp.GetRequiredService<IWebHostEnvironment>()));

// 11) Swagger (dev-only).
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(opts =>
    {
        opts.SwaggerDoc("v1", new OpenApiInfo { Title = "Briscola API", Version = "v1" });
    });
}

WebApplication app = builder.Build();

// 12) Migrations on startup (gated; default off in prod, on in Development).
//     After migrations, hydrate the orchestrator with any games left in
//     Running status from a prior process restart so their rooms exist
//     on the new process and reconnects/forfeits can flow through.
MigrationOptions migrations = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MigrationOptions>>().Value;
if (migrations.RunOnStartup)
{
    using IServiceScope scope = app.Services.CreateScope();
    BriscolaDbContext db = scope.ServiceProvider.GetRequiredService<BriscolaDbContext>();
    await db.Database.MigrateAsync().ConfigureAwait(false);
}
{
    Briscola.Application.Orchestration.GameOrchestrator orchestrator =
        app.Services.GetRequiredService<Briscola.Application.Orchestration.GameOrchestrator>();
    await orchestrator.HydrateAsync(app.Lifetime.ApplicationStopping).ConfigureAwait(false);
}

// 13) Middleware pipeline (order matters).
app.UseSerilogRequestLogging();
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    ExceptionHandler = ApiProblemDetails.WriteAsync,
});
app.UseSecurityHeaders();
bool disableHttpsRedirect = string.Equals(
    app.Configuration["DISABLE_HTTPS_REDIRECTION"], "true", StringComparison.OrdinalIgnoreCase);
if (!app.Environment.IsDevelopment() && !disableHttpsRedirect)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
app.UseCors(CorsOptions.PolicyName);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

// 14) SignalR hubs. JWT bearer is configured upstream to read
//     ?access_token= from /hubs/* paths so browsers can attach the
//     access token to the WebSocket handshake.
app.MapHub<Briscola.Api.Hubs.LobbyHub>("/hubs/lobby");
app.MapHub<Briscola.Api.Hubs.GameHub>("/hubs/game");

// 15) Liveness ping at root (covered by HealthController too).
app.MapGet("/", () => Results.Ok("elk-briscola api"));

await app.RunAsync().ConfigureAwait(false);

static byte[]? TryDecodeBase64(string s)
{
    if (string.IsNullOrWhiteSpace(s))
    {
        return null;
    }

    try { return Convert.FromBase64String(s); }
    catch (FormatException) { return null; }
}

public partial class Program;
