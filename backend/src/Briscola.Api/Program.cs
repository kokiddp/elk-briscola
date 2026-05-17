using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Briscola.Api;
using Briscola.Api.Authentication;
using Briscola.Api.CardSets;
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
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// 1) Options binding (read straight from configuration; sources are env vars,
//    appsettings.{Environment}.json, and command-line — built into the host).
builder.Services.Configure<GameOptions>(builder.Configuration.GetSection(GameOptions.SectionName));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection(CorsOptions.SectionName));
builder.Services.Configure<MigrationOptions>(builder.Configuration.GetSection(MigrationOptions.SectionName));
builder.Services.Configure<HubRateLimitOptions>(builder.Configuration.GetSection(HubRateLimitOptions.SectionName));

// 2) Serilog. Replaces the default ILogger* providers with Serilog's pipeline;
//    SerilogSetup encodes the dev-vs-prod sinks.
builder.Host.UseSerilog((ctx, lc) =>
    SerilogSetup.Configure(lc, ctx.HostingEnvironment, ctx.Configuration));

// 2b) OpenTelemetry metrics. Console exporter in dev (so `dotnet run` shows
//      counter ticks live), OTLP exporter when OTEL_EXPORTER_OTLP_ENDPOINT
//      is set — the standard env var the OTel SDK picks up automatically.
// Custom Meter "Briscola" (Briscola.Application.Telemetry.BriscolaMetrics)
// emits briscola.active_games / connected_players / moves_total.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService(serviceName: "elk-briscola-api",
                    serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0"))
    .WithMetrics(m =>
    {
        m.AddMeter(Briscola.Application.Telemetry.BriscolaMetrics.MeterName)
         .AddAspNetCoreInstrumentation()
         .AddRuntimeInstrumentation();

        if (builder.Environment.IsDevelopment() &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")))
        {
            m.AddConsoleExporter();
        }
        else
        {
            m.AddOtlpExporter();
        }
    });

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
builder.Services.AddRateLimiter(opts => RateLimitingPolicies.Configure(opts, builder.Configuration));

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
//
//    The JsonHubProtocol ships with default System.Text.Json options,
//    which means enums fly as numeric ids. The MVC layer uses
//    JsonStringEnumConverter + camelCase (step 8 above) and the AsyncAPI
//    spec advertises strings — keep the hub wire format aligned so the
//    Angular client decodes one shape, not two.
builder.Services
    .AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.AddHostedService<Briscola.Api.Hubs.GameEventDispatcher>();

// 10) Card-set catalog (loaded from wwwroot/card-sets at startup). Also
//     registered as ICardSetCatalog so application-layer services can
//     validate user-supplied card-set ids without depending on Briscola.Api.
builder.Services.AddSingleton(sp =>
    CardSetCatalog.LoadFromWebRoot(
        sp.GetRequiredService<IWebHostEnvironment>(),
        sp.GetRequiredService<ILoggerFactory>().CreateLogger<CardSetCatalog>()));
builder.Services.AddSingleton<ICardSetCatalog>(sp => sp.GetRequiredService<CardSetCatalog>());

// 11) Auto-docs (dev-only). Swashbuckle generates the OpenAPI v3 doc;
//     two UIs render it: classic Swagger UI at /swagger, Scalar at
//     /scalar/v1 (modern, dark-mode, language code samples).
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(opts =>
    {
        opts.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Briscola API",
            Version = "v1",
            Description =
                "REST surface for the Briscola card game. The companion " +
                "real-time hubs (`/hubs/lobby`, `/hubs/game`) are documented " +
                "in `docs/asyncapi.json` (rendered at /docs/asyncapi in dev). " +
                "Auth flow: POST /auth/register → POST /auth/login → use the " +
                "returned access token in the `Authorization: Bearer …` header.",
        });

        // JWT bearer "Authorize" button for both UIs. Microsoft.OpenApi
        // collapsed the .Models sub-namespace in v2; types live directly
        // under Microsoft.OpenApi (Swashbuckle 10 follows suit). Security
        // requirements reference the registered scheme by its
        // OpenApiSecuritySchemeReference id rather than embedding the
        // scheme instance directly.
        const string bearerSchemeId = "Bearer";
        opts.AddSecurityDefinition(bearerSchemeId, new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Description = "Paste the access token returned by /auth/login. The 'Bearer ' prefix is added automatically.",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
        });
        opts.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(bearerSchemeId)] = new List<string>(),
        });

        // Pull XML doc comments off the API assembly (controller actions,
        // remarks, response codes). The csproj emits Briscola.Api.xml next
        // to the assembly; suppress IO errors so a missing file in CI
        // doesn't break startup.
        string xmlPath = Path.Combine(AppContext.BaseDirectory, "Briscola.Api.xml");
        if (File.Exists(xmlPath))
        {
            opts.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
        }

        opts.SupportNonNullableReferenceTypes();
        opts.UseAllOfToExtendReferenceSchemas();
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
// Correlation-Id MUST run before SerilogRequestLogging so the request-summary
// line carries the same id the per-request logs do.
app.UseMiddleware<Briscola.Api.Middleware.CorrelationIdMiddleware>();
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
    // OpenAPI document at /swagger/v1/swagger.json (Swashbuckle convention).
    app.UseSwagger();
    // Classic Swagger UI at /swagger.
    app.UseSwaggerUI(opts =>
    {
        opts.DocumentTitle = "Briscola API — Swagger UI";
        opts.DefaultModelsExpandDepth(-1);
    });
    // Scalar at /scalar — modern UI, language code samples, dark-mode.
    app.MapScalarApiReference("/scalar", opts =>
    {
        opts.WithTitle("Briscola API — Scalar")
            .EnableDarkMode();
    });

    // AsyncAPI sidecar for the SignalR hubs. The spec is a static JSON
    // bundled next to the assembly (Content + CopyToOutputDirectory).
    // The viewer HTML and the /docs landing page live as embedded
    // strings in DocsLanding so static-web-asset routing rules don't
    // get in the way.
    string asyncApiJson = Path.Combine(AppContext.BaseDirectory, "docs", "asyncapi.json");
    if (File.Exists(asyncApiJson))
    {
        app.MapGet("/docs/asyncapi.json", () =>
            Results.File(asyncApiJson, contentType: "application/json"))
           .ExcludeFromDescription()
           .AllowAnonymous();
    }
    app.MapGet("/docs/asyncapi",
        () => Results.Content(DocsLanding.AsyncApiViewerHtml, "text/html"))
       .ExcludeFromDescription()
       .AllowAnonymous();
    // Tiny landing page so /docs lists the surfaces (REST + AsyncAPI).
    app.MapGet("/docs", () => Results.Content(DocsLanding.Html, "text/html"))
       .ExcludeFromDescription()
       .AllowAnonymous();
}

app.MapControllers();

// 14) SignalR hubs. JWT bearer is configured upstream to read
//     ?access_token= from /hubs/* paths so browsers can attach the
//     access token to the WebSocket handshake.
app.MapHub<Briscola.Api.Hubs.LobbyHub>("/hubs/lobby");
app.MapHub<Briscola.Api.Hubs.GameHub>("/hubs/game");

// 15) Liveness ping at root (covered by HealthController too).
app.MapGet("/", () => Results.Ok("elk-briscola api"));

// 16) Startup validation: the README mandates that the bundled
//     `placeholder` card set is always present. Treat its absence as an
//     installation bug — the resolver's per-card fallback relies on it,
//     so a missing placeholder would leave broken images. Fail fast.
{
    CardSetCatalog catalog = app.Services.GetRequiredService<CardSetCatalog>();
    if (!catalog.Contains(CardSetCatalog.PlaceholderId))
    {
        throw new InvalidOperationException(
            $"wwwroot/card-sets/{CardSetCatalog.PlaceholderId}/manifest.json is missing. " +
            "The placeholder set is required — see README §Card sets.");
    }
}

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
