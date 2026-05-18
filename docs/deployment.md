# Deployment

This document covers the full prod-shape compose stack, secrets, backups,
upgrades, and key rotation. Application-level configuration knobs are
listed in [README.md](../README.md#configuration-keys); this doc focuses
on the *ops* layer.

## Architecture at a glance

```
                       ┌────────────────────────────────┐
   browser ── :8080 ──▶│ frontend (nginx)               │
                       │   static SPA + reverse proxy   │
                       │   for /api/* /hubs/* /card-…/* │
                       └──────────────┬─────────────────┘
                                      │ docker bridge
                                      ▼
                       ┌────────────────────────────────┐
                       │ api (ASP.NET Core)             │
                       │   /healthz  /readyz  /api  /hubs│
                       └──────────────┬─────────────────┘
                                      │ docker bridge
                                      ▼
                       ┌────────────────────────────────┐
                       │ postgres                       │
                       │   volume: pgdata               │
                       └────────────────────────────────┘
```

Only the frontend port is published to the host. The api never exposes
a port outside the compose network; the browser hits `/api/v1/*` and
`/hubs/*` on the same origin that served the SPA, and nginx forwards
those upstream over the docker bridge.

## Quick start (clean VM in under 10 minutes)

```bash
# 0) Prerequisites: Docker Engine + the Compose plugin. Nothing else.
docker --version && docker compose version

# 1) Clone the repo + drop into it.
git clone https://github.com/<org>/elk-briscola.git
cd elk-briscola

# 2) Fill in the two required secrets.
cp .env.example .env
sed -i "s|replace-me-with-a-long-random-string|$(openssl rand -hex 32)|" .env
sed -i "s|replace-me-with-base64-32B-min|$(openssl rand -base64 32)|" .env

# 3) Build + boot the stack.
docker compose up -d --build

# 4) Wait for healthy and smoke-test.
docker compose ps   # all three rows should show STATUS = healthy
curl -fsS http://localhost:8080/healthz                # 200
curl -fsS http://localhost:8080/api/v1/me              # 401 (auth required)
open http://localhost:8080                             # the SPA
```

## Image choices

| Image | Base | Footprint | Notes |
| --- | --- | --- | --- |
| `elk-briscola/api` | `aspnet:10.0-noble-chiseled-extra` with curl spliced in from the SDK build stage | ~218 MB | distroless-style; non-root by default; ICU + tzdata + curl available |
| `elk-briscola/frontend` | `nginx:alpine` | ~52 MB | static SPA + reverse proxy to the api |

The api Dockerfile spent a stage on a curl-bearing image and copies
`/usr/bin/curl` plus its `ldd`-resolved shared libs into the chiseled
runtime. That keeps the runtime image distroless-shaped while still
giving the `HEALTHCHECK` a working probe; the alternative (full
`aspnet:10.0-noble`) is ~280 MB and hits the t64 ABI transition on
`libcurl4` in noble apt.

## Required environment variables

`.env.example` is the source of truth. Every key marked **REQUIRED**
must be set or the api will refuse to start.

| Key | Source / how to generate | Notes |
| --- | --- | --- |
| `POSTGRES_PASSWORD` | `openssl rand -hex 32` | Used by both the `postgres` service and the api's connection string |
| `JWT_SIGNING_KEY` | `openssl rand -base64 32` | Base64-encoded HMAC-SHA256 key; **must be ≥ 32 bytes after decode** (the api fails fast on shorter keys) |
| `ALLOWED_ORIGIN` | `https://briscola.example.com` | The CORS origin you serve the SPA from. Defaults to `http://localhost:8080`; **must be set** in prod |
| `IMAGE_TAG` / `GHCR_NAMESPACE` | optional | Used by `docker compose pull` against ghcr.io |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | optional | When set, the api swaps the dev console exporter for OTLP |

## Backups

Snapshotting the `pgdata` volume is the safest path; here's the
manual `pg_dump` flow for ad-hoc backups.

```bash
# Stream a logical dump to a timestamped file on the host.
docker compose exec -T postgres \
    pg_dump -U briscola -Fc briscola \
    > backups/briscola-$(date -u +%Y%m%dT%H%M%SZ).dump

# Verify it isn't truncated — pg_restore --list reads the TOC.
docker run --rm -i postgres:17-alpine \
    pg_restore --list < backups/briscola-…dump | head -20
```

Restore (destructive — drops + recreates the database):

```bash
docker compose stop api
docker compose exec postgres \
    psql -U briscola -d postgres -c "DROP DATABASE briscola"
docker compose exec postgres \
    psql -U briscola -d postgres -c "CREATE DATABASE briscola"
docker compose exec -T postgres \
    pg_restore -U briscola -d briscola --no-owner --clean --if-exists \
    < backups/briscola-…dump
docker compose start api
```

Production cadence: hourly logical dumps via cron + a nightly full
volume snapshot. Retention 14 days for dumps, 30 days for snapshots.

## Database migrations

The api applies pending migrations on startup (`Migrations__RunOnStartup=true`
in the compose env). To preview without applying, run the same image
with the dotnet ef tooling pinned to the migrations assembly:

```bash
docker compose run --rm api \
    dotnet /app/Briscola.Infrastructure.Postgres.Migrations.dll list
```

To roll back a single migration on a paused service:

```bash
docker compose stop api
docker compose run --rm api \
    dotnet ef database update <previous-migration-name> \
        --project Briscola.Infrastructure.Postgres.Migrations.dll \
        --startup-project Briscola.Api.dll
docker compose start api
```

## Rolling restart

The api is stateless except for in-memory rooms (drained by the Phase 11.6
`GracefulShutdownHostedService` with a 10 s budget). For a no-downtime
deploy:

```bash
# 1. Pull the new image (CI publishes on tag — see the release workflow).
docker compose pull api frontend

# 2. Roll the api first. Compose will stop the old container, send
#    SIGTERM (ApplicationStopping fires; rooms drain), then start the
#    new one. Browsers see hub-disconnect events, the SignalR client's
#    reconnect logic kicks in.
docker compose up -d --no-deps --force-recreate api

# 3. Then the frontend.
docker compose up -d --no-deps --force-recreate frontend

# 4. Verify.
docker compose ps
curl -fsS http://localhost:8080/healthz
```

## JWT signing-key rotation

The api signs access tokens with a single HMAC key — there's no in-process
key-set rotation yet, so the rotation procedure is a *short outage*
window rather than a hot swap. Keep this for v1.1 if you need true
overlap; for v1 the steps are:

```bash
# 1. Generate a fresh key + edit .env.
NEW_KEY=$(openssl rand -base64 32)
sed -i "s|^JWT_SIGNING_KEY=.*|JWT_SIGNING_KEY=$NEW_KEY|" .env

# 2. Roll the api. Existing tokens will fail validation on the next
#    request → browsers refresh → some get bounced to /login.
docker compose up -d --no-deps --force-recreate api

# 3. Confirm.
curl -fsS http://localhost:8080/healthz
```

The refresh-token chain in the DB is independent of `JWT_SIGNING_KEY`
(refresh tokens are hashed at rest with SHA-256 — see
[security.md](./security.md)), so existing refresh tokens stay valid.
The next refresh issues a new access token signed with the new key.

## Upgrade dotnet 10 → 11 (or any major)

1. Bump `<TargetFramework>` in every csproj.
2. Bump the SDK + runtime base images in `backend/Dockerfile`.
3. Build + run the full test suite locally (`dotnet test Briscola.sln`).
4. Cut a release tag — CI builds + pushes the new images.
5. Roll the api per the rolling-restart procedure above.
6. Keep the previous image tag pinned for a fast rollback (`docker compose
   up -d --no-deps api` with `IMAGE_TAG=<old-tag>` in `.env`).

## Observability

- **Metrics:** the api exports the `Briscola` meter (`briscola.active_games`,
  `briscola.connected_players`, `briscola.moves_total`) plus AspNetCore +
  Runtime instrumentation. Set `OTEL_EXPORTER_OTLP_ENDPOINT` in `.env` to
  ship to a collector. In dev (no endpoint set) the meter prints to stdout.
- **Logs:** stdout in CompactJsonFormatter (`SerilogSetup` in production).
  Each line carries a `CorrelationId` property matching the request's
  `X-Correlation-Id` header (Phase 11.1).
- **Health:** `/healthz` (liveness — process up) and `/readyz` (readiness
  — DB reachable, 503 when not).

## Troubleshooting

- **`docker compose up` says api is unhealthy.** The chiseled runtime's
  HEALTHCHECK uses exec-form `["curl", "-fsS", "http://localhost:8080/healthz"]`.
  If you fork the image and lose curl (or shadow `/usr/bin/curl`), the
  probe will fail despite the api itself being fine. Run `docker logs
  elk-briscola-api-1` to confirm the app started; the probe error will
  show under `docker inspect --format '{{json .State.Health}}'`.
- **`Authentication:Jwt:SigningKey must be at least 32 bytes`.** You set
  `JWT_SIGNING_KEY` to a short literal; regenerate with
  `openssl rand -base64 32` so it's at least 32 bytes after decode.
- **Browser can't reach `/api/v1/...`.** You're loading the SPA from
  somewhere the nginx reverse proxy isn't in front of (e.g. the api's
  own swagger UI). Production traffic must go through the frontend
  service so `/api/*` and `/hubs/*` reach the api via the docker
  bridge.
- **Postgres "too many clients already" under stress.** The api's pool
  is bounded but a host that runs other Postgres clients on the same
  instance can exhaust connections. Either bump
  `max_connections` via a Postgres command override or split into a
  dedicated DB.



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
