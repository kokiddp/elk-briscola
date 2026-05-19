# 4. Pluggable card sets

Date: 2026-05-19

## Status

Accepted

## Context

Briscola is played with several regional deck variants — Piacentine, Napoletane, Toscane, Trevigiane, Bergamasche, and French-suit decks. We need to support multiple sets without:

- Shipping every set in the SPA bundle (bloat — each set is ~40 SVGs).
- Coupling the engine to artwork (the engine speaks suits + ranks; rendering is a UI concern).
- Rebuilding the SPA whenever a new set is added.

We also need to ship without licensed artwork — first commits must compile + run, with a placeholder set that lets the engine + UI be validated end-to-end before any licensing work.

## Decision

Card sets are a server-hosted plug-in surface.

- **Location.** Each set lives under `backend/src/Briscola.Api/wwwroot/card-sets/{setId}/`. The static-files middleware serves them at `/card-sets/{setId}/{file}`.
- **Manifest.** Each set ships a `manifest.json` next to its assets:
  ```json
  {
    "id": "piacentine",
    "name": "Piacentine",
    "license": "Public Domain — <source>",
    "preview": "preview.png",
    "fileExtension": "svg",
    "placeholderOnly": false
  }
  ```
- **Discovery.** `GET /api/v1/card-sets` enumerates `wwwroot/card-sets/*/manifest.json` at boot. Adding a set is a content-only change: drop the folder, restart the API.
- **Engine isolation.** `Briscola.Domain` knows about suits + ranks only. The Angular `CardComponent` resolves a `(suit, rank)` pair to an asset URL via `CardSetService.assetUrlFor` using the active set's `fileExtension`. A missing file falls back to the `placeholder` set per-card.
- **Placeholder set.** Always shipped, always available. `manifest.placeholderOnly: true` flags sets that are SVG-only stand-ins (the v1 default).
- **User preference.** `PATCH /api/v1/me { activeCardSetId }` is persisted; the Angular `CardSetService` reads it on login and feeds it to every `CardComponent`.

## Consequences

**Positive.**

- The first commits compile + run with the placeholder set. Licensing work for real artwork is a parallel content track, not a blocker.
- A new set is one folder + a restart, with zero SPA code changes.
- Per-card fallback to `placeholder` means a half-imported set still renders (missing cards visibly fall back to the generic art) — better than a 404 hole or a broken page.
- The license field on every set is machine-readable, so an attribution page (or licensing audit) is trivially derivable.

**Negative / costs.**

- We pay an HTTP round-trip per unique card per game. Mitigated by `Cache-Control: public, immutable, max-age=31536000` on the assets — after first load the SVGs are cached for the session.
- The set is mutable at the *server* level: if an operator replaces a file under an existing `setId`, browsers will keep serving the cached version until the cache key changes. Cache keys are immutable: we recommend operators bump the `setId` (e.g. `piacentine-v2`) when assets change.
- Manifest validation is shallow today — duplicate IDs / missing previews are not caught at boot. The static-files middleware just 404s and the SPA falls back to placeholder. Acceptable for v1; a stricter loader could land in v2.

**Trade-offs explicitly accepted.**

- We don't support per-card-back customization. A future ADR could open that up, but v1 only varies the deck face artwork.
- We do not run an asset CDN — assets are served by the same nginx instance that serves the SPA. The card-set surface is small (<1MB per set) and traffic patterns are favorable.
