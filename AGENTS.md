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

## Tooling environment (WSL gotchas + version pins)

The repo is developed on WSL2 / Ubuntu 24.04. A new shell does NOT inherit the right toolchain by default; the project keeps an env-prelude at `~/.elk-env.sh` that puts Linux .NET and Linux Node *ahead of* the Windows shims that WSL inherits via `/mnt/c`.

```sh
# ~/.elk-env.sh
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.nvm/versions/node/v22.22.2/bin:$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
```

Source it at the start of any Bash session before running `dotnet`, `npm`, or `npx`:

```sh
. ~/.elk-env.sh
dotnet --version    # 10.0.203
node --version      # v22.22.2
```

**Without this**, `npx -p @angular/cli@21 ng new …` finds the Windows nvm4w `npx`, runs `cmd.exe` with a WSL path it can't resolve, and fails with `EPERM: operation not permitted, mkdir 'C:\Windows\frontend'`. If you see that error, the prelude isn't sourced.

### Pinned versions (don't drift without a reason)

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 10.0.203 | Installed via `dotnet-install.sh` into `~/.dotnet`. No sudo. |
| Node | v22.22.2 (LTS "Jod") | Installed via nvm; default alias = `lts/jod`. |
| `xunit` | 2.9.3 | What the .NET 10 `dotnet new xunit` template ships. |
| `Microsoft.NET.Test.Sdk` | 17.14.1 | Same. |
| `xunit.runner.visualstudio` | 3.1.4 | Same. |
| `coverlet.collector` | 6.0.4 | Same. |
| `FluentAssertions` | 7.2.2 | Last Apache-2.0 release; v8+ is commercial. |
| `@angular/cli` | 21.2.x | And every `@angular/*` peer at the matching major. |
| `@testing-library/angular` | 19.x | Compatible with Angular 21 + Vitest. |

### `dotnet new sln` defaults to `.slnx` on .NET 10

The new XML solution format is the default. We use the legacy `.sln` because `Briscola.sln` is the canonical name in [TODO.md](TODO.md). When recreating the solution, force the format:

```sh
dotnet new sln -n Briscola --format sln
```

If you ever need to migrate to `.slnx`, do it as a separate ADR.

### `ng new` and `--standalone`

Angular 21's `ng new` makes standalone components the default; the legacy `--standalone` flag is a no-op. Don't pass it. The default app class is `App` (in `app.ts`), NOT `AppComponent` — anywhere our docs or tests still say `AppComponent`, treat it as a typo for `App`.

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

- `Briscola.Domain` references **nothing** outside the BCL. Verified by grep — no `PackageReference` or `ProjectReference` rows in `Briscola.Domain.csproj`, and only `using System.*` / `using Briscola.Domain.*` directives in any source file.
- `Briscola.Application` references `Briscola.Domain` and abstractions packages (`Microsoft.Extensions.*`). **No EF Core, no ASP.NET, no SignalR.** `System.Text.Json` IS allowed for non-snapshot serialization (e.g. `MoveRecord.PayloadJson`); snapshot serialization must go through `IGameStateCodec` so the JSON-vs-other choice stays in Infrastructure.
- `Briscola.Infrastructure` references `Briscola.Application` and concrete framework packages (EF Core providers, Identity, Serilog, BCrypt).
- `Briscola.Api` references `Briscola.Infrastructure`. This is the only project that knows about HTTP and SignalR.

If you find yourself wanting a project to reference "down" the dependency arrow, stop and re-design.

### Language

- C# 14 (the language version that ships with .NET 10), `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`.
- **`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` is set ONLY in Release** (via `Directory.Build.props`'s `<PropertyGroup Condition="'$(Configuration)' == 'Release'">`). When verifying acceptance, always run `dotnet build … --configuration Release` and `dotnet test … --configuration Release`. A clean Debug build is not a clean Release build.
- Prefer `record` and `record struct` for value types.
- Prefer `ImmutableArray<T>` over `T[]` for state snapshots; `IReadOnlyList<T>` for return types.
- File-scoped namespaces.
- Primary constructors are fine; use them when they shorten the file without obscuring intent.
- Avoid `var` when the right-hand side is not obviously a type (e.g., factory methods returning interfaces).
- No `async void`. Hub callbacks return `Task`.
- Always pass `CancellationToken` through async chains; default value only at the outermost public boundary.

### Records, equality, and `ImmutableArray<T>` — known footgun

A `record` with `ImmutableArray<T>` (or `ImmutableArray<ImmutableArray<T>>`) fields **does not** get correct value equality out of the box. The auto-generated `Equals` calls `EqualityComparer<ImmutableArray<T>>.Default.Equals`, which falls back to **reference equality** on the underlying array. Two semantically-identical states will compare unequal.

Implications:

- **Don't compare `GameState`s for equality** in production code paths. Serialize-and-compare, or assert specific fields. (`GameState.Tests` already follows this convention.)
- **Don't introduce wrapper classes** around `ImmutableArray<T>` (e.g. a `Hand` class) just to "improve ergonomics." If equality matters, you'll have to implement `Equals`/`GetHashCode`/`IEquatable<T>` everywhere — Phase 1 considered and rejected this for `Hand`.
- If equality on a record with `ImmutableArray<T>` is genuinely required, override `Equals` to call `SequenceEqual` on each array field. Document why.

### `required` init properties

`GameState`'s shape uses `required` init-only properties on a `sealed record`. This is the preferred pattern for Domain/Application snapshot types: the compiler enforces full initialization at construction, the type is immutable after, and `with`-expressions still work for transitions.

### `[ExcludeFromCodeCoverage]` on data records

Plain DTO records (DTOs, command records, event records, `…Record` persistence types) get `[ExcludeFromCodeCoverage]`. `coverlet` can't see through compiler-generated record `Equals`/`GetHashCode`/`PrintMembers`, so leaving them in coverage drops the percentage with no real signal. Apply liberally to anything that's "just data" — but NOT to anything with behavior.

### `InternalsVisibleTo` for testing internals

Domain-internal types (e.g. `Briscola.Domain.Primitives.Deck`) stay `internal` to enforce the assembly's public surface, with one assembly attribute making them visible to the test project:

```csharp
// backend/src/Briscola.Domain/AssemblyInfo.cs
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Briscola.Domain.Tests")]
```

Use this pattern in any production assembly whose public surface should stay tight. Do NOT make types public just to test them.

### Code analyzer suppressions (project-scoped)

Two warnings have legitimate project-wide suppressions:

- **CA1707 (no underscores in identifiers)** — suppressed in `backend/tests/Directory.Build.props` only. xUnit-style test names (`Method_Should_Behave`) are conventional.
- **CA1716 (member name conflicts with VB.NET reserved word, e.g. `Next`)** — suppressed in `Briscola.Domain.csproj` only. We follow `System.Random.Next`'s precedent.

Don't suppress analyzer warnings elsewhere without justification; new suppressions need a comment explaining why.

### Naming

- Interfaces: `IFoo`. Implementations: `Foo` (or `EfFoo`, `InMemoryFoo` when context matters).
- Async methods: `…Async` suffix.
- DTOs: `XxxRequest`, `XxxResponse`, `XxxDto`. **Never** reuse domain types as DTOs.
- Configuration option classes: `XxxOptions`, with `public const string SectionName = "Xxx";`.
- **No shadow interfaces.** If a type already has a usable interface in `Briscola.Domain.Primitives.*`, the application layer consumes it directly — do not redeclare a same-named interface in `Briscola.Application.Ports.*` "to mark the boundary." That created a real bug in Phase 2 (caller code had to write `Ports.IRandomSource …` to disambiguate); we kill it on sight.

### Error handling

- Domain throws `InvalidMoveException(InvalidMoveCode)` only.
- Application throws specific typed exceptions in `Briscola.Application.Errors`: `ConcurrencyConflictException`, `LobbyConflictException`, `InvalidPasswordException`, `GameNotFoundException`, `GameCommandException`. All inherit from `BriscolaApplicationException`.
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
- Optimistic concurrency uses a **plain `long Version` column** managed by the application — not EF's `byte[] RowVersion`. `IGameRepository.UpdateAsync` matches `current.Version == record.Version`, increments on success, returns `false` on mismatch. Callers retry up to 3 times, then surface `ConcurrencyConflictException`.
- The move log uses a strictly-increasing `(GameId, MoveIndex)` unique key. `GameRoom` hydrates `_moveIndex` from `IGameRepository.GetNextMoveIndexAsync` at the top of its process loop so a process restart on a Running game keeps the sequence intact.
- Snapshot serialization goes through `IGameStateCodec` (`JsonGameStateCodec` in Infrastructure). The Application layer holds the port; nobody outside Infrastructure imports `System.Text.Json` for snapshot I/O.
- Lobby-game passwords go through `IGamePasswordHasher` (`BCryptGamePasswordHasher` in Infrastructure). **Distinct** from ASP.NET Identity's user-password hasher — different trust boundary, different rotation rules. Don't share the implementation.

### Game-state lifecycle

- One `GameRoom` per Running game inside `GameOrchestrator`. Single-writer model: a `Channel<GameCommand>` per room, a single `ProcessLoopAsync` consumer. **All state mutation happens on that one task — no locks, no shared mutable state.**
- `GameRoom` is constructed only for `Running` games; pre-game lobby flow lives entirely on `LobbyService`. The room's command set is `PlayCard / ViewOwnPile / Disconnect / Reconnect / IdleTick / ForfeitOnDisconnect`. There is **no** `JoinGameCommand` or `LeaveGameCommand` — proposing one is a sign you're trying to do lobby work in the wrong place.
- After every successful state mutation: persist the snapshot via `IGameStateCodec`, append a `MoveRecord`, publish events via `IGameEventBus`. The order matters and is documented in TODO Phase 2 § Process loop semantics.

### Dependency injection

- Singletons: `GameOrchestrator`, `IBriscolaEngine`, `IGameEventBus`, `ITimerService`, `IRandomSourceFactory`, `IClock`, hosted services.
- Scoped: `LobbyService`, `RankingService`, `MatchHistoryService`, anything per-request that touches `IUserContext`.
- Tests construct services manually — no DI container in the test suite. The `TestGameFactory` helper is the canonical setup pattern.

> **Phase 4 carry-over:** `Briscola.Api/Program.cs` currently disables the DI scope-validator (`ValidateScopes = false`) because Phase 2's `GameOrchestrator` and `OpenLobbyJanitor` inject `IGameRepository` directly even though the EF-backed implementation is Scoped. The right fix is `IServiceScopeFactory` in both classes (open as a Phase 5 follow-up — see [TODO Phase 4 follow-ups](TODO.md#phase-4-follow-up-items-deferred-for-later-phases)). Don't remove the scope-validator opt-out without doing that refactor first, or the host won't boot.

### Configuring authentication options against `WebApplicationFactory<T>`

A REST integration test that overrides `JwtOptions` via `IWebHostBuilder.ConfigureAppConfiguration` will silently 401 every authenticated call if the host reads the JWT signing key **eagerly** during `Program.cs` startup. `WebApplicationFactory` adds its in-memory configuration *after* `WebApplication.CreateBuilder(args)` returns, so a line like:

```csharp
JwtOptions jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>();
... .AddJwtBearer(opts => opts.TokenValidationParameters = ... new SymmetricSecurityKey(jwt.SigningKey) ...);
```

binds the *appsettings* signing key into the validator, while `JwtIssuer` (resolved via `IOptions<JwtOptions>` later) sees the *test* signing key. Tokens signed with one key fail validation against the other.

The fix: register `IConfigureNamedOptions<JwtBearerOptions>` and resolve `IOptions<JwtOptions>` inside the configure callback (lazy resolution). See `Briscola.Api/Program.cs` step 5 for the canonical shape.

### FluentValidation v12 + ASP.NET Core (no auto-validation package)

`FluentValidation.AspNetCore` is archived. The modern recipe is:
1. Reference `FluentValidation` and `FluentValidation.DependencyInjectionExtensions` only.
2. Register validators with `services.AddValidatorsFromAssembly(typeof(Program).Assembly)`.
3. Run them yourself in an `IAsyncActionFilter` (see `FluentValidationFilter`) and throw `ValidationException` on failure.
4. Catch `ValidationException` in the global `UseExceptionHandler` and turn it into a `ValidationProblemDetails` 422 response.

Don't bring back the archived auto-validation package; it doesn't target ASP.NET Core 10.

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

- **Backend:** xUnit + FluentAssertions **pinned to 7.2.2** (last Apache-2.0 release before v8 commercial relicensing). Don't bump unless we accept the new license. `[Theory]` + `[InlineData]` / `[MemberData]` for table-driven cases. **Never** make tests dependent on test order.
- **Frontend:** Vitest + `@testing-library/angular`. Use `screen.getByRole(...)` over `getByTestId` when possible. (Vitest is the Angular CLI default since v20+; APIs are Jest-compatible for the surface we use.)
- **Integration:** ideally Testcontainers Postgres in a class fixture, but the Phase 3 implementation runs on **SQLite in-memory** because Docker is unavailable in this WSL dev environment. The `SchemaParityTests` boot both providers and assert the EF model is congruent, so SQLite-based integration tests are sufficient evidence the Postgres path works too. When CI gains Docker (Phase 11), add a parallel `Testcontainers.PostgreSql` fixture rather than swapping the existing one. **Never mock the DB** at the integration level — that lesson predates the WSL constraint.
- **Test-double pattern:** the canonical Application-layer test setup is `TestGameFactory` in `_TestDoubles/`, which composes `FakeClock` + `InMemoryGameRepository` + `RecordingGameEventBus` + `FakeTimerService` etc. New tests should reuse it; new test doubles go in `_TestDoubles/` and stay `internal`.

### Test runtime budget

The "no individual test > 500 ms; suite < 30 s" guideline from Phase 1 turned out to be conservative. Real numbers as of Phase 2:

- Domain suite: 2199 tests (incl. 2000 random-game property tests) in ~600 ms.
- Application suite: 48 tests in ~120 ms.
- Integration suite (Phase 4): 64 tests in ~3-4 s — Phase 3's 51 plus 13 REST scenarios via `WebApplicationFactory<Program>`. Each REST fixture spins up its own in-memory SQLite (per-class isolation), so the cost is dominated by host startup (~150 ms each) plus PBKDF2 hashing in Identity flows. If the REST suite drifts above ~10 s, the first lever is sharing one factory per class via `IClassFixture<T>` rather than instantiating in `IAsyncLifetime`.

If a test takes more than ~50 ms in isolation, ask whether it should — most application tests should be far below that. Property tests can take longer; that's fine.

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
