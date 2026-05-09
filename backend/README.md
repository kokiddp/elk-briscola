# backend

The .NET 10 backend for elk-briscola. See the repo-root [README.md](../README.md) for architecture and the repo-root [TODO.md](../TODO.md) for the phased plan.

## Pinned package versions

These are the versions our csprojs reference today. When a `dotnet add package` bump is proposed, check this list — if a pin is non-obvious, the rationale is here.

| Package | Version | Why pinned |
|---|---|---|
| `Microsoft.EntityFrameworkCore.*` | 10.0.7 | Latest stable on the .NET 10 release line at the time of Phase 3. |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.1 | Matching .NET 10 line; lags slightly behind EF Core's own version. |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | 10.0.7 | Matches the framework. |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 10.0.7 | Matches the framework. |
| `Serilog` | 4.3.1 | Latest stable. |
| `Serilog.AspNetCore` | 10.0.0 | First release that targets ASP.NET Core 10. |
| `Serilog.Sinks.Console` | 6.1.1 | Latest stable. |
| `Serilog.Sinks.File` | 7.0.0 | Latest stable. |
| `Serilog.Formatting.Compact` | 3.0.0 | For prod JSON output via `CompactJsonFormatter`. |
| `BCrypt.Net-Next` | 4.0.3 | Lobby-game password hashing — distinct from ASP.NET Identity's PBKDF2. |
| `FluentAssertions` (test only) | 7.2.2 | **Last Apache-2.0 release before v8 commercial relicensing.** Don't bump without accepting the new license — see [AGENTS.md § Testing conventions](../AGENTS.md#testing-conventions). |
| `xunit` (test only) | 2.9.3 | What the .NET 10 `dotnet new xunit` template ships. |
| `Microsoft.NET.Test.Sdk` (test only) | 17.14.1 | Same. |
| `xunit.runner.visualstudio` (test only) | 3.1.4 | Same. |
| `coverlet.collector` (test only) | 6.0.4 | Same. |
| `Testcontainers.PostgreSql` (test only) | 4.11.0 | Drives the integration suite against a real Postgres container. |

## Building & testing locally

See the repo-root [README.md § Local development](../README.md#local-development) — TL;DR:

```bash
. ~/.elk-env.sh   # WSL only; puts Linux toolchain ahead of Windows shims
dotnet build backend/Briscola.sln --configuration Release
dotnet test  backend/Briscola.sln --configuration Release
```

`Briscola.sln` is intentionally the legacy `.sln` format (.NET 10 `dotnet new sln` defaults to `.slnx`; we forced `--format sln`).
