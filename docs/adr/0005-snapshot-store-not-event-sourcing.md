# 5. Snapshot store, not event sourcing

Date: 2026-05-19

## Status

Accepted

## Context

The orchestrator processes commands and mutates `GameState`. Some persistence layer must let a game survive a process restart, support reconnect (server replies with the current authoritative state), and capture enough history to replay finished games.

Two architectural patterns were considered:

1. **Event sourcing.** Persist every domain event (`CardPlayed`, `TrickResolved`, `CardsDrawn`, …) as the source of truth. Reconstruct state by replaying events on startup. Adopted by some card-game backends because it preserves a complete audit trail.
2. **Snapshot store.** Persist the latest authoritative state as a single JSON blob (`GameRecord.StateSnapshotJson`), with optimistic concurrency on `Version`. Capture a per-move log (`GameMoves`) separately for analytics / replay, but not as the source of truth.

We want:

- Cheap reconnect (`SELECT … WHERE id = $1` → reattach).
- Cheap restart recovery (snapshot is current; replay isn't needed to be playable).
- Bounded write cost per move (one snapshot row, not N event rows).
- Replay capability for finished games (data captured, UI is v2).

## Decision

Use a snapshot store. The full game state is materialized into `Games.StateSnapshotJson` after every command; per-move detail is captured separately in `GameMoves` for analytics + future replay.

- **Snapshot column.** `GameRecord.StateSnapshotJson` carries a `System.Text.Json` serialization of `Briscola.Domain.State.GameState`. Postgres stores it as `jsonb`; SQLite as `TEXT`. Snapshots are written from `GameRoom` on every move.
- **Optimistic concurrency.** `GameRecord.Version` is incremented per update; the repository's `UpdateAsync` does a conditional `UPDATE … WHERE Id = $1 AND Version = $2` and returns false on miss. Conflict resolution is per-caller (the lobby retries up to 3 times; the orchestrator surfaces the conflict and pulls a fresh record).
- **Per-move log.** `GameMoves` captures one row per `PlayCard` or `ViewOwnPile`. Includes seat, card (where applicable), and a server timestamp. **Not the source of truth** — the engine never reads it. Used by the (future) replay viewer and by analytics queries.
- **Replay derivation.** Given `(ShuffleSeed, ImmutableArray<GameMove>)` from a finished game, the engine can deterministically re-derive every intermediate `GameState`. The shuffle source is `SeededRandomSource` (Fisher-Yates with `xoshiro256**`) so the same seed always produces the same deck.

## Consequences

**Positive.**

- Reconnect path is a single read: `LoadAsync(gameId)` → deserialize → snapshot for caller.
- Restart recovery is the same path. The orchestrator does not need to scan an event log.
- One write per move keeps the hot loop cheap; we don't pay for an append-only event table on every state transition.
- Per-move log is still captured, so the dataset for replay + analytics is intact; we just don't *have to* derive playable state from it.

**Negative / costs.**

- The snapshot column is large (~2-4KB per game) and rewritten every move. We accept this — game cardinality is low (hundreds of concurrent rooms target), and Postgres `jsonb` updates are well-tuned for this access pattern.
- Time-travel debugging is harder than with event sourcing. You can't ask "what was the state right after trick 4?" without re-running the move log. Mitigation: the per-move log is the data you'd need; the missing piece is a tool to replay it. Out of scope for v1.
- A schema change to `GameState` requires a migration of stored snapshots OR a backward-compatible deserializer. Mitigation: `JsonStringEnumConverter` + nullable optional fields on the record, plus a snapshot-format-version check that's deferred until we have the first such change.

**Trade-offs explicitly accepted.**

- We don't get a free audit log of every operation. The per-move log covers gameplay; structured Serilog covers operations. If we ever need true event-sourced audit we add it; today we don't.
- Snapshots are not idempotent on retry: a duplicate `UPDATE` with the same `Version` would just no-op (the optimistic check fails). That's the intended behavior — we use the version mismatch as the retry signal.
