# API reference

The runtime surface is split into two channels: a versioned REST API for state mutation + querying, and two SignalR hubs for low-latency real-time pushes. Both auth flows reuse the same JWT access token. This document is the human-readable companion to the machine-generated specs:

- **OpenAPI v3** for REST: `/swagger/v1/swagger.json` (Swashbuckle-generated, served only in Development).
- **AsyncAPI v3** for SignalR: `/docs/asyncapi.json` (hand-maintained, mirrors `Briscola.Application.Orchestration.Events.*`).

Interactive UIs are mounted in dev only — see [README.md § REST API surface](../README.md#rest-api-surface).

---

## Versioning

All REST routes are prefixed with `/api/v1`. The v1 contract is frozen at v1.0.0; additive changes (new optional fields, new endpoints) ship in v1.x; breaking changes ship a `/api/v2` prefix. SignalR payloads carry no version negotiation in v1 — the server adds optional fields with `?` nullability and clients ignore unknown keys.

## Authentication

Tokens come from `POST /api/v1/auth/login` (or `register`). Pass them on every subsequent call:

| Channel | Mechanism |
|---|---|
| REST | `Authorization: Bearer <accessToken>` header. |
| SignalR | `accessTokenFactory` option of `@microsoft/signalr`, which appends `?access_token=<jwt>` to the negotiate / WebSocket handshake. |

Access tokens are short-lived (15 minutes); refresh tokens are single-use and rotate on every `POST /auth/refresh`. Refresh-token reuse triggers chain revocation server-side. See [docs/security.md](security.md) for the threat model.

---

## REST

### Auth

| Method | Path | Body | 200 / 201 response | Notes |
|---|---|---|---|---|
| `POST` | `/api/v1/auth/register` | `{ username, email, password, displayName? }` | `201` with `{ id, username }` + `Location: /api/v1/me` | Does **not** auto-authenticate. Client must follow up with `POST /auth/login` to get tokens. |
| `POST` | `/api/v1/auth/login` | `{ usernameOrEmail, password }` | `{ accessToken, refreshToken, expiresAt }` | Returns `401` with `invalid_credentials` on bad password. |
| `POST` | `/api/v1/auth/refresh` | `{ refreshToken }` | `{ accessToken, refreshToken, expiresAt }` | Old refresh token is single-use; reuse triggers chain revocation. |
| `POST` | `/api/v1/auth/logout` | `{ refreshToken }` | `204` | Invalidates the refresh-token chain. Rate-limited per user (30/min). |
| `POST` | `/api/v1/auth/change-password` | `{ currentPassword, newPassword }` | `204` | Bumps the user's `SecurityStamp`, invalidating outstanding access tokens. Rate-limited per user (5/15min). |

### Profile

| Method | Path | Notes |
|---|---|---|
| `GET` | `/api/v1/me` | Current user + active card-set id + current Elo. |
| `PATCH` | `/api/v1/me` | Partial: `{ displayName?, activeCardSetId? }`. |
| `GET` | `/api/v1/me/history?page=&size=` | Paginated `Finished` games, newest first. Each row carries `mySeatIndex` + `outcome` + `seatScores`. |
| `GET` | `/api/v1/me/ranking` | Elo + W/L/D + games-played. Auto-updated on every `Finished` game via `RankingService` (see [Phase 10.1](../TODO.md#step-101----profile-history--ranking-s-x)). |

### Card sets

| Method | Path | Notes |
|---|---|---|
| `GET` | `/api/v1/card-sets` | Installed manifests. The picker UI groups these into "available" + "preview-only" via the manifest's `placeholderOnly` flag. |

Static assets live under `/card-sets/{setId}/{rank}-{suit}.{ext}` (and `preview.png`). See [docs/card-sets.md](card-sets.md) for the manifest schema.

### Lobby

| Method | Path | Body | Notes |
|---|---|---|---|
| `GET` | `/api/v1/games?status=Open` | — | Joinable games. Mirrors the LobbyHub's `gameCreated` / `gameUpdated` pushes. |
| `GET` | `/api/v1/games?status=Running` | — | For spectator browsing + the in-nav "Resume game" derivation on the SPA. |
| `POST` | `/api/v1/games` | `{ mode: "TwoPlayer" \| "FourPlayerTeams", name?, isPrivate?, password? }` | Returns the seated `GameSummary`. Auto-transitions Open → Running when the last seat fills. |
| `POST` | `/api/v1/games/{id}/join` | `{ password? }` | Required if `isPrivate`. Seats the caller at the first free slot. |
| `POST` | `/api/v1/games/{id}/leave` | — | Open game only. **Lone-creator semantics:** if the leaver was the last seated player, the row transitions straight to `Abandoned` and the lobby gets a `GameEnded` push — no empty 0-of-N ghost row. Returns `409` if the game is `Running`. |
| `GET` | `/api/v1/games/{id}` | — | Public summary (used by the spectator handshake). |

### Spectator

The spectator path is hub-driven, not REST:

- Open `/hubs/game` with a JWT, then `invoke('SpectateGame', gameId)`.
- The server adds the connection to the `game:{id}:spectators` group and immediately invokes `Joined(snapshot)` with a redacted snapshot (no `MyHand` / `MyPozzo`, no `MySeatIndex`). Subsequent `StateUpdated` pushes carry the same shape.
- `invoke('UnspectateGame', gameId)` removes the connection. Closing the WebSocket has the same effect.

### Health & ops

| Method | Path | Notes |
|---|---|---|
| `GET` | `/healthz` | Liveness — returns `200` if the process is up. |
| `GET` | `/readyz` | Readiness — pings the DB. |
| `GET` | `/metrics` | Prometheus-format metrics. Custom counters: `briscola_active_games`, `briscola_connected_players`, `briscola_moves_total`, `briscola_invalid_moves_total{code}`. |

---

## SignalR

Two hubs share a single connection lifecycle but live at different paths. The Angular client keeps both up while the user is authenticated (see [LobbyService](../frontend/src/app/features/lobby/lobby.service.ts) / [GameService](../frontend/src/app/features/game/game.service.ts)).

### Conventions

- **Server → client** push names use PascalCase on the wire (SignalR's default) and are exposed as `camelCase` callbacks on the JS client.
- **Client → server** invokes are PascalCase strings (`SubscribeOpen`, `JoinGame`, …).
- **Payloads are immutable records**. Adding a field is non-breaking; renaming or removing one is a v2 change.

### LobbyHub (`/hubs/lobby`)

Drives the open-game list and lobby-wide chat. Authenticated connections call `SubscribeOpen` to opt into the `lobby:open` group.

#### Server → client

| Push | Payload | Notes |
|---|---|---|
| `gameCreated` | `GameSummary` | New Open game; append to lobby list. |
| `gameUpdated` | `GameSummary` | Seat-fill, partial leave, Open → Running transition, etc. |
| `gameStarted` | `gameId: Guid` | Open list drops it; the creator's SPA auto-routes to `/game/:id` via `LobbyService.lastStartedGameId`. |
| `gameEnded` | `gameId: Guid` | Fires for natural finishes AND for janitor-abandoned or lone-leaver-abandoned games. The SPA toasts "Your open game expired" only if the ended id matches the local pending game. |
| `chatMessage` | `{ id, fromUserId, fromDisplayName, scope: "Lobby", text, createdAt }` | Lobby chat. |

#### Client → server

| Invoke | Payload | Notes |
|---|---|---|
| `SubscribeOpen` | — | Join the `lobby:open` group. Idempotent. |
| `UnsubscribeOpen` | — | Leave the group; the disconnect handler also leaves implicitly. |
| `SendChat` | `text: string` | Server trims; whitespace-only is dropped; ≤500 chars after truncation. Rate-limited per user (5 / 10s). |

### GameHub (`/hubs/game`)

Per-game state pushes. Wire payloads correspond 1:1 to the events defined in `Briscola.Application.Orchestration.Events.*`; the hub layer's `*Dto` types are presentation-only mappings.

#### Server → client

| Push | Payload | Notes |
|---|---|---|
| `joined` | `RedactedStateForUser` | Initial snapshot tailored to the caller. Re-emitted on every `JoinGame` so reconnect is idempotent. |
| `stateUpdated` | `RedactedStateForUser` | Full per-recipient snapshot after each command. |
| `cardPlayed` | `{ seatIndex, card }` | Broadcast. |
| `trickResolved` | `{ winnerSeat, newSeatScores }` | Broadcast after the Nth play of a trick. |
| `cardsDrawn` | `{ countsBySeat, drawnCard?, targetUserId? }` | Emitted N times per resolution; `drawnCard` is non-null only on the variant routed to that player. |
| `phaseChanged` | `newPhase: "Dealing" \| "Playing" \| "LastHand" \| "Finished"` | Broadcast. |
| `gameFinished` | `{ outcome, seatScores, reason: "Normal" \| "ForfeitDisconnect" \| "ForfeitIdle" }` | Broadcast. |
| `playerDisconnected` | `{ seatIndex, graceDeadlineUtc }` | Broadcast. |
| `playerReconnected` | `seatIndex: int` | Broadcast. |
| `idleWarning` | `{ seatIndex, forfeitDeadlineUtc }` | Soft warning when the active seat has been idle past `Game:IdleWarnSeconds` (default 90s). **Note:** the per-turn countdown chip is now driven by `RedactedStateForUser.ActiveSeatForfeitDeadline` (which resets on every move), not by this push. The push is kept for legacy clients / future banners. |
| `chatMessage` | `{ id, fromUserId, fromDisplayName, scope: "Game" \| "Team", text, createdAt }` | Broadcast. |
| `rankingUpdated` | `{ userId, elo, wins, losses, draws, gamesPlayed }` | Fires once per affected user when a game ends. The SPA patches `seatPlayers[i].elo` in-place so the end-game dialog renders post-game Elo. |
| `invalidMove` | `code: string` | Targeted to the caller — see [§ InvalidMove codes](#invalidmove-error-codes-public-contract). |

##### `RedactedStateForUser` shape

| Field | Type | Notes |
|---|---|---|
| `gameId` | `Guid` | |
| `mode` | `"TwoPlayer" \| "FourPlayerTeams"` | |
| `phase` | `"Dealing" \| "Playing" \| "LastHand" \| "Finished"` | |
| `dealerSeat`, `leaderSeat`, `nextToPlaySeat` | `int` | |
| `trickNumber` | `int` | 1-indexed. |
| `briscolaCard` | `CardDto` | The face-up trump at the bottom of the stock. |
| `briscolaSuit` | `Suit` | |
| `stockCount` | `int` | Cards still face-down in the stock. |
| `handCountsBySeat` | `int[]` | Public per-seat hand sizes. |
| `myHand` | `CardDto[]?` | **Null for spectators** and for the no-myself path. |
| `myPozzo` | `CardDto[]?` | Populated only by `ViewOwnPile` during `LastHand`. |
| `currentTrick` | `PlayedCardDto[]` | Seat + card, in play order. |
| `seatScores` | `int[]` | |
| `outcome` | `GameOutcomeDto?` | Set when `phase == Finished`. |
| `mySeatIndex` | `int?` | Null for spectators. |
| `seatPlayers` | `(PlayerInfoDto?)[]` | Per-seat display name + Elo; null where the seat is empty. |
| `activeSeatForfeitDeadline` | `DateTimeOffset?` | UTC instant at which `nextToPlaySeat` auto-forfeits for idleness. Recomputed at every snapshot from `lastMoveCompletedAt + Game:IdleForfeitSeconds`; null outside `Playing` / `LastHand`. Drives the per-turn countdown chip in the table UI. |

#### Client → server

| Invoke | Payload | Notes |
|---|---|---|
| `JoinGame` | `gameId: Guid` | Player path. Idempotent — replaying it returns the current authoritative snapshot. |
| `SpectateGame` | `gameId: Guid` | Spectator path. Adds the caller to `game:{id}:spectators`. |
| `UnspectateGame` | `gameId: Guid` | Removes the caller. Disconnect does the same implicitly. |
| `PlayCard` | `gameId, card` | Engine validates; rejection comes back as `invalidMove(code)`. Rate-limited to 1/sec per connection. |
| `ViewOwnPile` | `gameId` | `LastHand` only; private `stateUpdated` with `myPozzo` populated. |
| `SendChat` | `gameId, text` | Spectators get `invalidMove("SpectatorsCannotChat")`. Rate-limited to 5 / 10s per user. |
| `LeaveGame` | `gameId` | **Open** → seat-clearing leave (collapses the row to `Abandoned` if it was the last player); **Running** → disconnect (starts the reconnect grace timer). |

### `InvalidMove` error codes (public contract)

The first five come from `Briscola.Domain.Errors.InvalidMoveCode` (engine); the last two are hub-only string literals.

| Code | Source | Meaning |
|---|---|---|
| `NotYourTurn` | engine | The caller is not the current `NextToPlaySeat`. |
| `CardNotInHand` | engine | The requested card is not in the caller's hand. |
| `GameFinished` | engine | The game is already over. |
| `WrongPhase` | engine | The action requires a different `GamePhase`. |
| `PileViewNotAllowed` | engine | `ViewOwnPile` called outside `LastHand`. |
| `SpectatorsCannotChat` | hub | A spectator attempted `SendChat`. |
| `RateLimited` | hub | `PlayCard` or `SendChat` exceeded their per-user rate limit. |

---

## Auto-generated specs

Both specs are generated as part of the API build:

- `backend/src/Briscola.Api/docs/asyncapi.json` — hand-maintained AsyncAPI v3 file, copied into the image.
- `/swagger/v1/swagger.json` — Swashbuckle-generated OpenAPI v3, served at runtime in Development.

To feed these into codegen, point your tooling at the dev box:

```bash
# OpenAPI → TypeScript axios client (or any other generator)
npx @openapitools/openapi-generator-cli generate \
  -i http://localhost:8080/swagger/v1/swagger.json \
  -g typescript-axios -o ./out/openapi-client

# AsyncAPI → docs HTML
npx @asyncapi/cli generate fromTemplate \
  http://localhost:8080/docs/asyncapi.json \
  @asyncapi/html-template@latest -o ./out/asyncapi-docs
```
