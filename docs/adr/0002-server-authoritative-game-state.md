# 2. Server-authoritative game state

Date: 2026-05-19

## Status

Accepted

## Context

Briscola is a card game with private information (each player's hand) and a strict turn structure. We need to decide where the canonical game state lives and which side validates moves.

Two broad options exist:

1. **Client-trusting / peer-to-peer.** Each client simulates the game; clients exchange moves and trust each other to enforce rules. Used by some real-time games where latency dominates correctness.
2. **Server-authoritative.** The server is the only source of truth: it holds full state, validates every move, and pushes per-recipient redacted views to clients. Clients render but do not decide.

Option 1 is cheaper to operate but trivial to cheat against — a modified client can play any card, see opponents' hands, or replay states. Card games with persistent ranking (Elo) cannot tolerate that posture.

We also need to support:

- Reconnect after a tab close (server must reply with the current authoritative state, not a stale local replica).
- Spectators (a separate redacted view: counts only, no hands).
- Replay (a finished game must be reproducible from its persisted record).

## Decision

The server is the sole authority for game state.

- **State ownership.** `Briscola.Application.Orchestration.GameRoom` owns the per-game `GameState` record. Mutations go through `BriscolaEngine.PlayCard`, which validates every move.
- **Single writer.** Each room has a `Channel<GameCommand>`; the room's background task consumes commands sequentially. No two threads ever mutate `_state` at once.
- **Per-recipient redaction.** `GameRoom.SnapshotForUser(userId)` returns a `RedactedStateForUser` that includes the calling player's own hand and pile but only counts for everyone else. Spectators use the `Guid.Empty` variant.
- **Validation in the engine.** Illegal moves are rejected with stable `InvalidMoveCode` strings (`NotYourTurn`, `CardNotInHand`, `GameFinished`, `WrongPhase`, `PileViewNotAllowed`). The hub surfaces them via the `invalidMove` push.
- **Replay capture.** Every finished game records its `ShuffleSeed` plus a per-move log in `GameMoves`; combined with the engine's deterministic shuffle (`Briscola.Domain.Engine.Deck`), any game can be re-derived from those two columns. The replay viewer is v2 — the data is captured today.

## Consequences

**Positive.**

- Cheating is structurally impossible: the client never holds information it shouldn't, and the server rejects moves the engine doesn't permit.
- Reconnect is trivial — `JoinGame(gameId)` is idempotent and returns the current authoritative snapshot.
- Spectators get a strictly weaker view of the state without any client-side logic.
- Bugs in the rules engine surface as deterministic unit tests; we can replay a finished game from its seed + move log.

**Negative / costs.**

- Every move round-trips the server. We accept the latency hit (typical move latency is well under 200ms on the same datacenter).
- The orchestrator is in-process for v1; horizontal scale of a single game is impossible without a shared message bus. We treat this as a v2 problem — for v1 the per-process room count is bounded by memory and CPU well below the deploy target.
- We carry the cost of building + maintaining a redaction layer. The cost is contained to `GameRoom.SnapshotForUser`.

**Trade-offs explicitly accepted.**

- We do not implement client-side prediction. A laggy connection looks laggy.
- The engine is the only validator: clients cannot pre-filter illegal moves except as UX hints. Illegal-move toasts come back over the wire as `invalidMove` codes.
