# elk-briscola

A production-ready implementation of the traditional Italian card game **Briscola**, with a C# (.NET 10) backend and an Angular 21 frontend. Supports 2-player and 4-player (fixed-pairs) modes with real-time multiplayer over SignalR.

> Briscola rules reference: https://it.wikipedia.org/wiki/Briscola

> The `elk-` prefix is the repo owner's chosen namespace — it has no functional meaning.

### For contributors and AI coding agents

If you are about to write code in this repository, **read [AGENTS.md](AGENTS.md) first.** It is the operating manual for any LLM or human implementing the plan: which document to read when, how to update [TODO.md](TODO.md), coding/testing/git conventions, and the things you must not do. The detailed phase-by-phase implementation plan lives in [TODO.md](TODO.md).

### Conventions used in this document

- **Game mode names:** `TwoPlayer` (2p) and `FourPlayerTeams` (4p) — these strings are the canonical wire format used in REST DTOs, SignalR messages, and the C# enum.
- **All timestamps are UTC.** No local times anywhere — server emits ISO-8601 with `Z`, client formats for display.
- **Seat ordering is the authoritative play order.** "Counter-clockwise around the table" is a UI hint, not a server concept. Seats are 0-indexed; play moves seat → seat+1 (mod count). The seat after the dealer leads.
- **Internationalization:** v1 ships English UI copy from `i18n/en.json`; the structure is in place to add `it.json` later. Card names render in Italian regardless of UI locale.

---

## Table of contents

1. [High-level architecture](#high-level-architecture)
2. [Tech stack](#tech-stack)
3. [Repository layout](#repository-layout)
4. [Game rules implemented](#game-rules-implemented)
5. [Domain model](#domain-model)
6. [Backend design](#backend-design)
7. [Frontend design](#frontend-design)
8. [Real-time protocol (SignalR)](#real-time-protocol-signalr)
9. [REST API surface](#rest-api-surface)
10. [Card sets (Piacentine and beyond)](#card-sets-piacentine-and-beyond)
11. [Authentication and security](#authentication-and-security)
12. [Configuration keys](#configuration-keys)
13. [Disconnect / reconnect handling](#disconnect--reconnect-handling)
14. [Database schema](#database-schema)
15. [Testing strategy](#testing-strategy)
16. [Local development](#local-development)
17. [Production deployment](#production-deployment)
18. [Definition of done](#definition-of-done)

---

## High-level architecture

```
┌─────────────────────────┐                   ┌─────────────────────────────┐
│   Angular SPA           │  REST + WS        │   ASP.NET Core 8 backend    │
│   (standalone, signals) │ ◄───────────────► │   ┌───────────────────────┐ │
│                         │                   │   │ Controllers (REST)    │ │
│  - Auth screens         │                   │   │ SignalR GameHub       │ │
│  - Lobby / game list    │                   │   │ SignalR LobbyHub      │ │
│  - Game table           │                   │   └─────────┬─────────────┘ │
│  - Card-set picker      │                   │             │               │
│  - Profile / history    │                   │   ┌─────────▼─────────────┐ │
└─────────────────────────┘                   │   │ Application services  │ │
                                              │   │  - GameOrchestrator   │ │
                                              │   │  - LobbyService       │ │
                                              │   │  - RankingService     │ │
                                              │   │  - MatchHistoryService│ │
                                              │   └─────────┬─────────────┘ │
                                              │             │               │
                                              │   ┌─────────▼─────────────┐ │
                                              │   │ Domain (pure C#)      │ │
                                              │   │  - BriscolaEngine     │ │
                                              │   │  - Card / Deck / Hand │ │
                                              │   │  - GameState / Trick  │ │
                                              │   └─────────┬─────────────┘ │
                                              │             │               │
                                              │   ┌─────────▼─────────────┐ │
                                              │   │ Infrastructure        │ │
                                              │   │  - EF Core DbContext  │ │
                                              │   │  - Identity store     │ │
                                              │   │  - Game state store   │ │
                                              │   └───────────────────────┘ │
                                              └──────────────┬──────────────┘
                                                             │
                                                ┌────────────▼─────────────┐
                                                │  SQLite (dev)            │
                                                │  PostgreSQL (prod)       │
                                                └──────────────────────────┘
```

**Design principles:**

- The **domain layer (`Briscola.Domain`)** is a pure C# library: no I/O, no DB, no SignalR. The rules engine is fully unit-testable in isolation.
- The **application layer (`Briscola.Application`)** orchestrates use cases and emits events that the API layer turns into REST responses or hub messages.
- The **API layer (`Briscola.Api`)** is the only project that knows about HTTP, SignalR, EF Core, Identity and JWT.
- All authoritative game state lives **server-side**. The client never trusts itself; every move is validated by the engine.

---

## Tech stack

| Layer | Choice | Rationale |
|---|---|---|
| Backend runtime | .NET 10 (LTS, GA Nov 2025) | Latest LTS, supported through Nov 2028 |
| Web framework | ASP.NET Core 10 (controllers + SignalR) | Idiomatic, fits the REST + realtime split |
| ORM | EF Core 10 | Provider-swappable (SQLite ↔ PostgreSQL) |
| Identity | ASP.NET Core Identity + JWT bearer | Username/password registration, JWT works for REST and SignalR |
| Database (dev) | SQLite | Zero-setup local dev |
| Database (prod) | PostgreSQL 16 | Free, robust, well-supported by EF Core |
| Migrations | EF Core migrations (per-provider) | Two migration projects, one per provider |
| Logging | Serilog → console + rolling file | Structured logs, easy to ship to ELK/Loki |
| Testing (BE) | xUnit + FluentAssertions + `WebApplicationFactory` + Testcontainers (Postgres) | Domain unit tests in-process; integration tests against a real Postgres container — no EF in-memory provider (its semantics drift from real DBs) |
| Frontend | Angular 21 standalone components, signals, SCSS | Modern, signal-based reactivity; latest stable as of May 2026 |
| Frontend state | Angular signals + small service stores | No NgRx — keeps the bundle and mental model small |
| Frontend HTTP | Angular `HttpClient` | Standard |
| Frontend WS | `@microsoft/signalr` | First-party SignalR client |
| Frontend tests | Vitest + Angular Testing Library | Default test runner shipped by `ng new` since Angular 20; signal-friendly, Jest-compatible APIs |
| E2E tests | Playwright | Drives two browser contexts for the multiplayer golden path |
| Containerization | Docker + docker-compose | API, frontend (nginx), Postgres |
| CI | GitHub Actions | Build + test + lint on every PR |

---

## Repository layout

```
elk-briscola/
├── README.md                       # this file
├── TODO.md                         # phased work plan
├── docker-compose.yml              # api + frontend + postgres for prod-like local
├── docker-compose.dev.yml          # postgres only, for hybrid dev
├── .editorconfig
├── .gitignore
├── .github/
│   └── workflows/
│       ├── backend.yml
│       ├── frontend.yml
│       └── e2e.yml
├── docs/
│   ├── architecture.md             # detailed C4-ish diagrams
│   ├── game-rules.md               # the canonical rules we implement
│   ├── api.md                      # REST + SignalR contract
│   ├── card-sets.md                # how to add a new card set
│   ├── deployment.md               # prod runbook
│   └── adr/                        # architecture decision records
│       ├── 0001-record-architecture-decisions.md
│       ├── 0002-server-authoritative-game-state.md
│       ├── 0003-signalr-over-raw-websockets.md
│       └── 0004-pluggable-card-sets.md
├── backend/
│   ├── Briscola.sln
│   ├── Directory.Build.props
│   ├── src/
│   │   ├── Briscola.Domain/                          # pure rules engine, no dependencies
│   │   ├── Briscola.Application/                     # use-case services, DTOs, interfaces
│   │   ├── Briscola.Infrastructure/                  # EF Core, Identity, persistence
│   │   ├── Briscola.Infrastructure.Sqlite.Migrations/    # EF Core migrations (SQLite)
│   │   ├── Briscola.Infrastructure.Postgres.Migrations/  # EF Core migrations (PostgreSQL)
│   │   └── Briscola.Api/                             # ASP.NET Core host: controllers + hubs
│   └── tests/
│       ├── Briscola.Domain.Tests/
│       ├── Briscola.Application.Tests/
│       └── Briscola.Api.IntegrationTests/
├── frontend/
│   ├── package.json
│   ├── angular.json
│   ├── src/
│   │   ├── app/
│   │   │   ├── core/               # auth, http interceptors, signalr client
│   │   │   ├── shared/             # ui primitives, pipes
│   │   │   ├── features/
│   │   │   │   ├── auth/           # login, register
│   │   │   │   ├── lobby/          # game list, create, join
│   │   │   │   ├── game/           # the table, hand, briscola, opponent area
│   │   │   │   ├── profile/        # card-set picker, history, ranking
│   │   │   │   └── spectate/       # read-only view of an ongoing game
│   │   │   ├── card-sets/          # CardSet interface + bundled sets
│   │   │   └── app.routes.ts
│   │   ├── assets/
│   │   │   └── card-sets/
│   │   │       ├── placeholder/    # ships in v1
│   │   │       └── piacentine/     # added once art is sourced
│   │   └── styles.scss
│   └── e2e/
│       └── playwright/
└── scripts/
    ├── seed-db.ps1
    └── reset-db.sh
```

---

## Game rules implemented

This is the canonical specification the engine enforces. It comes straight from the Wikipedia article and is also captured in [docs/game-rules.md](docs/game-rules.md).

### Deck

- 40 cards: 4 suits × 10 ranks.
- Suits: `Bastoni`, `Coppe`, `Denari`, `Spade` (Italian). French-suit equivalents are a **display** concern handled by the card-set, not the engine.
- Ranks (Italian names): `Asso, Tre, Re, Cavallo, Fante, Sette, Sei, Cinque, Quattro, Due`. The figures Re/Cavallo/Fante correspond to K/Q/J in French-suit decks; the displayed numeric "10/9/8" on French Briscola decks is purely cosmetic and **does not affect strength**.

### Card values (points)

| Card | Points |
|---|---|
| Asso | 11 |
| Tre | 10 |
| Re | 4 |
| Cavallo | 3 |
| Fante | 2 |
| Sette, Sei, Cinque, Quattro, Due | 0 |

Total points in a deck: **120**. Win threshold: **≥ 61**. Draw at exactly **60–60**.

### Trick-taking strength

Within the same suit, strongest to weakest:

`Asso > Tre > Re > Cavallo > Fante > Sette > Sei > Cinque > Quattro > Due`.

This ordering is **independent** of point value and of the K/Q/J/10/9/8 markings sometimes printed on French-suit Briscola decks. The engine looks up strength via a fixed table; rank enum members carry no implicit numeric strength.

### Setup

1. Shuffle the 40-card deck (Fisher-Yates with a seeded `IRandomSource` so games are replayable from `Games.ShuffleSeed` + the move log).
2. Pick a dealer at random (single-game sessions only in v1; no multi-hand rotation).
3. Dealer gives 3 cards to each player.
4. Dealer turns the next card face-up — the **briscola** card; its suit is the trump suit for the whole game.
5. The briscola card is placed perpendicular under the stock (tallone), visible to all players. It is the **last** card drawn from the stock.

### Turn order

- Play proceeds in seat order. Seats are 0-indexed; the seat *after the dealer in seat order* leads the first trick.
- After a trick, the trick winner leads the next one.
- The "counter-clockwise" geometry from the source rules is purely a UI/seating presentation; the server only knows seat indices.

### Playing a trick

1. The leader plays any card from their hand. Its suit is the **lead suit**.
2. Each subsequent player plays any card. **There is no obligation to follow suit** — Briscola does not require following the lead suit.
3. The trick is won by:
   - the highest **briscola** card played, if any briscola was played; otherwise
   - the highest card of the **lead suit**.
4. Cards of any other (non-briscola, non-lead) suit cannot win.

### Drawing

After a trick:
1. The winner gathers the played cards face-down into their pile (their **pozzo**).
2. The winner draws first from the stock, then the remaining players in seat order — each takes exactly one card.
3. The briscola card sits at the bottom of the stock; the player whose draw lands on it gets the briscola. By construction it is therefore the **last** card drawn.
   - In 2p: after the trick at which the stock has 1 card + the briscola, the trick winner takes the stock card and the loser takes the briscola.
   - In 4p: similarly, the briscola goes to whichever seat draws last in the trick that empties the stock.
4. Once the stock (including the briscola card) is exhausted, every player has exactly 3 cards. Play continues without drawing until all hands are empty (the **last-hand phase** — see below).

### Last-hand phase

The phase begins the moment the stock is empty and the briscola card has been drawn — i.e., when each player holds exactly 3 cards. Per Wikipedia, regional rules typically loosen at this point. We implement the **standard subset** explicitly chosen for v1:

- **2-player and 4-player:** every player may inspect their own `pozzo` (`viewOwnPile()` hub call) — disallowed in earlier phases.
- **4-player only:** in-game chat continues to be enabled (it always is); the UI highlights it as the **tactical phase**, signalling teammates that they may now coordinate via chat. The chat contents themselves are not engine-relevant.
- **Not implemented in v1 (deliberate, deferred):**
  - Viewing teammates' hands.
  - Card swaps with teammates.
  - Hard "no talking" enforcement outside the last-hand phase (we never enforce silence — chat is always allowed; this is a documentation/etiquette concern, not engine behavior).

These permissions are gated by a server-computed `Phase == LastHand` flag, derived from `Stock.Count == 0`.

### Scoring and winning

- After all 40 cards are played, sum the point values of cards in each player's (or team's) `pozzo`.
- Highest score wins; **60–60 is a draw**. The engine reports the result as a discriminated `GameOutcome { Winner(seatOrTeam) | Draw }` — the schema does not encode "winner" as nullable.
- Persisted to match history; Elo updated for ranked games.

### 4-player teams

- Fixed pairs by seat: seats `0` and `2` form **Team A**, seats `1` and `3` form **Team B**.
- 4-player games require exactly 4 humans to start; a partially-filled lobby cannot start. The lobby creator may cancel.
- Team scores are summed from per-seat scores. Match history records per-seat scores AND the team-level result.

### Game lifecycle

- A `Game` represents a single hand-out (one shuffle, one briscola, one play-through). v1 has no concept of best-of-N sessions or multi-hand matches.
- A game in `Open` status auto-transitions to `Running` the moment the seat count is filled (2 or 4 depending on mode). No manual "start" button.

---

## Domain model

Pure C# types in `Briscola.Domain`, no framework dependencies.

```csharp
public enum Suit { Bastoni, Coppe, Denari, Spade }

// Rank members carry no implicit numeric semantics. Strength and points are looked up
// via static tables, NOT via the enum's underlying integer value.
public enum Rank { Asso, Tre, Re, Cavallo, Fante, Sette, Sei, Cinque, Quattro, Due }

public enum GameMode  { TwoPlayer, FourPlayerTeams }
public enum GamePhase { Dealing, Playing, LastHand, Finished }
public enum GameStatus { Open, Running, Finished, Abandoned }

public readonly record struct Card(Suit Suit, Rank Rank);

public static class CardTables
{
    // Trick-taking strength: higher value beats lower within the same suit.
    public static int Strength(Rank r) => r switch
    {
        Rank.Asso    => 10,
        Rank.Tre     => 9,
        Rank.Re      => 8,
        Rank.Cavallo => 7,
        Rank.Fante   => 6,
        Rank.Sette   => 5,
        Rank.Sei     => 4,
        Rank.Cinque  => 3,
        Rank.Quattro => 2,
        Rank.Due     => 1,
    };

    public static int Points(Rank r) => r switch
    {
        Rank.Asso    => 11,
        Rank.Tre     => 10,
        Rank.Re      => 4,
        Rank.Cavallo => 3,
        Rank.Fante   => 2,
        _            => 0,
    };
}

public sealed record PlayedCard(int SeatIndex, Card Card);

public abstract record GameOutcome
{
    public sealed record Winner(int SeatOrTeam) : GameOutcome;     // seat index in 2p, team id in 4p
    public sealed record Draw() : GameOutcome;
}

public sealed class GameState
{
    public Guid GameId { get; init; }
    public GameMode Mode { get; init; }
    public int DealerSeat { get; init; }
    public IReadOnlyList<Seat> Seats { get; init; }       // 2 or 4
    public Suit BriscolaSuit { get; init; }
    public Card BriscolaCard { get; init; }               // shown perpendicular under the stock
    public IReadOnlyList<Card> Stock { get; init; }       // tail = briscola card
    public IReadOnlyList<PlayedCard> CurrentTrick { get; init; }
    public int LeaderSeat { get; init; }
    public int NextToPlaySeat { get; init; }
    public GamePhase Phase { get; init; }
    public IReadOnlyList<int> SeatScores { get; init; }   // per seat; team scores derived
    public GameOutcome? Outcome { get; init; }            // set only when Phase == Finished
}

public interface IBriscolaEngine
{
    GameState StartGame(GameSetup setup, IRandomSource rng);
    GameState PlayCard(GameState state, int seatIndex, Card card);  // throws InvalidMoveException
    bool IsLegalMove(GameState state, int seatIndex, Card card);
    TrickResult? ResolveTrickIfComplete(GameState state);
}
```

The engine is **deterministic** given an `IRandomSource` (seedable for tests). State transitions are pure functions returning new `GameState` values — no mutation of inputs. The engine asserts at end-of-game that **scores sum to 120**; if they don't, it throws — this is a defensive in-production invariant, not just a test.

---

## Backend design

### Projects

- **Briscola.Domain** — rules engine, value types, no external deps.
- **Briscola.Application** — services, DTOs, ports: `IGameRepository`, `IChatRepository`, `IRankingRepository`, `IUserContext`, `IClock`, `IRandomSource`. Implementations live in `Infrastructure`.
- **Briscola.Infrastructure** — EF Core `BriscolaDbContext`, repositories, Identity stores, JWT issuance, Serilog config.
- **Briscola.Api** — controllers, SignalR hubs, DI composition, `Program.cs`.

### Game state ownership

- One in-memory `GameRoom` per active game, behind a `GameOrchestrator` keyed by `GameId`.
- Each `GameRoom` serializes incoming moves through a `Channel<GameCommand>` so a single worker mutates state — no locks scattered around the engine.
- After every state transition, the new state is persisted (snapshot + last move) so that a server restart can rehydrate active games.
- The orchestrator publishes diff events to the SignalR group `game-{gameId}`.

### Why a snapshot store rather than event sourcing?

For 40-card games this is overkill. We persist:
- a compact JSON snapshot of `GameState` after each move (column on `Games`), and
- a `GameMoves` append-only log for replay/debug/match history.

This gives us replayability without an event-sourcing framework.

---

## Frontend design

### Routing

```
/login
/register
/lobby                 # list of open games + create button
/game/:gameId          # the table
/game/:gameId/spectate # read-only
/profile               # card-set picker, history, ranking
```

### State

- `AuthService` (signal: `currentUser`)
- `LobbyService` (signal: `openGames`, populated via SignalR `LobbyHub`)
- `GameService` (signals: `gameState`, `myHand`, `legalMoves`, populated via `GameHub`)
- `CardSetService` (signal: `activeSet`, persisted in user profile + localStorage fallback)

### Components

- `LobbyPage` — list, filter by mode, create-game dialog, join button.
- `GameTablePage` — composes `OpponentArea`, `BriscolaIndicator`, `Stock`, `TrickArea`, `MyHand`, `Scoreboard`, `ChatPanel`.
- `Card` — renders via the active `CardSet`'s asset resolver.
- `CardSetPicker` — preview thumbnails, select active set.
- `MatchHistory`, `RankingBadge`.

### UX guarantees

- Cards animate from stock → hand on draw, hand → trick on play, trick → pile on resolution.
- The briscola card stays visibly perpendicular under the stock until it's the last drawn.
- Illegal moves are prevented client-side AND rejected server-side (defense in depth).
- During `LastHandPhase`, the UI surfaces the "look at your pile" action and (in 4p) highlights the chat as the tactical channel.

---

## Real-time protocol (SignalR)

Two hubs:

### LobbyHub (`/hubs/lobby`)

Server → client:
- `gameCreated(GameSummary)`
- `gameUpdated(GameSummary)` (joins, leaves)
- `gameStarted(gameId)` — also removes from open list
- `gameEnded(gameId)` — for any spectator listings
- `chatMessage(LobbyChatMessage)`

Client → server:
- `subscribeOpen()`
- `sendChat(text)`

### GameHub (`/hubs/game`)

Server → client:
- `joined(GameStateForPlayer)` — initial snapshot tailored to the calling player (their own hand visible, others' hidden)
- `stateUpdated(GameStateDiff)`
- `cardPlayed(PlayedCardEvent)`
- `trickResolved(TrickResolvedEvent)`
- `cardsDrawn(DrawnEvent)` — counts only, not specific cards (except for the calling player)
- `phaseChanged(GamePhase)`
- `gameFinished(GameResult)`
- `playerDisconnected(seatIndex, graceDeadlineUtc)`
- `playerReconnected(seatIndex)`
- `chatMessage(GameChatMessage)`

Client → server:
- `joinGame(gameId)` (also re-used for reconnect)
- `playCard(card)` — server validates against engine; rejection returns an `InvalidMove` error
- `viewOwnPile()` — only allowed in `LastHandPhase`; server returns the pile contents
- `sendChat(text)`
- `leaveGame()`

**Authentication:** the Angular client uses `@microsoft/signalr`'s `accessTokenFactory` option to supply the JWT on each (re)connect; ASP.NET Core's SignalR JWT integration accepts the token via the `access_token` query string for the WebSocket handshake. Identity then flows through `Context.UserIdentifier`.

**Spectator chat policy:** spectators **read** game chat but cannot post. They have no team channel. This is enforced server-side in the hub method, not just the UI.

**Rate limiting on hub methods:** `playCard` is throttled to 1/second per connection (anti-spam, not a real anti-cheat mechanism — the engine rejects illegal moves anyway). `sendChat` is throttled to 5 messages / 10 seconds per user.

### `InvalidMove` error codes (public contract)

The hub's `invalidMove` push delivers a stable string code. Clients are expected to handle these by name; do not parse messages.

| Code | Meaning |
|---|---|
| `NotYourTurn` | The caller is not the current `NextToPlaySeat`. |
| `CardNotInHand` | The requested card is not in the caller's hand (anti-cheat / desync). |
| `GameFinished` | The game is already over. |
| `WrongPhase` | The action requires a different `GamePhase` than the current one. |
| `PileViewNotAllowed` | `viewOwnPile()` was called outside `LastHand`. |
| `SpectatorsCannotChat` | A spectator attempted `sendChat`. |
| `RateLimited` | The caller exceeded a hub-method rate limit. |

---

## REST API surface

All under `/api/v1`. Full OpenAPI spec generated by Swashbuckle at `/swagger`.

### Auth
- `POST /auth/register` — `{ username, email, password }` → `201`
- `POST /auth/login` — `{ usernameOrEmail, password }` → `{ accessToken, refreshToken, expiresAt }`
- `POST /auth/refresh` — `{ refreshToken }` → new tokens
- `POST /auth/logout` — invalidates refresh token

### Profile
- `GET /me` — current user, active card set, ranking
- `PATCH /me` — update display name, active card set
- `GET /me/history?page=&size=` — paginated match history
- `GET /me/ranking` — ELO + W/L

### Card sets
- `GET /card-sets` — list installed sets (id, name, license, preview image)
- (Card set assets are static files served from `/assets/card-sets/{id}/...`)

### Lobby
- `GET /games?status=open` — list joinable games (i.e., not started and not ended). Also pushed via `LobbyHub`.
- `GET /games?status=running` — list ongoing games (for spectator browsing).
- `POST /games` — `{ mode: 'TwoPlayer' | 'FourPlayerTeams', name, isPrivate?, password? }` → `GameSummary`. Status is `Open` until seats fill, then auto-transitions to `Running`.
- `POST /games/{id}/join` — `{ password? }` (required if `isPrivate`). Joins the next free seat; SignalR push follows.
- `POST /games/{id}/leave` — leaves before start (returns 409 if `Running`).
- `GET /games/{id}` — public summary (used for spectator check + reconnect handshake).

### Spectator
- `POST /games/{id}/spectate` — adds caller to spectators group. Server pushes redacted state (no hands, only counts).

### Health & ops
- `GET /healthz` — liveness
- `GET /readyz` — readiness (DB ping)

---

## Card sets (Piacentine and beyond)

The card-rendering layer is pluggable: adding a new set is a content-only change (drop files server-side; no rebuild of the SPA).

### Server-hosted assets

Card sets live under **`backend/src/Briscola.Api/wwwroot/card-sets/{setId}/`**. The backend serves them as static files at `/card-sets/{setId}/{file}`. Each set ships with a `manifest.json`:

```json
{
  "id": "piacentine",
  "name": "Piacentine",
  "license": "Public Domain — <source>",
  "preview": "preview.png",
  "fileExtension": "svg",
  "filePattern": "{suit}-{rank}.{ext}",
  "back": "back.svg"
}
```

`{suit}` ∈ `bastoni|coppe|denari|spade`; `{rank}` ∈ `asso|tre|re|cavallo|fante|sette|sei|cinque|quattro|due` (lowercase, ASCII).

The backend exposes the **list** of installed sets at `GET /api/v1/card-sets`, which scans `wwwroot/card-sets/*/manifest.json` at startup. The user's preferred set id is stored on their profile and returned on `GET /me`.

### Client side

```ts
export interface CardSet {
  id: string;
  name: string;
  resolveFront(card: Card): string;   // -> /card-sets/{id}/<filename>
  resolveBack(): string;
}
```

A `CardSetRegistry` builds a `CardSet` per manifest fetched from the backend. The `<bri-card>` component reads the active set from `CardSetService` and renders the correct asset. If a specific card asset is missing from a set, the resolver **falls back to the `placeholder` set** for that single card and logs a warning — no broken images.

### v1 ships with

- `placeholder` — schematic SVGs (suit symbol + Italian rank label + back). Functional, visually clear, zero licensing risk. Bundled with the backend image.
- `piacentine` — directory present with `manifest.json` only; no art yet. The fallback above keeps the picker option visible without showing broken cards.

> **Asset sourcing note:** the project will not bundle Piacentine art whose license cannot be verified. The TODO has a content task to either (a) commission an artist, (b) source a verified public-domain reproduction, or (c) license a commercial pack. The plug-in architecture means this is a content task, not engineering.

---

## Authentication and security

### Identity & tokens

- Passwords hashed with the ASP.NET Identity default (`PasswordHasher<TUser>`, PBKDF2-SHA256, 100k iterations).
- JWT signing key supplied via configuration (`Authentication:Jwt:SigningKey`); never committed; rotation procedure in `docs/deployment.md`.
- Access token: 15 minutes. Refresh token: 14 days, rotating, **stored hashed** in the DB.
- Logout invalidates the refresh token. Access tokens remain valid until natural expiry (≤ 15 min); v1 does **not** maintain a JWT revocation list. This is the standard, documented trade-off — for forced cross-device sign-out a user changes their password (which rotates a server-side `SecurityStamp`, invalidating issued access tokens via stamp validation).
- All endpoints require `[Authorize]` except `POST /auth/register`, `POST /auth/login`, `POST /auth/refresh`, `GET /healthz`, `GET /readyz`, and the static card-set asset paths.

### Account policy

- Username: 3–32 chars, `[a-zA-Z0-9_-]`, unique (case-insensitive).
- Email: standard RFC validation, unique.
- Password: minimum 10 characters, must include at least one letter and one digit. No upper-bound complexity requirements (counterproductive vs. length).
- Display name: 1–32 chars, defaults to username.

### Network & headers

- CORS: explicit allowlist of frontend origins (config-driven, comma-separated).
- HTTPS enforced in production with HSTS (max-age 1 year, includeSubDomains, preload).
- CSP: `default-src 'self'; img-src 'self' data:; connect-src 'self' wss:` (tightened in the nginx config that fronts the SPA).
- `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy: ()`.

### Anti-cheat / data minimization

- **Server is authoritative.** The client never asserts what is in any hand — it requests "play `{Suit, Rank}` from my hand"; the server verifies that this exact card is in the caller's hand for the current seat.
- State diffs sent to a player redact other players' hands (counts only).
- Spectators receive an even more redacted view: counts only for everyone.
- Trick contents in `pozzo` are revealed to a player only via `viewOwnPile()` and only when `Phase == LastHand`.

### Rate limits

| Surface | Limit |
|---|---|
| `POST /auth/login` | 5/min per IP, 10/hour per username |
| `POST /auth/register` | 3/hour per IP |
| `POST /auth/refresh` | 30/min per user |
| `playCard` (hub) | 1/sec per connection |
| `sendChat` (hub) | 5 messages / 10 sec per user |

### Input validation

- FluentValidation on every DTO; failures map to RFC 7807 `ProblemDetails`.
- Hub method arguments are validated identically (extracted helpers, not duplicated rules).

### Logging

- Serilog structured, JSON to stdout in prod.
- Never log: passwords, tokens (access or refresh), refresh-token hashes, full hand contents, full chat message bodies (length and message-id only at INFO; full body at DEBUG, opt-in).
- Every request gets a correlation id (`X-Correlation-Id`, generated if absent), propagated to logs and downstream calls.

---

## Configuration keys

Every configurable value is an environment variable using the ASP.NET Core double-underscore convention (`Section__Key`). Defaults are listed; production deployments must explicitly set the `Authentication__Jwt__SigningKey` and `ConnectionStrings__Default` (no defaults).

### `Game` — gameplay timers and policies

| Key | Default | Notes |
|---|---|---|
| `Game__ReconnectGraceSeconds` | `120` | Seconds to wait for a disconnected player before forfeiting. |
| `Game__IdleWarnSeconds` | `90` | Soft warning threshold for idle on a turn. |
| `Game__IdleForfeitSeconds` | `180` | Hard forfeit threshold for idle on a turn. |
| `Game__OpenLobbyTtlMinutes` | `60` | After this, an unfilled `Open` game is moved to `Abandoned`. |
| `Game__FourPlayerForfeitMode` | `TeamForfeit` | One of `TeamForfeit` or `WaitForBackup` (v1 ships `TeamForfeit`). |

### `Authentication__Jwt`

| Key | Default | Notes |
|---|---|---|
| `Authentication__Jwt__Issuer` | `briscola` | JWT `iss` claim. |
| `Authentication__Jwt__Audience` | `briscola` | JWT `aud` claim. |
| `Authentication__Jwt__SigningKey` | — | **Required.** Base64-encoded ≥ 32 bytes. |
| `Authentication__Jwt__AccessTokenLifetimeMinutes` | `15` | |
| `Authentication__Jwt__RefreshTokenLifetimeDays` | `14` | |

### `ConnectionStrings`

| Key | Default | Notes |
|---|---|---|
| `ConnectionStrings__Provider` | `Sqlite` | One of `Sqlite` or `Postgres`. |
| `ConnectionStrings__Default` | — | **Required.** Connection string for the chosen provider. |

### `Cors`

| Key | Default | Notes |
|---|---|---|
| `Cors__AllowedOrigins` | — | Comma-separated origins; required when frontend is on a different origin. |

### `Migrations`

| Key | Default | Notes |
|---|---|---|
| `Migrations__RunOnStartup` | `false` | Set `true` in dev / single-instance prod to apply migrations at boot. |

### Observability

| Key | Default | Notes |
|---|---|---|
| `Otel__ExporterEndpoint` | — | OTLP endpoint URL; if unset, traces go to console. |
| `Logging__LogLevel__Default` | `Information` | Standard ASP.NET logging knob. |

---

## Disconnect / reconnect handling

All timers are **server-authoritative**, driven by `IClock` (so they are unit-testable). The client only displays the deadline it was told.

1. SignalR `OnDisconnectedAsync` notifies the relevant `GameRoom`.
2. Game enters `Paused` substate; a `ReconnectDeadline = clock.UtcNow + Game:ReconnectGraceSeconds` is set and broadcast as `playerDisconnected(seatIndex, graceDeadlineUtc)`.
3. **Default `ReconnectGraceSeconds = 120`**, configurable via env var `Game__ReconnectGraceSeconds`.
4. Other players' UI shows a countdown banner.
5. If the player reconnects before the deadline (re-authenticates, re-joins the hub group, calls `joinGame`), the orchestrator pushes the latest state and resumes.
6. If the deadline passes:
   - **2-player:** the disconnected player forfeits; the opponent wins; match recorded as `EndedReason: ForfeitDisconnect`.
   - **4-player:** the disconnected player's **team** forfeits. (Configurable via `Game__FourPlayerForfeitMode = TeamForfeit | WaitForBackup`; default `TeamForfeit`.)
7. A separate **idle timer** for "stuck on a player's turn" (player connected but not playing): soft warning at `Game:IdleWarnSeconds` (default 90), forfeit at `Game:IdleForfeitSeconds` (default 180). Same forfeit semantics as above.
8. Reconnect is **idempotent**: replaying `joinGame` returns the current authoritative snapshot every time, so flaky networks don't desync.

---

## Database schema

EF Core entities. Migrations are produced per provider (separate migration assemblies under `Briscola.Infrastructure`, selected at startup by `ConnectionStrings:Provider` ∈ `Sqlite | Postgres`). All timestamp columns are UTC, stored as `timestamptz` on Postgres / ISO-8601 text on SQLite.

```
Users (Identity)
  Id, UserName, NormalizedUserName, Email, NormalizedEmail, PasswordHash,
  SecurityStamp, ConcurrencyStamp, ...
  DisplayName, ActiveCardSetId, CreatedAt

RefreshTokens
  Id, UserId, TokenHash, ExpiresAt, RevokedAt, ReplacedByTokenId?

Games
  Id, Mode, Name, Status (Open|Running|Finished|Abandoned),
  CreatedByUserId, CreatedAt, StartedAt, EndedAt,
  ShuffleSeed (long, persisted at game start for replayability),
  StateSnapshot (JSON), BriscolaSuit,
  IsPrivate, PasswordHash?

GameSeats
  GameId, SeatIndex, UserId, JoinedAt, LeftAt?

GameMoves
  Id, GameId, MoveIndex, SeatIndex, MoveType, Payload (JSON), CreatedAt
  // MoveType ∈ { PlayCard, Forfeit, Disconnect, Reconnect, IdleTimeout }
  // Chat is NOT in GameMoves; it lives in ChatMessages.

GameResults
  GameId, Outcome (Win|Draw), WinnerKey? (seat for 2p, team for 4p),
  SeatScoresJson, TeamScoresJson?, EndedReason (Normal|ForfeitDisconnect|ForfeitIdle)

ChatMessages
  Id, Scope (Lobby|Game), GameId?, UserId, Text, CreatedAt
  // No Team scope at the storage layer: 4p team-tactical phase reuses Game scope;
  // the "tactical" highlight is a UI affordance, not a server filter.

Rankings
  UserId, Elo, Wins, Losses, Draws, GamesPlayed, UpdatedAt
```

Indexes on hot lookups: `Games(Status)`, `GameSeats(UserId)`, `GameSeats(GameId, SeatIndex)` unique, `GameMoves(GameId, MoveIndex)` unique, `ChatMessages(GameId, CreatedAt)`, `RefreshTokens(UserId)`, `RefreshTokens(TokenHash)` unique.

**Replayability:** `Games.ShuffleSeed` + ordered `GameMoves` rows are sufficient to reproduce any game deterministically — useful for support and post-mortem debugging.

---

## Testing strategy

### Backend

- **Unit (Briscola.Domain.Tests)** — exhaustive engine coverage:
  - Card point values and trick-taking strength tables.
  - Setup deals exactly 3 cards each, briscola card visible, stock has correct count.
  - "No must-follow-suit" verified.
  - Trick winner: lead suit only / briscola wins / higher briscola beats lower.
  - Draw order after trick: winner first, then turn order.
  - Last card from stock is the briscola card.
  - End-of-stock continues until hands empty.
  - Total points always sum to 120.
  - 4-player team scoring.
  - `LastHandPhase` triggers correctly.
  - Property tests with seeded random: any random sequence of legal plays terminates in a valid finished state.
- **Application tests** — orchestrator behavior under concurrent commands (single-writer guarantee), reconnect/forfeit timers (with a fake `IClock`), ranking math.
- **Integration tests (Briscola.Api.IntegrationTests)** — `WebApplicationFactory` + Testcontainers Postgres:
  - Auth flow (register → login → refresh → access protected endpoint).
  - Lobby create/join/leave end to end.
  - SignalR hub: drive a 2-player game from two test connections, assert end state.
  - Spectator sees redacted state.
  - Disconnect during turn → reconnect within deadline → game continues.
  - Disconnect → deadline elapses → forfeit recorded.

### Frontend

- **Unit/component (Vitest + Testing Library)** — auth forms, lobby list, card rendering with a fake CardSet, hand component disables illegal plays.
- **Service tests** — `GameService` against a mock SignalR connection.

### E2E (Playwright)

- Two-browser-context test: register two users, both join the same game, play to completion, assert the winner banner.
- Reconnect test: kill one browser context mid-game, re-launch, verify state restored.

### CI gates

- Build green, all backend tests green, frontend tests green, Playwright golden path green, lint clean (dotnet format + ESLint), no high-severity `npm audit`/`dotnet list package --vulnerable` findings.

---

## Local development

### Prerequisites

- .NET 10 SDK
- Node 22+ (LTS)
- (Optional, for prod-like local) Docker

### Backend (SQLite, fastest path)

```bash
cd backend
dotnet restore
dotnet ef database update --project src/Briscola.Infrastructure --startup-project src/Briscola.Api
dotnet run --project src/Briscola.Api
# API at http://localhost:5080
# Swagger at http://localhost:5080/swagger
```

### Frontend

```bash
cd frontend
npm install
npm start
# Angular dev server at http://localhost:4200, proxies /api and /hubs to :5080
```

### Postgres locally

```bash
docker compose -f docker-compose.dev.yml up -d
# then run backend with ConnectionStrings:Provider=Postgres
```

### Tests

```bash
# backend
cd backend && dotnet test

# frontend
cd frontend && npm test

# e2e (requires both servers running, or use the all-in-one compose)
cd frontend && npm run e2e
```

---

## Production deployment

- **Build artifacts:**
  - Backend: multi-stage Dockerfile — `dotnet publish -c Release` (framework-dependent) into the official `mcr.microsoft.com/dotnet/aspnet:10.0` runtime image. Non-root user, healthcheck wired to `/healthz`.
  - Frontend: `ng build --configuration production` → static files served by nginx (alpine), with gzip + long-cache on hashed assets and `index.html` no-cache.
- **docker-compose.yml** brings up `api`, `frontend` (nginx with proxy_pass to api), and `postgres`.
- **Configuration via env vars only.** Secrets (`Authentication__Jwt__SigningKey`, DB password) are never in the repo.
- **Migrations** run on startup behind a feature flag (`Migrations:RunOnStartup=true`). For larger deployments, run them as a separate job.
- **Observability:** Serilog → stdout (JSON) for container log shipping; `/healthz` and `/readyz` for orchestrators; OpenTelemetry instrumentation (ASP.NET Core + EF Core) ready to ship traces.
- **Backups:** Postgres dump cron job (documented in `docs/deployment.md`); retention 14 days.
- **Zero-downtime deploys:** stateful games complicate this — the API persists snapshots after every move, so a rolling restart resumes games on the new instance. Document the procedure.

---

## Definition of done

The product is "production ready" only when ALL of the following are true:

- [ ] All rules in [Game rules implemented](#game-rules-implemented) verified by unit tests, including the score-sums-to-120 invariant enforced at runtime.
- [ ] Two-player and four-player (fixed pairs) flows playable end to end via the UI.
- [ ] Auth: register, login, refresh, logout, password change. Password change rotates `SecurityStamp`, invalidating outstanding access tokens.
- [ ] Lobby: create / list / join / leave; updates pushed in real time. Auto-start when seats fill.
- [ ] In-game: all SignalR events implemented, both directions; per-recipient redaction proven by tests.
- [ ] Disconnect/reconnect with grace timer (default 120s, configurable), idle timer, forfeit on timeout — covered by integration tests.
- [ ] Spectator mode: read-only, redacted hands, read-only chat (no posting).
- [ ] Chat: lobby + in-game; rate-limited; tactical-highlight UI in 4p last-hand phase.
- [ ] Match history persisted with `ShuffleSeed` + move log (replayable).
- [ ] Elo updated after each completed match (forfeits count as a loss for the forfeiting side).
- [ ] Card-set picker working; `placeholder` set ships and is hosted from backend `wwwroot/card-sets/`; `piacentine` manifest stub present; per-card fallback to placeholder for missing assets.
- [ ] Swagger / OpenAPI accurate; SignalR contract documented in `docs/api.md`.
- [ ] Dockerized; `docker compose up` brings up a working stack against Postgres.
- [ ] CI green on every PR (build + test + lint + dependency audit).
- [ ] Playwright golden-path E2E green (multi-context 2p game) AND reconnect E2E green.
- [ ] Logs are structured (JSON), include correlation ids, and contain no secrets / hand contents.
- [ ] Health (`/healthz`) and readiness (`/readyz`) probes wired; OpenTelemetry instrumentation in place.
- [ ] HSTS, CSP, security headers verified by an integration test that asserts response headers.
- [ ] Documentation in `docs/` complete and accurate; ADRs `0001`–`0004` filled in.
- [ ] Threat-model review completed (`docs/security.md`).

See [TODO.md](TODO.md) for the phased work plan that gets us there.
