// Stress test harness for elk-briscola — Phase 11.5 ops baseline tool.
// Spins up N concurrent worker tasks, each looping over the full
// register → login → create-game → join → SignalR-connect flow for the
// requested duration. Reports request count + latency percentiles + the
// most common failure shapes.
//
// Self-contained: no third-party load-test SDK so the repo doesn't pick
// up a non-OSS license. CI does NOT run this — it's a manual tool.
//
// Usage:
//   dotnet run --project backend/tools/Briscola.StressTest -c Release -- \
//       --base http://localhost:5080 \
//       --games 50 \
//       --duration 00:30:00

using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Briscola.Api.Dtos;
using Briscola.Domain.Primitives;
using Microsoft.AspNetCore.SignalR.Client;

// Match the REST surface's JSON shape: camelCase + enums as strings.
JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter() },
};

Uri baseUri = ResolveOption(args, "--base", new Uri("http://localhost:5080"), v => new Uri(v));
int games = ResolveOption(args, "--games", 50, int.Parse);
TimeSpan duration = ResolveOption(args, "--duration", TimeSpan.FromMinutes(2), TimeSpan.Parse);

Console.WriteLine("== elk-briscola stress harness ==");
Console.WriteLine($"   base     = {baseUri}");
Console.WriteLine($"   games    = {games}");
Console.WriteLine($"   duration = {duration}");
Console.WriteLine();

string runId = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
using CancellationTokenSource stop = new(duration);

var results = new System.Collections.Concurrent.ConcurrentBag<IterationResult>();
DateTimeOffset startedAt = DateTimeOffset.UtcNow;

Task[] workers = Enumerable.Range(0, games).Select(workerId =>
    Task.Run(async () =>
    {
        int iter = 0;
        while (!stop.IsCancellationRequested)
        {
            iter++;
            // GUID disambiguates between concurrent runs on the same DB —
            // {runId, workerId, iter} alone collided when the same DB was
            // reused across consecutive harness invocations.
            string suffix = $"{runId}_{workerId}_{iter}_{Guid.NewGuid():N}".Substring(0, 40);
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                await RunOneIterationAsync(baseUri, suffix, JsonOpts, stop.Token);
                results.Add(new IterationResult(true, sw.Elapsed, null));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                results.Add(new IterationResult(false, sw.Elapsed, $"{ex.GetType().Name}: {ex.Message}"));
            }
        }
    })).ToArray();

await Task.WhenAll(workers);

TimeSpan ran = DateTimeOffset.UtcNow - startedAt;
PrintReport(results.ToArray(), ran);

return 0;

static async Task RunOneIterationAsync(Uri baseUri, string suffix, JsonSerializerOptions json, CancellationToken ct)
{
    using HttpClient alice = NewClient(baseUri);
    using HttpClient bob = NewClient(baseUri);

    await RegisterAsync(alice, $"alice_{suffix}", json, ct);
    await RegisterAsync(bob, $"bob_{suffix}", json, ct);
    string aliceTok = await LoginAsync(alice, $"alice_{suffix}", json, ct);
    string bobTok = await LoginAsync(bob, $"bob_{suffix}", json, ct);
    alice.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", aliceTok);
    bob.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bobTok);

    HttpResponseMessage create = await alice.PostAsJsonAsync("/api/v1/games",
        new CreateGameRequestDto(GameMode.TwoPlayer, $"stress_{suffix}", false, null), json, ct);
    create.EnsureSuccessStatusCode();
    GameDetailDto game = (await create.Content.ReadFromJsonAsync<GameDetailDto>(json, cancellationToken: ct))!;
    HttpResponseMessage join = await bob.PostAsJsonAsync(
        $"/api/v1/games/{game.Id}/join", new JoinGameRequestDto(null), json, ct);
    join.EnsureSuccessStatusCode();

    await using HubConnection aliceHub = await ConnectGameHubAsync(baseUri, aliceTok, ct);
    await using HubConnection bobHub = await ConnectGameHubAsync(baseUri, bobTok, ct);
    await aliceHub.InvokeAsync("JoinGame", game.Id, ct);
    await bobHub.InvokeAsync("JoinGame", game.Id, ct);

    // Hold the connections so concurrent workers overlap on active-games.
    await Task.Delay(TimeSpan.FromSeconds(2), ct);
}

static HttpClient NewClient(Uri baseUri) => new()
{
    BaseAddress = baseUri,
    Timeout = TimeSpan.FromSeconds(30),
};

static async Task RegisterAsync(HttpClient client, string username, JsonSerializerOptions json, CancellationToken ct)
{
    HttpResponseMessage r = await client.PostAsJsonAsync("/api/v1/auth/register",
        new RegisterRequest(username, username + "@stress.local", "Strong-Pass-123", username), json, ct);
    r.EnsureSuccessStatusCode();
}

static async Task<string> LoginAsync(HttpClient client, string username, JsonSerializerOptions json, CancellationToken ct)
{
    HttpResponseMessage r = await client.PostAsJsonAsync("/api/v1/auth/login",
        new LoginRequest(username, "Strong-Pass-123"), json, ct);
    r.EnsureSuccessStatusCode();
    TokenResponse t = (await r.Content.ReadFromJsonAsync<TokenResponse>(json, cancellationToken: ct))!;
    return t.AccessToken;
}

static async Task<HubConnection> ConnectGameHubAsync(Uri baseUri, string accessToken, CancellationToken ct)
{
    HubConnection hub = new HubConnectionBuilder()
        .WithUrl(new Uri(baseUri, "/hubs/game"),
            opts => opts.AccessTokenProvider = () => Task.FromResult<string?>(accessToken))
        .Build();
    await hub.StartAsync(ct);
    return hub;
}

static T ResolveOption<T>(string[] argv, string name, T fallback, Func<string, T> parse)
{
    for (int i = 0; i < argv.Length - 1; i++)
    {
        if (argv[i] == name) return parse(argv[i + 1]);
    }
    return fallback;
}

static void PrintReport(IterationResult[] all, TimeSpan ran)
{
    int total = all.Length;
    int ok = all.Count(r => r.Ok);
    int fail = total - ok;
    double[] okMs = all.Where(r => r.Ok).Select(r => r.Latency.TotalMilliseconds).OrderBy(x => x).ToArray();

    Console.WriteLine();
    Console.WriteLine("==================== REPORT ====================");
    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
        " total iterations   {0}", total));
    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
        " ok                 {0} ({1:F2} %)", ok, total == 0 ? 0 : 100.0 * ok / total));
    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
        " fail               {0} ({1:F2} %)", fail, total == 0 ? 0 : 100.0 * fail / total));
    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
        " wall-clock         {0:hh\\:mm\\:ss}", ran));
    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
        " ok throughput      {0:F2} iter/s", ran.TotalSeconds == 0 ? 0 : ok / ran.TotalSeconds));
    if (okMs.Length > 0)
    {
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            " ok latency (ms)    p50={0:F0} p90={1:F0} p95={2:F0} p99={3:F0} max={4:F0}",
            Percentile(okMs, 0.50),
            Percentile(okMs, 0.90),
            Percentile(okMs, 0.95),
            Percentile(okMs, 0.99),
            okMs[^1]));
    }
    if (fail > 0)
    {
        Console.WriteLine();
        Console.WriteLine(" top failure types:");
        foreach (var grp in all.Where(r => !r.Ok && r.Error != null)
                               .GroupBy(r => r.Error)
                               .OrderByDescending(g => g.Count())
                               .Take(5))
        {
            Console.WriteLine($"   {grp.Count(),5} × {grp.Key}");
        }
    }
    Console.WriteLine("================================================");
}

static double Percentile(double[] sorted, double p)
{
    int idx = (int)Math.Clamp(p * (sorted.Length - 1), 0, sorted.Length - 1);
    return sorted[idx];
}

internal sealed record IterationResult(bool Ok, TimeSpan Latency, string? Error);
