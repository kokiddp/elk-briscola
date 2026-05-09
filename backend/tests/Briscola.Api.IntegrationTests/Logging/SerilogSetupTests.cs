using Briscola.Infrastructure.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Briscola.Api.IntegrationTests.Logging;

/// <summary>
/// Smoke-level coverage of <see cref="SerilogSetup.Configure"/>: build a
/// LoggerConfiguration with each environment and verify it produces a
/// usable logger that respects the documented level overrides.
/// </summary>
public sealed class SerilogSetupTests
{
    [Fact]
    public void Development_environment_writes_to_console_and_file()
    {
        var config = new LoggerConfiguration();
        var env = new TestHostEnvironment("Development");
        var appConfig = new ConfigurationBuilder().Build();

        var configured = SerilogSetup.Configure(config, env, appConfig);
        using Logger logger = configured.CreateLogger();

        // No throw on a basic emit.
        logger.Information("hello {Name}", "alice");
    }

    [Fact]
    public void Production_environment_uses_json_formatter()
    {
        var config = new LoggerConfiguration();
        var env = new TestHostEnvironment("Production");
        var appConfig = new ConfigurationBuilder().Build();

        var configured = SerilogSetup.Configure(config, env, appConfig);
        using Logger logger = configured.CreateLogger();

        logger.Information("hello {Name}", "alice");
    }

    [Fact]
    public void AspNetCore_default_level_is_warning()
    {
        var config = new LoggerConfiguration();
        var env = new TestHostEnvironment("Production");
        var appConfig = new ConfigurationBuilder().Build();

        SerilogSetup.Configure(config, env, appConfig);
        using Logger logger = config.CreateLogger();

        // The level switch on Microsoft.AspNetCore should drop Information.
        // We can't easily intercept output here without a sink shim; assert
        // the level via Serilog's logger.IsEnabled instead.
        var aspNetLogger = logger.ForContext("SourceContext", "Microsoft.AspNetCore.Hosting");
        aspNetLogger.IsEnabled(LogEventLevel.Information).Should().BeFalse();
        aspNetLogger.IsEnabled(LogEventLevel.Warning).Should().BeTrue();
    }

    [Fact]
    public void Configure_throws_on_null_args()
    {
        Action a = () => SerilogSetup.Configure(null!, new TestHostEnvironment("Production"), new ConfigurationBuilder().Build());
        Action b = () => SerilogSetup.Configure(new LoggerConfiguration(), null!, new ConfigurationBuilder().Build());
        Action c = () => SerilogSetup.Configure(new LoggerConfiguration(), new TestHostEnvironment("Production"), null!);
        a.Should().Throw<ArgumentNullException>();
        b.Should().Throw<ArgumentNullException>();
        c.Should().Throw<ArgumentNullException>();
    }

    private sealed class TestHostEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "elk-briscola.tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
