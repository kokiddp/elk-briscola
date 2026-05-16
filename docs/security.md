# Security

> Most of this doc remains a stub — the full threat model and security
> review checklist land in Phase 11. The current security model is
> documented in [README.md](../README.md#authentication-and-security).

## Frontend token storage (added in Phase 6)

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
- **If revisited in Phase 11:** move to `HttpOnly` refresh cookies with
  a paired CSRF token, behind an ADR. Until then, this remains the
  documented, knowingly-accepted shape.
