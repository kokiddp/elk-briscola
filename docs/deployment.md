# Deployment

> Detailed deploy + backup procedures land with Phase 12. The configuration
> knobs themselves are listed in [README.md](../README.md#configuration-keys).

## Stress test (Phase 11.5)

`backend/tools/Briscola.StressTest` is a self-contained C# load generator
that exercises the full register → login → create-game → join → SignalR
flow concurrently. It's a manual ops tool — CI does **not** run it.

### Profile

The default rate limits — register: 3 / hour / IP, login: 5 / minute /
IP — are intentional in production but block any meaningful single-host
load. The `Stress` ASP.NET environment relaxes both to one million per
minute and shortens the game's idle / forfeit timers so abandoned
scenarios don't pile up. The profile file is checked in at
`backend/src/Briscola.Api/appsettings.Stress.json`.

### One-shot run

```bash
# 1. Start a dev Postgres (the dev seed credentials are in
#    appsettings.Development.json; reuse the same container the
#    integration suite uses).
docker start briscola-dev-postgres

# 2. Start the API under the Stress profile. We point at the published
#    DLL because `dotnet run` honors launchSettings.json, which pins
#    the environment to Development.
( cd backend && dotnet build -c Release )
cd backend/src/Briscola.Api/bin/Release/net10.0
ASPNETCORE_ENVIRONMENT=Stress dotnet Briscola.Api.dll &

# 3. Run the harness from the repo root.
cd /home/koki/elk-briscola/backend
dotnet run --project tools/Briscola.StressTest -c Release -- \
    --base http://localhost:5080 \
    --games 50 \
    --duration 00:30:00
```

### What the harness reports

At end-of-run the harness prints an aggregated report:

```text
==================== REPORT ====================
 total iterations   3127
 ok                 3120 (99.78 %)
 fail               7 (0.22 %)
 wall-clock         00:30:00
 ok throughput      1.73 iter/s
 ok latency (ms)    p50=820 p90=2150 p95=2914 p99=4011 max=5832

 top failure types:
       4 × HttpRequestException: Response status code does not indicate success: 500 (Internal Server Error).
       3 × TaskCanceledException: ...
================================================
```

### Assertion budget

Use these as ops red-lines when interpreting a run:

- **Errors < 1 %** of total iterations.
- **p95 latency < 30 s** per iteration (each iteration is register +
  login + create + join + 2× SignalR connect + 2 s linger).
- **`briscola.active_games` steady-state ≤ `--games`** — confirm via the
  OTel console exporter output (look for the gauge under the `Briscola`
  meter) or by piping OTLP to Prometheus.
- **No `OutOfMemoryException`** in the host log.
- **CPU < 70 %** on a 2-core VM.

### Baseline numbers

> Populate this table from the next dev-env run before tagging
> a release. Numbers from a smoke / one-off run go below.

| Profile | Games | Duration | OK % | p50 | p95 | RPS |
| --- | --- | --- | --- | --- | --- | --- |
| `Stress` (dev WSL2) | 50 | 30 min | _pending_ | _pending_ | _pending_ | _pending_ |
