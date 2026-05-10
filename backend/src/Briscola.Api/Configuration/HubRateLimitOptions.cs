namespace Briscola.Api.Configuration;

/// <summary>
/// Per-connection rate limits for SignalR hub methods. Defaults match
/// the spec values from the Phase 5.6 plan; tests that drive a full
/// game in-process can bump <see cref="PlayCardWindowSeconds"/> down to
/// 0 to disable the limit.
/// </summary>
public sealed class HubRateLimitOptions
{
    public const string SectionName = "HubRateLimits";

    public int PlayCardPermits { get; init; } = 1;
    public double PlayCardWindowSeconds { get; init; } = 1.0;
    public int SendChatPermits { get; init; } = 5;
    public double SendChatWindowSeconds { get; init; } = 10.0;
}
