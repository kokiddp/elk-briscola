# 1. Record architecture decisions

Date: 2026-05-09

## Status

Accepted

## Context

We need a lightweight, durable record of architectural decisions taken during the development of this project — both to help future contributors (human or LLM) understand *why* the system looks the way it does, and to give us a place to consciously reverse a decision when circumstances change.

GitHub PR descriptions and commit messages are not enough: they are organised by *change*, not by *decision*, and they decay with the codebase. Wikis are too easy to ignore.

## Decision

We will use **Architecture Decision Records (ADRs)**, in the lightweight format proposed by Michael Nygard, stored in this repository under `docs/adr/`.

- One file per decision.
- Filename: `NNNN-short-kebab-title.md` where `NNNN` is a zero-padded sequence number.
- Each ADR has these sections: **Title**, **Date**, **Status** (Proposed / Accepted / Deprecated / Superseded), **Context**, **Decision**, **Consequences**.
- ADRs are immutable once Accepted: to change a decision, write a new ADR that **Supersedes** the old one and update the old ADR's `Status` line accordingly.

The decisions covered by ADRs are those that:
- Are hard to reverse, OR
- Affect more than one component, OR
- Materially shape the codebase's structure or operational profile.

Routine implementation choices stay in code review.

## Consequences

- A consistent paper trail of *why* we made architectural choices.
- A shared format for any contributor — including AI agents per [AGENTS.md](../../AGENTS.md) — to follow.
- A small ongoing cost per architectural change: writing an ADR. We accept this cost; it's far less than the cost of someone re-deriving rejected designs years later.
- ADRs to be written in subsequent phases (placeholders in [TODO.md Phase 14](../../TODO.md#phase-14--documentation-polish--release)):
  - `0002-server-authoritative-game-state.md`
  - `0003-signalr-over-raw-websockets.md`
  - `0004-pluggable-card-sets.md`
  - `0005-snapshot-store-not-event-sourcing.md`

## References

- Michael Nygard, "Documenting Architecture Decisions" (2011) — https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions
