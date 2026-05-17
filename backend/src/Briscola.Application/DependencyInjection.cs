using System.Diagnostics.CodeAnalysis;
using Briscola.Application.Background;
using Briscola.Application.Bus;
using Briscola.Application.Configuration;
using Briscola.Application.History;
using Briscola.Application.Lobby;
using Briscola.Application.Orchestration;
using Briscola.Application.Orchestration.Timers;
using Briscola.Application.Ports;
using Briscola.Application.Ranking;
using Briscola.Application.Telemetry;
using Briscola.Domain.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Briscola.Application;

[ExcludeFromCodeCoverage]
public static class DependencyInjection
{
    public static IServiceCollection AddBriscolaApplication(this IServiceCollection services)
    {
        services.AddOptions<GameOptions>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IGameEventBus, InMemoryGameEventBus>();
        services.AddSingleton<ITimerService, SystemTimerService>();
        services.AddSingleton<IRandomSourceFactory, SystemRandomSourceFactory>();
        services.AddSingleton<IBriscolaEngine, BriscolaEngine>();
        services.AddSingleton<BriscolaMetrics>();
        services.AddSingleton<GameOrchestrator>();
        services.AddScoped<LobbyService>();
        services.AddScoped<RankingService>();
        services.AddScoped<MatchHistoryService>();
        services.AddHostedService<OpenLobbyJanitor>();
        services.AddHostedService<GracefulShutdownHostedService>();
        return services;
    }
}
