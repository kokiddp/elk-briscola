# AGENTS.md — instructions for any LLM (or human) implementing this project

This file is the operating manual for whichever agent (Claude, GPT, a human, anything else) is working on **elk-briscola**. If you are an LLM coding assistant: **read this file in full at the start of every session, before touching any other file.**

If something in this document conflicts with the user's direct instruction in the conversation, the user's instruction wins for that turn — but flag the conflict and offer to update this file.

---

## Table of contents

1. [Document hierarchy & precedence](#document-hierarchy--precedence)
2. [How to start a coding session](#how-to-start-a-coding-session)
3. [How to update TODO.md](#how-to-update-todomd)
4. [How to update README.md](#how-to-update-readmemd)
5. [Coherence between the three documents](#coherence-between-the-three-documents)
6. [Coding conventions — backend (.NET 10)](#coding-conventions--backend-net-10)
7. [Coding conventions — frontend (Angular 21)](#coding-conventions--frontend-angular-21)
8. [Testing conventions](#testing-conventions)
9. [Git & PR conventions](#git--pr-conventions)
10. [What to do, what to avoid](#what-to-do-what-to-avoid)
11. [Definition of "step done"](#definition-of-step-done)
12. [When to ask the user](#when-to-ask-the-user)
13. [Forbidden behaviors](#forbidden-behaviors)
14. [Self-checks before claiming a phase complete](#self-checks-before-claiming-a-phase-complete)

---

## Document hierarchy & precedence

There are three load-bearing documents in this repo:

1. **[README.md](README.md)** — *what* we are building and *why*. Architecture, contracts, rules, definition of done. **Source of truth for any architectural or contract question.**
2. **[TODO.md](TODO.md)** — *how*, step by step. The phased plan, with paths, signatures, tests, acceptance criteria. **Source of truth for the order of work and the level of detail to implement.**
3. **[AGENTS.md](AGENTS.md)** — *the meta-rules for the agent doing the work*. This file. **Source of truth for conventions, workflow, and protocol.**

When two documents disagree:

- README vs. TODO on architecture/contract → README wins; update TODO to match in the same PR.
- README vs. TODO on "what to do next" → TODO wins; update README only if the *contract* changed.
- AGENTS vs. anything on workflow → AGENTS wins; if a convention turns out wrong, propose updating AGENTS first, then apply the new convention.

**Never silently follow only one document.** When you start a step, you read the README section it touches AND the TODO step in full.

---

## How to start a coding session

Every session, before writing any code:

1. **Read [AGENTS.md](AGENTS.md) (this file) end to end.**
2. Skim [README.md](README.md) — at minimum the section corresponding to the phase you'll touch.
3. Read the [TODO.md](TODO.md) phase you're working on **in full**. Identify the next `[ ]` step (or the next `[~]` if a step was left in-progress). Steps within a phase are usually sequential.
4. Read the doc strings of any files you'll modify before editing.
5. State to the user, in one short sentence, which step you're starting. Then begin.
6. After each step's acceptance criteria are met, **mark it done in TODO.md** before moving on (see protocol below).

If a step's acceptance criteria are unclear or ambiguous, **ask the user** rather than guessing — see [When to ask the user](#when-to-ask-the-user).

---

## How to update TODO.md

`TODO.md` is mutable. You will update it routinely. Rules:

### Status markers

| Marker | Meaning | When to set |
|---|---|---|
| `[ ]` | not started | initial state |
| `[~]` | in progress | when you start the step |
| `[x]` | done | when acceptance criteria are met **and** verified (tests run, build green) |
| `[!]` | blocked | when work cannot proceed; **must** be paired with a one-line reason |

### Update protocol

1. **One status change per Edit call.** Don't bundle multiple status flips in one diff — it makes the history harder to read.
2. When marking `[x]`, run the relevant tests/build first. If they don't pass, do not mark done.
3. When marking `[!]`, append a comment after the line: `[!] (S) Step text — blocked: <one-line reason>`. Then either ask the user or work around it on a separate branch.
4. **Never delete a step.** If a step turns out to be wrong:
   - Mark it `[x]` if it's superseded by a newer plan, AND
   - Add a follow-up step or note explaining the change. Past plan history is informative.
5. If the user asks for a new sub-task, add it under the appropriate phase as a new `[ ]` line. Keep the phase narrative coherent.

### When to add a new step

Add a step when:
- You discover something that should have been in the plan and isn't.
- The user requests a new feature inside an existing phase.

Do not add steps for:
- Bug fixes during implementation — those are part of the step you're already on.
- Refactors discovered while implementing — fold them into the current step or, if substantial, propose a new phase explicitly to the user.

### When to split a step

Split when a step would take more than its size estimate suggests (a `S` step taking a full day → split into 2–3 sub-steps). Add the children indented under the original; mark the original `[~]` until all children are done.

### When NOT to update TODO.md

- Don't paraphrase or "tidy up" prose in steps you aren't actively working on.
- Don't change test counts, file paths, or signatures unless the underlying code requires it.
- Don't reorder phases.

---

## How to update README.md

README is the contract. Update it when:

- A REST endpoint, SignalR event, DTO field, or DB column changes shape.
- An architectural decision changes (record an ADR under `docs/adr/` first; then update README).
- A configuration key is added/removed/renamed.
- The Definition of Done gains or loses an item.

Do **not** update README for:
- Implementation details that don't change the external contract (e.g., refactoring `GameOrchestrator`'s internal queue).
- Test additions/removals.
- Phase progress (that's TODO's job).

When updating README, **also update the corresponding TODO step** in the same change so the docs stay in sync.

---

## Coherence between the three documents

After **any change** that touches more than one of {README.md, TODO.md, AGENTS.md, code}, run this checklist:

- [ ] Is the contract described in README.md the same as the one described in TODO.md (paths, signatures, configuration keys)?
- [ ] Are the names of types, interfaces, files, environment variables, and routes byte-identical across the docs?
- [ ] Does the Definition of Done in README.md still reflect what TODO.md will actually deliver?
- [ ] If a convention from this AGENTS.md was violated by something you wrote, either fix the code or update AGENTS.md.
- [ ] Run a grep for the changed identifier across all three files; no stale references should remain.

If the checklist surfaces a mismatch, **fix it before moving on.**

---

## Coding conventions — backend (.NET 10)

### Project layout

Strict four-project structure: `Briscola.Domain`, `Briscola.Application`, `Briscola.Infrastructure`, `Briscola.Api`.

- `Briscola.Domain` references **nothing** outside the BCL.
- `Briscola.Application` references `Briscola.Domain` and abstractions packages (`Microsoft.Extensions.*`). **No EF Core, no ASP.NET, no SignalR, no JSON.**
- `Briscola.Infrastructure` references `Briscola.Application` and concrete framework packages (EF Core providers, Identity, Serilog, BCrypt).
- `Briscola.Api` references `Briscola.Infrastructure`. This is the only project that knows about HTTP and SignalR.

If you find yourself wanting a project to reference "down" the dependency arrow, stop and re-design.

### Language

- C# 14 (the language version that ships with .NET 10), `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in Release.
- Prefer `record` and `record struct` for value types.
- Prefer `ImmutableArray<T>` over `T[]` for state snapshots; `IReadOnlyList<T>` for return types.
- File-scoped namespaces.
- Primary constructors only when they don't compromise clarity.
- Avoid `var` when the right-hand side is not obviously a type (e.g., factory methods returning interfaces).
- No `async void`. Hub callbacks return `Task`.
- Always pass `CancellationToken` through async chains; default value only at the outermost public boundary.

### Naming

- Interfaces: `IFoo`. Implementations: `Foo` (or `EfFoo`, `InMemoryFoo` when context matters).
- Async methods: `…Async` suffix.
- DTOs: `XxxRequest`, `XxxResponse`, `XxxDto`. **Never** reuse domain types as DTOs.
- Configuration option classes: `XxxOptions`, with `public const string SectionName = "Xxx";`.

### Error handling

- Domain throws `InvalidMoveException(InvalidMoveCode)` only.
- Application throws specific typed exceptions (`ConcurrencyConflictException`, `LobbyClosedException`, `InvalidPasswordException`).
- API maps every exception type to a stable `ProblemDetails` shape with a stable error code. **No bare 500s for expected error paths.**
- Don't catch `Exception` and swallow. If you catch, log with context and rethrow or convert.

### Logging

- Inject `ILogger<T>`; never use `Console.WriteLine` outside of `Program.cs` setup.
- Structured logging only: `_logger.LogInformation("Game {GameId} started by {UserId}", gameId, userId)` — never string interpolation.
- Forbidden tokens in any log call (any level): `password`, `token`, `accessToken`, `refreshToken`, full card lists, full chat text. A unit test enforces this in Phase 11.

### Configuration

- All configurable values live in `XxxOptions` classes bound from `IConfiguration`. Never call `configuration["Key"]` outside `Program.cs`.
- Secrets (JWT signing key, DB password) come from environment variables only. Never check defaults into source.

### Persistence

- All EF Core access goes through repositories implementing `Briscola.Application.Ports.*`. **No DbContext usage outside `Briscola.Infrastructure`.**
- Use `AsNoTracking()` for queries that don't intend to update.
- Migrations live in their own per-provider assemblies. Never edit a generated migration after it's been merged to `main`; create a new one.

---

## Coding conventions — frontend (Angular 21)

### Project structure

```
frontend/src/app/
├── core/        # cross-cutting: auth, http, signalr, guards, interceptors
├── shared/      # reusable presentational pieces, pipes
├── features/
│   ├── auth/
│   ├── lobby/
│   ├── game/
│   ├── profile/
│   └── spectate/
└── card-sets/   # CardSet interface + registry + active-set service
```

### Components

- **Standalone components only.** No `NgModule`s.
- One component per file; SCSS sibling.
- Prefer **signals** for component state (`signal()`, `computed()`, `effect()`) over `BehaviorSubject` / RxJS for *local* state. RxJS only when interfacing with HTTP / SignalR streams.
- `OnPush` change detection isn't needed when state is signal-driven; default CD with signals is efficient.

### Inputs/outputs

- New input/output signal API: `input()`, `model()`, `output()`. Avoid the legacy `@Input()` decorator unless you must.

### State

- No NgRx, no Akita. State lives in services that expose signals.
- Persist only what must outlive a reload (auth tokens, active card-set id) — in `localStorage`. Everything else is in-memory.

### HTTP & SignalR

- All HTTP via `HttpClient`. No `fetch`.
- All hub interaction via `core/signalr-client.ts`'s factory; never instantiate `HubConnectionBuilder` in a feature.
- Always handle `withAutomaticReconnect` events; on reconnect, re-call `JoinGame` to fetch a fresh snapshot.

### Naming

- Files: kebab-case; classes: PascalCase; signals: camelCase.
- Components: `SomethingComponent` selector `bri-something`.
- Services: `SomethingService`.
- Tests: sibling `.spec.ts`.

### Templates

- Two-way binding only via the new `model()` API, never legacy `[(ngModel)]` for new code.
- No logic in templates beyond pure expressions and `*ngIf`/`@if`/`@for`. No subscriptions in templates — pre-derive in `computed`.

### Styling

- SCSS, scoped to the component. Avoid global rules outside `styles.scss`.
- Use CSS custom properties for theming.
- `prefers-reduced-motion` disables animations.

### i18n

- Strings via `I18nService.t('key')`. Adding a key requires adding it to `en.json` and `it.json` (English fallback acceptable for `it.json` in v1).

---

## Testing conventions

### What to test

- **Domain layer:** every rule, every invariant, with unit tests that target ≥ 95% line coverage. Property tests for game-completion invariants.
- **Application layer:** every use-case path through the orchestrator and lobby service, against in-memory fakes. ≥ 85% coverage.
- **API layer (integration):** every endpoint's happy path, every named error case, security headers, rate limits, hub redaction. Coverage target is incidental; **scenario coverage** matters more than line coverage here.
- **Frontend:** component renders, form validation, signal-driven UI behavior. Smoke-level coverage; the heavy lifting is the E2E suite.
- **E2E (Playwright):** the multiplayer golden path. Two contexts. One reconnect test.

### How to write tests

- **Backend:** xUnit + FluentAssertions. `[Theory]` + `[InlineData]` for table-driven cases. **Never** make tests dependent on test order.
- **Frontend:** Jest + `@testing-library/angular`. Use `screen.getByRole(...)` over `getByTestId` when possible.
- **Integration:** Testcontainers Postgres in a class fixture; never mock the DB at the integration level.

### What NOT to test

- Don't test Angular framework behavior (the framework's own tests cover it).
- Don't test EF Core serialization (it's tested upstream).
- Don't write tests that just mirror implementation (e.g., asserting that `_logger` was called — unless that's the whole point).
- Don't add a "regression test for bug X" without naming the bug in a comment.

### CI gating

CI must run on every PR:

- `dotnet build --configuration Release` — 0 warnings.
- `dotnet test --configuration Release` — all green.
- `npm run lint` — clean.
- `npm run build` — succeeds.
- `npm run test:ci` — all green.
- E2E suite (Phase 13+).
- `dotnet list package --vulnerable --include-transitive` — no high/critical.
- `npm audit --production --audit-level=high` — clean.

---

## Git & PR conventions

### Branches

- `main` is always green and deployable.
- Feature branches: `feat/<short-name>`. Bugfix: `fix/<short-name>`. Docs: `docs/<short-name>`.
- One PR per logical change. Don't bundle phase 5 work and phase 7 work in one PR.

### Commits

- Imperative mood, no period: `add GameOrchestrator process loop`.
- Conventional commits prefix is OK but not required: `feat:`, `fix:`, `docs:`, `chore:`, `test:`, `refactor:`.
- Co-author trailer for AI-assisted commits: `Co-Authored-By: Claude <noreply@anthropic.com>` (or whichever model).
- **Never `--amend` or `--force`** on a published branch unless the user explicitly asks.
- **Never** skip hooks (`--no-verify`).

### Pull requests

- PR title: concise, imperative, ≤ 70 chars.
- Body sections: Summary (1–3 bullets), Test plan (checklist), Notes (optional).
- Link the TODO.md step(s) the PR completes.
- Don't open a PR with `[~]` items still in progress in the same area; either finish them or move them out of scope.

---

## What to do, what to avoid

### Do

- **Match scope to request.** A bugfix is not a refactor.
- **Prefer editing existing files** to adding new ones.
- **Write the test first** for non-trivial logic, especially in the domain layer.
- **Read before writing.** When editing, read the surrounding 50 lines, not just the line you're changing.
- **Use the right tool**: `Read` over `cat`; `Edit` over `sed`. (For LLM agents wired to those tools.)
- **Update TODO.md status as you go**, never at the end of the session in one big batch.
- **Communicate trade-offs** when you make a choice the user might want different.
- **Keep PRs small.** A 2000-line PR is a code review you'll lose.

### Avoid

- Adding features that weren't requested.
- Defensive code for impossible states (`if (this == null) ...`).
- Comments that paraphrase the next line of code.
- Premature abstractions ("we might want to swap this later").
- Touching files outside the step's scope without a reason.
- Renaming things just because you'd have named them differently.
- Adding new dependencies without weighing the alternative.

---

## Definition of "step done"

A TODO step is `[x]` only when **all** of these hold:

1. Code compiles in Release with 0 warnings.
2. Tests written for the step pass locally.
3. Linter (`dotnet format`, `eslint`) clean on touched files.
4. Acceptance criteria in the step's "Acceptance" line are demonstrably met.
5. README and TODO are still coherent (run the [Coherence checklist](#coherence-between-the-three-documents)).
6. No new TODO comments in code (`// TODO`, `// FIXME`) without an associated TODO.md entry.

If any of these fail, the step is `[~]` (in progress) or `[!]` (blocked) — not done.

---

## When to ask the user

Ask **before** acting when:

- The step is ambiguous and the wrong choice would require rework (e.g., "should reconnect grace be 60 or 120 seconds?" — already answered, but the *kind* of question).
- A request would expand scope (new feature mid-step).
- A destructive action is needed (`git reset`, dropping a column, deleting a file).
- A new dependency is being introduced.
- The user might prefer a manual confirmation (e.g., "should I push to main?").

Don't ask when:

- The answer is in README/TODO/AGENTS.
- The answer is obvious from convention (e.g., file naming).
- It's a routine implementation detail (private method names, internal data shapes).

When asking, **offer 2–4 concrete options** with the recommendation labeled. Don't ask open-ended questions if a concrete short list will do.

---

## Forbidden behaviors

These are non-negotiable. Violating them is a bug:

1. **Never** push to `main` directly without an explicit user instruction. PRs always.
2. **Never** force-push, amend published commits, or `git reset --hard` published branches without explicit user consent.
3. **Never** commit secrets. If a secret was committed accidentally, stop, tell the user, and rotate.
4. **Never** disable a failing test to make CI green. Fix the test or the code.
5. **Never** introduce horizontal scaling (Redis, distributed locks, etc.) — explicitly out of scope for v1.
6. **Never** change the public contract (REST/SignalR shape, DB schema) without updating README **and** TODO in the same PR.
7. **Never** silently change the rules engine. Rules updates require:
   - An ADR under `docs/adr/`.
   - Updates to `docs/game-rules.md` and the README rules section.
   - New tests covering the change.
8. **Never** include uncleared third-party card art. The Piacentine slot stays empty until licensing is verified by a human.
9. **Never** auto-merge dependency PRs without reading the changelog and running the test suite.
10. **Never** ignore a flaky test — investigate and fix or quarantine with a TODO.md entry.

---

## Self-checks before claiming a phase complete

Before announcing "Phase N is done":

- [ ] Every step in the phase is `[x]`.
- [ ] The phase's "Exit" line at the end of the section is satisfied.
- [ ] CI is green on the latest pushed branch (skip this check until a remote is configured; instead run the same commands locally and confirm green).
- [ ] Coverage thresholds (Phase 1 ≥ 95% domain; Phase 2 ≥ 85% application) are met.
- [ ] No `[!]` blockers carried over.
- [ ] Coherence checklist passes against README.md.
- [ ] You've used the product end-to-end at the level the phase enables (e.g., for Phase 7, two browsers join a game manually).

If any item is unchecked, the phase is not done.

---

## Final notes

This file will evolve. If you discover a convention that's missing or wrong, propose an update to AGENTS.md as a separate change before applying it. The goal is a stable, predictable contract between the user and any agent doing the work — including future-you with no memory of this session.
