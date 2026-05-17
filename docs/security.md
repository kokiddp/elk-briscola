# Security

This document is the Phase 11.4 security walkthrough. Each line maps to a
test or a review note, so a future audit can replay it mechanically.

## Threat model — v1

- **Anonymous network attacker** (untrusted client traffic). Mitigations:
  - HTTPS-only in prod (`UseHttpsRedirection` + `UseHsts`).
  - JWT signatures (HMAC-SHA256, ≥32-byte key) on every authenticated
    REST + SignalR call.
  - Rate limits on the noisy hub methods (chat, play-card).
  - CSP / `X-Content-Type-Options` / `Referrer-Policy` / `Permissions-Policy`
    on every response (`SecurityHeadersMiddleware`).
- **Logged-in user attacker** (valid token, malicious intent). Mitigations:
  - Per-recipient redaction in `GameRoom.SnapshotForUser` — only the
    calling user's `MyHand` is ever serialized to that user's wire.
  - `GameHub.EnsureUserIsParticipantAsync` blocks any hub call against a
    game the caller isn't seated at.
  - Cheating attempts (playing a card not in your own hand) come back as
    `InvalidMove("CardNotInHand")` — covered by
    `DeepHubTests.Cheating_attempt_rejected_with_CardNotInHand`.
- **Compromised SPA (XSS)**. Mitigations + accepted residual risk: see
  ["Frontend token storage"](#frontend-token-storage) below.

## Checklist

| Item | Where it's enforced | Test |
| --- | --- | --- |
| No card data in non-self-targeted messages | `GameRoom.SnapshotForUser`, `GameRoom.SpectatorSnapshot` (Application layer) | `GameEventDispatcherTests.JoinGame_delivers_a_redacted_snapshot_to_the_caller`, `SpectatorAndLimitsTests.Spectator_receives_redacted_initial_state` |
| All hub callbacks validate caller identity vs. seat | `GameHub.EnsureUserIsParticipantAsync`, `GameHub.SpectateGame` | `GameHubTests` ("*not a participant*" cases), `DeepHubTests` cheating-attempt |
| Rate limit on chat | `GameHub.SendChat` → `TryAcquireRateLimit("SendChat", …)` | `RateLimitTests` (chat) |
| Rate limit on play-card | `GameHub.PlayCard` → `TryAcquireRateLimit("PlayCard", …)` | `RateLimitTests` (play) |
| HSTS / CSP / security headers | `SecurityHeadersMiddleware` | `SecurityHeadersTests.Healthz_response_carries_the_documented_security_headers` |
| Dependency audit fails on High/Critical CVE | `.github/workflows/backend.yml` (NuGet), `frontend.yml` (npm) | CI |
| JWT secret from env var only, fail-fast | `JwtIssuer` constructor throws on empty / short `Authentication:Jwt:SigningKey` | `JwtIssuerTests.Constructor_throws_when_signing_key_is_missing/short` |
| No sensitive data in logs | `[LoggerMessage]` templates scanned by `SensitiveLogContentTests` | `SensitiveLogContentTests.Template_does_not_log_forbidden_tokens` |
| Correlation-id header propagates server-side | `CorrelationIdMiddleware` | `CorrelationIdTests.Echoes_inbound_correlation_id_header` |
| Readiness probe surfaces DB outage | `HealthController.Readiness` (uses `Database.CanConnectAsync`) | `HealthControllerTests.Readiness_returns_503_when_db_unreachable` |
| Refresh-token replay is rejected and revokes the chain | `RefreshTokenService.RotateAsync` | `AuthFlowTests.Refresh_rotates_tokens_and_replayed_refresh_is_rejected` |
| Password change invalidates outstanding access tokens | `ApplicationUser.SecurityStamp` rotated; JWT carries it | `AuthFlowTests.Change_password_invalidates_outstanding_access_tokens` |

## How to walk this checklist

1. Run the test row(s) listed for the item:

   ```bash
   dotnet test backend/tests/Briscola.Api.IntegrationTests \
     -c Release --filter "FullyQualifiedName~SecurityHeadersTests"
   ```

2. Run the dependency audits exactly as CI does:

   ```bash
   # NuGet
   ( cd backend && dotnet list Briscola.sln package --vulnerable \
       --include-transitive ) | tee /tmp/nuget-audit.txt
   grep -E 'Severity: (High|Critical)' /tmp/nuget-audit.txt && exit 1

   # npm (production deps only)
   ( cd frontend && npm audit --omit=dev --audit-level=high )
   ```

3. Eyeball the security headers in dev:

   ```bash
   curl -i http://localhost:5080/healthz | grep -E '^(Content-Security|X-Content|Referrer|Permissions|Strict-Transport)'
   ```

## Frontend token storage

The Angular client (`AuthService`, `frontend/src/app/core/auth.service.ts`)
keeps the **access token** in memory only — never in `localStorage` /
`sessionStorage` / cookies. The **refresh token** is persisted in
`localStorage` under `elk-briscola.refreshToken`.

This is a deliberate trade-off, not an accident:

- **Benefit:** survives a full page reload without re-prompting the user;
  enables "remember me" on a single device with no server-side session
  store.
- **Cost:** any XSS vulnerability on the SPA can exfiltrate the refresh
  token. Cookies marked `HttpOnly; SameSite=Strict; Secure` would block
  that exfiltration path but require a backend session-cookie issuance
  flow that v1 does not implement.
- **Mitigations already in place:**
  - Refresh tokens are short-ish (default 14 d) and *rotated* on every
    use; a successful XSS would have to be both fast and silent to keep
    the chain alive across the next refresh from the legitimate tab.
  - The backend's `RefreshTokenService` detects chain replay (a token
    re-presented after rotation invalidates the entire chain).
  - CSP, output encoding, and the FluentValidation surface all narrow
    the XSS attack surface.
- **If revisited in v1.1:** move to `HttpOnly` refresh cookies with a
  paired CSRF token, behind an ADR. Until then, this remains the
  documented, knowingly-accepted shape.
