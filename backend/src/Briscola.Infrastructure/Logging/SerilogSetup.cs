using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Briscola.Infrastructure.Logging;

/// <summary>
/// Configures Serilog for the API host. Dev: console (compact text) +
/// rolling file. Prod: console (JSON) only.
/// </summary>
public static class SerilogSetup
{
    /// <summary>
    /// Apply Serilog config to a <see cref="LoggerConfiguration"/>. Called
    /// from <c>Program.cs</c> via <c>builder.Host.UseSerilog((ctx, lc) =&gt; SerilogSetup.Configure(lc, ctx.HostingEnvironment, ctx.Configuration))</c>.
    /// </summary>
    public static LoggerConfiguration Configure(
        LoggerConfiguration config,
        IHostEnvironment environment,
        IConfiguration appConfiguration)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(appConfiguration);

        config
            .ReadFrom.Configuration(appConfiguration)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentName()
            .Enrich.WithThreadId()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command",
                environment.IsDevelopment() ? LogEventLevel.Information : LogEventLevel.Warning);

        if (environment.IsDevelopment())
        {
            // {Properties:j} dumps every enriched property (CorrelationId included).
            config
                .WriteTo.Console(
                    outputTemplate:
                        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}",
                    formatProvider: CultureInfo.InvariantCulture)
                .WriteTo.File(
                    path: "logs/briscola-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    fileSizeLimitBytes: 100 * 1024 * 1024,
                    outputTemplate:
                        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}",
                    formatProvider: CultureInfo.InvariantCulture);
        }
        else
        {
            // Production: JSON to stdout for log shippers (ELK / Loki / etc.).
            config.WriteTo.Console(new CompactJsonFormatter());
        }

        return config;
    }
}
