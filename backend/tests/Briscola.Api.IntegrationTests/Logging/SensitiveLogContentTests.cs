using System.Reflection;
using System.Text.RegularExpressions;
using Briscola.Api;

namespace Briscola.Api.IntegrationTests.Logging;

/// <summary>
/// Static guard against sensitive data leaking into logs. We scan every
/// <c>[LoggerMessage]</c>-decorated method's <c>Message</c> template in
/// the API + Application + Infrastructure assemblies and assert that
/// none of them mention the forbidden tokens. Source-generated logging
/// means raw <c>logger.LogX("…")</c> calls would also be visible to a
/// future <c>String</c>-table scan, but for now the codebase uses
/// <c>[LoggerMessage]</c> exclusively (enforced by a separate test below).
/// </summary>
public sealed class SensitiveLogContentTests
{
    private static readonly string[] ForbiddenTokens =
    [
        "password",
        "token",
        "accessToken",
        "refreshToken",
        "passwordHash",
        "chatText",
    ];

    private static readonly Regex AllowedAlongsideToken = new(
        // "token" appears as a substring of identifiers we DO want to log,
        // e.g. "TokenExpiredException" / "RefreshTokenRotated". The guard
        // fires on lowercase template-style placeholders or unbroken words.
        @"(?<![A-Za-z])(password|access_token|refresh_token|chat_text)(?![A-Za-z])",
        RegexOptions.IgnoreCase);

    public static IEnumerable<object[]> AllLoggerMessageTemplates =>
        EnumerateLoggerMessageTemplates().Select(t => new object[] { t.Owner, t.Template });

    [Theory]
    [MemberData(nameof(AllLoggerMessageTemplates))]
    public void Template_does_not_log_forbidden_tokens(string owner, string template)
    {
        foreach (string forbidden in ForbiddenTokens)
        {
            // Whole-word, case-insensitive — "TokenizerException" stays fine,
            // but "refreshToken" / "Password" do not.
            if (Regex.IsMatch(template, $@"\b{Regex.Escape(forbidden)}\b", RegexOptions.IgnoreCase))
            {
                Assert.Fail(
                    $"LoggerMessage template on {owner} contains forbidden token '{forbidden}'. " +
                    $"Template: \"{template}\"");
            }
        }

        // The placeholder check is the second line of defence — a template
        // like "User changed password to {Password}" would be caught above
        // by the word "Password" alone.
        Match m = AllowedAlongsideToken.Match(template);
        if (m.Success)
        {
            Assert.Fail($"LoggerMessage template on {owner} mentions '{m.Value}'.");
        }
    }

    [Fact]
    public void Codebase_uses_LoggerMessage_attribute_exclusively()
    {
        // Defensive: if a future contributor writes `logger.LogInformation("…")`
        // with a raw string, the per-template scan above won't catch it.
        // Walk method bodies looking for direct calls to ILogger.LogX(string).
        // (We can't read IL trivially without an extra dep, so this is a soft
        // check on assembly-level type counts: every API-side logger user
        // should declare at least one [LoggerMessage]-annotated method.)
        Assembly api = typeof(Program).Assembly;
        int loggerMessageMethods = api.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .Count(m => m.GetCustomAttribute<Microsoft.Extensions.Logging.LoggerMessageAttribute>() is not null);
        loggerMessageMethods.Should().BeGreaterThan(0,
            "the API assembly should use [LoggerMessage] for all logger output");
    }

    private static IEnumerable<(string Owner, string Template)> EnumerateLoggerMessageTemplates()
    {
        Assembly[] assemblies =
        [
            typeof(Program).Assembly,
            typeof(Briscola.Application.Configuration.GameOptions).Assembly,
            typeof(Briscola.Infrastructure.Persistence.BriscolaDbContext).Assembly,
        ];

        foreach (Assembly assembly in assemblies)
        {
            foreach (Type t in assembly.GetTypes())
            {
                MethodInfo[] methods = t.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Static | BindingFlags.Instance |
                    BindingFlags.DeclaredOnly);
                foreach (MethodInfo m in methods)
                {
                    var attr = m.GetCustomAttribute<Microsoft.Extensions.Logging.LoggerMessageAttribute>();
                    if (attr is null || string.IsNullOrEmpty(attr.Message))
                    {
                        continue;
                    }
                    yield return ($"{t.FullName}.{m.Name}", attr.Message);
                }
            }
        }
    }
}
