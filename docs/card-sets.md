# Card sets

elk-briscola separates the rules engine (which speaks `Suit` + `Rank` enums) from the visual *deck*. Switching deck art is a content-only change: drop a manifest + image folder, rebuild the backend image, the new set appears in the picker at `/profile`. This document is the reference for adding, validating, and licensing card sets.

## Directory layout

Every set lives in its own directory under the backend's static asset root:

```
backend/src/Briscola.Api/wwwroot/card-sets/
├── placeholder/         # bundled — always present
│   ├── manifest.json
│   ├── back.svg
│   ├── preview.svg
│   ├── bastoni-asso.svg
│   ├── bastoni-tre.svg
│   ├── …                # 40 cards total
│   ├── spade-due.svg
│   └── spade-tre.svg
└── <set-id>/
    ├── manifest.json
    ├── back.<ext>
    ├── preview.<ext>
    └── {suit}-{rank}.<ext>   # 40 cards
```

ASP.NET Core's static-file middleware serves them at `/card-sets/{setId}/{file}`. `GET /api/v1/card-sets` returns every manifest as JSON (with the server-derived `path` field), cached by the response-cache header so a reverse-proxy can hold it for 5 minutes.

## Manifest schema

```json
{
  "id": "piacentine",
  "name": "Piacentine",
  "license": "Public Domain — <source URL or attribution>",
  "preview": "preview.svg",
  "fileExtension": "svg",
  "filePattern": "{suit}-{rank}.{ext}",
  "back": "back.svg"
}
```

| Field | Required | Notes |
|---|---|---|
| `id` | yes | URL-safe slug, must match the directory name. The frontend stores this on the user's profile. |
| `name` | yes | Human-readable label shown in the picker. |
| `license` | yes | Free-form; intended for audit. Include the source for derivative works. |
| `preview` | yes | Filename of the thumbnail rendered in `/profile`. PNG or SVG. The doc-string for the placeholder picks a 600×280 montage; non-standard sizes are scaled with `aspect-ratio: 600 / 280; object-fit: contain`. |
| `fileExtension` | yes | Extension applied to `filePattern`'s `{ext}` placeholder. Supported: `svg`, `png`, `jpg`, `jpeg`, `webp`, `avif`. The static-files middleware stamps the right `Content-Type` for each; unknown extensions log a warning at boot and the browser may refuse to render them. All assets within one set must share the extension; mixing formats requires shipping multiple sets. |
| `filePattern` | yes | Card-asset filename template. Tokens: `{suit}`, `{rank}`, `{ext}`. The canonical pattern is `{suit}-{rank}.{ext}`. |
| `back` | yes | Filename of the card-back asset. |

The server-derived `path` (`/card-sets/<id>/`) is appended by `CardSetCatalog` when the manifest is read; you do **not** include it in the file on disk.

## Slugs for `{suit}` and `{rank}`

Lowercase, ASCII, hyphen-free.

| Domain enum | Filename slug |
|---|---|
| `Suit.Bastoni` | `bastoni` |
| `Suit.Coppe` | `coppe` |
| `Suit.Denari` | `denari` |
| `Suit.Spade` | `spade` |
| `Rank.Asso` | `asso` |
| `Rank.Tre` | `tre` |
| `Rank.Re` | `re` |
| `Rank.Cavallo` | `cavallo` |
| `Rank.Fante` | `fante` |
| `Rank.Sette` | `sette` |
| `Rank.Sei` | `sei` |
| `Rank.Cinque` | `cinque` |
| `Rank.Quattro` | `quattro` |
| `Rank.Due` | `due` |

That gives 40 card filenames per set (4 × 10).

## Missing-asset behaviour

The frontend's `CardSetService` registers the placeholder set as an always-available fallback. If the active set is missing a specific card (or `back.svg`, or `preview.<ext>`), the renderer's `(error)` handler swaps to `/card-sets/placeholder/<filename>`, and the service logs **one** warning per `(set, card)` pair to the browser console:

```
[card-sets] missing asset for Coppe/Re in set "piacentine"; falling back to placeholder
```

This means a *partial* set (e.g. art shipped suit-by-suit) still works in-game: every missing card falls back to a schematic, broken images are impossible. The startup validator on the backend (`Program.cs`) refuses to boot when the bundled `placeholder` directory is missing — the per-card fallback depends on it.

## Adding a new set

1. **Reserve a slug.** Pick a stable, URL-safe `id`. Use the directory name and the manifest `id` interchangeably.
2. **Create the directory.** `backend/src/Briscola.Api/wwwroot/card-sets/<id>/`.
3. **Write `manifest.json`** following the schema above. The license field is the *only* place attribution lives — keep it accurate.
4. **Drop the assets.** Either ship all 41 files at once (40 cards + `back` + `preview`) or stage them — the per-card fallback handles partial sets.
5. **Optional: pre-flight check.** `dotnet test backend/tests/Briscola.Api.IntegrationTests/Briscola.Api.IntegrationTests.csproj --filter CardSetCatalogTests` exercises the catalog scanner against any directory under `wwwroot/card-sets/`.
6. **Rebuild + smoke test.** Restart the API; hit `GET /api/v1/card-sets` and confirm the new entry appears. Open `/profile` in the SPA; the new tile shows up in the picker grid.

There is **no SPA rebuild** required: the registry is server-side, the picker fetches it at login.

## Licensing checklist (mandatory)

The project does not bundle uncleared third-party art. Before merging a new set:

- [ ] Source documented in `manifest.json#license` (URL or written attribution).
- [ ] Either Public Domain / CC0 / Apache-2.0 / MIT, **or** a commercial pack with the license file committed under `docs/licenses/<set-id>/`.
- [ ] No "internet pile" — files traced from an unknown origin are not acceptable.
- [ ] If the art is a derivative (e.g. recolour of a public-domain source), the lineage is noted.
- [ ] The 40 card filenames match the slug table above (preflight: `ls wwwroot/card-sets/<id>/*.svg | wc -l` reports 40).

`piacentine` ships an empty slot today — only the manifest exists. The first contributor to clear licensing (commissioned, public-domain reproduction, or licensed pack) drops the SVGs in and the picker lights up automatically.

## How the rest of the stack consumes it

- **Backend:** `CardSetCatalog` scans `wwwroot/card-sets/*/manifest.json` at startup, exposes `ICardSetCatalog` to the application layer for id-validation use cases. `Program.cs` fails fast if `placeholder` is missing.
- **Frontend:** `CardSetService` (`@core`/`card-sets`) fetches the list once after login, seeds `activeSet` from the user profile, and exposes a `CardSet { resolveFront(card), resolveBack() }` factory used by `<bri-card>`.
- **Profile UI:** `/profile` (`ProfileComponent`) renders one tile per registered set. Selecting a tile calls `CardSetService.setActiveSet(id)` which optimistically updates the signal and persists via `PATCH /api/v1/me` (reverting on failure).
- **In-game:** `<bri-card>` resolves the URL through `CardSetService.activeSet()`, and falls back to placeholder per missing card as described above.
