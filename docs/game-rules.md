# Game rules

Canonical specification of the Briscola variant the engine enforces, annotated with pointers into the implementation. This is the file to read alongside `Briscola.Domain` — every rule below is realized in code that this document references by file/symbol.

The high-level rules also live in [README.md § Game rules implemented](../README.md#game-rules-implemented); this document is the engine-reader's view.

> Rule deviations require an ADR. Don't silently broaden last-hand permissions or change the strength table — those decisions are load-bearing for replayability + Elo fairness.

---

## Deck

- 40 cards: 4 suits × 10 ranks. Defined in `Briscola.Domain.Primitives.Card`.
- Suits — `Suit` enum, `Briscola.Domain.Primitives.Suit`:
  - `Bastoni`, `Coppe`, `Denari`, `Spade`.
  - French-suit equivalents are a **display** concern handled by the card-set, not the engine. See [docs/card-sets.md](card-sets.md).
- Ranks — `Rank` enum, `Briscola.Domain.Primitives.Rank`:
  - `Asso, Tre, Re, Cavallo, Fante, Sette, Sei, Cinque, Quattro, Due` (Italian).
  - The enum carries no implicit ordering or point value — both are looked up via `CardTables`.

## Card values (points)

`Briscola.Domain.CardTables.Points(rank)`:

| Card | Points |
|---|---|
| Asso | 11 |
| Tre | 10 |
| Re | 4 |
| Cavallo | 3 |
| Fante | 2 |
| Sette, Sei, Cinque, Quattro, Due | 0 |

Total points in a deck: **120**. Win threshold: **≥ 61**. Draw at exactly **60–60**. The `≥ 61` boundary is enforced by `BriscolaEngine.ComputeOutcome`.

## Trick-taking strength

`Briscola.Domain.CardTables.Strength(rank)`. Within the same suit:

`Asso > Tre > Re > Cavallo > Fante > Sette > Sei > Cinque > Quattro > Due`.

This ordering is **independent** of point value and of the K/Q/J/10/9/8 markings sometimes printed on French-suit Briscola decks. The strength table is a fixed lookup; rank enum members carry no implicit numeric strength.

---

## Setup

`BriscolaEngine.StartGame(GameState, IRandomSource)`:

1. Shuffle the 40-card deck — Fisher-Yates against `IRandomSource`. The seed comes from `GameRecord.ShuffleSeed`, captured at room start so a finished game is replayable from `(ShuffleSeed, GameMoves)`.
2. Pick a dealer at random (single-game sessions only in v1 — no multi-hand rotation).
3. Dealer gives **3 cards** to each player.
4. Dealer turns the next card face-up — the **briscola** card; its suit is the trump suit for the whole game (`GameState.BriscolaSuit`).
5. The briscola card is placed perpendicular under the stock (tallone). It is the **last** card drawn from the stock.

`GameState.Phase` transitions Setup → Dealing → Playing on the first lead.

## Turn order

- Play proceeds in seat order. Seats are 0-indexed; the seat *after the dealer in seat order* leads the first trick.
- After a trick, the trick winner leads the next one (`GameState.LeaderSeat`).
- "Counter-clockwise" geometry from canonical regional rules is purely UI/seating presentation; the server only knows seat indices. The frontend rotates the seats so the local player sees themselves at the bottom (see `GameTablePageComponent.opponentSlots`).

## Playing a trick

`BriscolaEngine.PlayCard(GameState, seat, card)`:

1. The leader plays any card from their hand. Its suit is the **lead suit**.
2. Each subsequent player plays any card. **There is no obligation to follow suit** — Briscola does not require following the lead suit.
3. The trick is won by:
   - the highest **briscola** card played, if any briscola was played; otherwise
   - the highest card of the **lead suit**.
4. Cards of any other (non-briscola, non-lead) suit cannot win. Computed by `BriscolaEngine.ResolveTrickWinner` using `CardTables.Strength`.

## Drawing

After a trick (still inside `BriscolaEngine.PlayCard`):

1. The winner gathers the played cards face-down into their `pozzo` (`GameState.Pozzo[seat]`).
2. The winner draws first from the stock, then the remaining players in seat order — each takes exactly one card.
3. The briscola card sits at the bottom of the stock; the player whose draw lands on it gets the briscola. By construction it is therefore the **last** card drawn.
   - In 2p: after the trick at which the stock has 1 card + the briscola, the trick winner takes the stock card and the loser takes the briscola.
   - In 4p: similarly, the briscola goes to whichever seat draws last in the trick that empties the stock.
4. Once the stock (including the briscola card) is exhausted, every player has exactly 3 cards. Play continues without drawing until all hands are empty (the **last-hand phase** — see below).

## Last-hand phase

The phase begins the moment `GameState.Stock.IsEmpty && BriscolaCardDrawn` — i.e., when each player holds exactly 3 cards. `GameState.Phase` transitions Playing → LastHand. We implement the **standard subset** explicitly chosen for v1:

- **2p and 4p:** every player may inspect their own `pozzo` via `ViewOwnPile`. The engine rejects this in any other phase with `InvalidMoveCode.PileViewNotAllowed`.
- **4p only:** in-game chat continues to be enabled (it always is); the UI highlights it as the **tactical phase**, signalling teammates that they may now coordinate via chat. The chat contents themselves are not engine-relevant.
- **Not implemented in v1 (deliberate, deferred):**
  - Viewing teammates' hands.
  - Card swaps with teammates.
  - Hard "no talking" enforcement outside the last-hand phase (we never enforce silence — chat is always allowed; this is a documentation/etiquette concern, not engine behavior).

These permissions are gated server-side by `GameState.Phase == LastHand`. Clients should not pre-emptively allow `ViewOwnPile`; the engine is the only authority.

## Scoring and winning

`BriscolaEngine.ComputeOutcome(GameState)`:

- After all 40 cards are played, sum the point values of cards in each player's (or team's) `pozzo`.
- Highest score wins; **60–60 is a draw**. The engine reports the result as a discriminated `GameOutcome { Winner(seatOrTeam) | Draw }` — the schema does not encode "winner" as nullable.
- Persisted to match history (`MatchHistoryService.RecordFinishedAsync`); Elo updated by `RankingService.UpdateAsync`.

## 4-player teams

- Fixed pairs by seat: seats `0` and `2` form **Team A**, seats `1` and `3` form **Team B**.
- 4-player games require exactly 4 humans to start; a partially-filled lobby cannot start. The lobby creator may cancel via the lobby UI (REST `POST /games/{id}/leave`).
- Team scores are summed from per-seat scores. Match history records per-seat scores AND the team-level result.

## Game lifecycle

`GameStatus` transitions (`Briscola.Domain.Primitives.GameStatus`):

| From | To | Trigger |
|---|---|---|
| (none) | `Open` | `POST /games` |
| `Open` | `Open` | `POST /games/{id}/join` (still seats free) |
| `Open` | `Running` | last seat fills (atomic with `BriscolaEngine.StartGame`) |
| `Open` | `Abandoned` | `OpenLobbyJanitor` after TTL; OR last seated player calls `LeaveAsync` |
| `Running` | `Finished` | engine reaches `Phase == Finished` and `Outcome != null` |
| `Running` | `Abandoned` | reconnect grace expires (`ForfeitDisconnect`) or idle timer expires (`ForfeitIdle`) |

Persisted in `GameRecord` rows; the SignalR `gameUpdated` / `gameStarted` / `gameEnded` pushes fan these transitions out to the lobby SignalR group.

## Idle handling

- Idle warn: after `Game:IdleWarnSeconds` (default 90s) of the active seat thinking, `idleWarning(seatIndex, forfeitDeadlineUtc)` is broadcast.
- Idle forfeit: after `Game:IdleForfeitSeconds` (default 180s), the engine forfeits the active seat with reason `ForfeitIdle`.
- `RedactedStateForUser.ActiveSeatForfeitDeadline` carries the per-turn deadline in **every** snapshot, so the client renders a countdown that resets the moment a move lands — independent of the legacy `idleWarning` push (kept for back-compat).

## Disconnect grace

- On `OnDisconnectedAsync` or `LeaveGame` against a `Running` game, the server starts a `Game:ReconnectGraceSeconds` (default 60s) grace timer.
- `playerDisconnected(seatIndex, graceDeadlineUtc)` is broadcast.
- If the player reconnects via `JoinGame(gameId)` before the deadline, `playerReconnected(seatIndex)` is broadcast and the grace timer is cancelled.
- Otherwise the engine forfeits the seat with reason `ForfeitDisconnect`.

---

## Implementation pointers

| Concern | File |
|---|---|
| Strength + points tables | [`backend/src/Briscola.Domain/CardTables.cs`](../backend/src/Briscola.Domain/CardTables.cs) |
| Shuffle | [`backend/src/Briscola.Domain/Engine/Deck.cs`](../backend/src/Briscola.Domain/Engine/Deck.cs) |
| Rules engine | [`backend/src/Briscola.Domain/Engine/BriscolaEngine.cs`](../backend/src/Briscola.Domain/Engine/BriscolaEngine.cs) |
| Per-room state machine | [`backend/src/Briscola.Application/Orchestration/GameRoom.cs`](../backend/src/Briscola.Application/Orchestration/GameRoom.cs) |
| Per-recipient snapshot | `GameRoom.SnapshotForUser` |
| Idle + reconnect timers | `GameRoom.ScheduleIdleChecks` / `GameRoom.OnDisconnectedAsync` |
| Match history persistence | [`backend/src/Briscola.Application/Persistence/MatchHistoryService.cs`](../backend/src/Briscola.Application/Persistence/MatchHistoryService.cs) |
| Elo computation | [`backend/src/Briscola.Application/Ranking/RankingService.cs`](../backend/src/Briscola.Application/Ranking/RankingService.cs) |
