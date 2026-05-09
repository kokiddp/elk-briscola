# TODO — exhaustive, step-by-step development plan for elk-briscola

This is the **single source of truth for implementation work**. It is intentionally verbose and prescriptive: any LLM or human contributor should be able to pick up a sub-task and implement it without re-deriving design choices. When this document and [README.md](README.md) ever disagree, **README.md wins on architecture/contract questions** and this file is updated to match.

How to read this file:

- Phases are sequential. **Do not start phase N+1 until phase N's exit criteria are green.** Inside a phase, sub-steps within a "Step N" block are usually sequential too; sub-steps in different "Step N" blocks of the same phase can be parallelized only if explicitly noted.
- Every step has: **What**, **Where** (paths), **How** (concrete actions), **Why** (rationale, if non-obvious), **Tests**, **Acceptance**.
- Status markers: `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked. Update inline as work proceeds (see [AGENTS.md](AGENTS.md) for protocol).
- Size legend: `S` ≤ half a day · `M` ≤ two days · `L` 2–5 days. These are *informational only*; the only hard rule is "complete the step or break it down further; don't leave it half-done."

> Conventions used everywhere below: namespace prefix `Briscola.*`; C# 14 / .NET 10 (LTS); Angular 21 standalone components; Node 22 LTS; SCSS; UTC timestamps; no `cd` chains in scripts (use absolute paths). All code identifiers in this document are the **canonical** names — implement them exactly as written.

> **Read [AGENTS.md § Tooling environment](AGENTS.md#tooling-environment-wsl-gotchas--version-pins) before running any `dotnet` / `npm` / `npx` commands — it documents the WSL `~/.elk-env.sh` prelude and the version pins (.NET 10.0.203, Node v22.22.2, FluentAssertions 7.2.2, etc.) that the repo expects. AGENTS also has a "Records, equality, and `ImmutableArray<T>` known footgun" section that's load-bearing for Phase 3+ work — don't introduce wrapper types around `ImmutableArray<T>` without reading it first.**

---

## Table of contents

- [Phase 0 — Repo bootstrap](#phase-0--repo-bootstrap)
- [Phase 1 — Domain rules engine](#phase-1--domain-rules-engine)
- [Phase 2 — Application layer](#phase-2--application-layer)
- [Phase 3 — Infrastructure (EF Core, Identity, JWT)](#phase-3--infrastructure-ef-core-identity-jwt)
- [Phase 4 — REST API](#phase-4--rest-api)
- [Phase 5 — SignalR hubs](#phase-5--signalr-hubs)
- [Phase 6 — Angular foundation](#phase-6--angular-foundation)
- [Phase 7 — Lobby UI](#phase-7--lobby-ui)
- [Phase 8 — Game table UI](#phase-8--game-table-ui)
- [Phase 9 — Card sets](#phase-9--card-sets)
- [Phase 10 — Match history, ranking, spectator](#phase-10--match-history-ranking-spectator)
- [Phase 11 — Hardening](#phase-11--hardening)
- [Phase 12 — Containerization & deploy](#phase-12--containerization--deploy)
- [Phase 13 — End-to-end golden path](#phase-13--end-to-end-golden-path)
- [Phase 14 — Documentation polish & release](#phase-14--documentation-polish--release)
- [Out of scope for v1](#out-of-scope-for-v1)
- [Risk register](#risk-register)

---

## Phase 0 — Repo bootstrap

**Goal:** an empty but consistent monorepo that builds and runs trivially on CI. No business logic yet.

### Step 0.1 — Top-level files [S] [x]

**What:** create `.editorconfig`, `.gitignore`, `LICENSE`, empty stubs for `docs/`.

**Where:**
- `/.editorconfig`
- `/.gitignore`
- `/LICENSE` — MIT
- `/docs/architecture.md`, `/docs/game-rules.md`, `/docs/api.md`, `/docs/card-sets.md`, `/docs/deployment.md`, `/docs/security.md`
- `/docs/adr/0001-record-architecture-decisions.md` (Nygard template)

**How:**
- `.editorconfig` enforces: `end_of_line = lf`, `insert_final_newline = true`, `charset = utf-8`, `indent_style = space`, `indent_size = 4` for `.cs`, `indent_size = 2` for `.ts/.js/.json/.html/.scss/.yml/.md`, `trim_trailing_whitespace = true` everywhere except `.md`.
- `.gitignore` covers: `bin/`, `obj/`, `node_modules/`, `dist/`, `.angular/`, `*.user`, `.vs/`, `.idea/`, `.DS_Store`, `appsettings.*.local.json`, `.env`, `coverage/`, `TestResults/`, `playwright-report/`, `test-results/`.
- `docs/` files are one-liner stubs; they get filled in during Phase 14. Empty stubs prevent broken intra-repo links from earlier phases.

**Tests:** none.

**Acceptance:** `git status` shows clean tree after commit; all listed files exist.

---

### Step 0.2 — Backend skeleton (solution + projects, no code) [S] [x]

**What:** create the .NET 10 solution and four empty projects. No production code yet — just project files so CI can compile zero LOC.

**Where:**
- `backend/Briscola.sln`
- `backend/src/Briscola.Domain/Briscola.Domain.csproj` (`net10.0`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, no package refs)
- `backend/src/Briscola.Application/Briscola.Application.csproj` (refs `Briscola.Domain`)
- `backend/src/Briscola.Infrastructure/Briscola.Infrastructure.csproj` (refs `Briscola.Application`)
- `backend/src/Briscola.Api/Briscola.Api.csproj` (refs `Briscola.Infrastructure`; SDK `Microsoft.NET.Sdk.Web`)
- `backend/tests/Briscola.Domain.Tests/...csproj` (refs `Briscola.Domain`)
- `backend/tests/Briscola.Application.Tests/...csproj` (refs `Briscola.Application`)
- `backend/tests/Briscola.Api.IntegrationTests/...csproj` (refs `Briscola.Api`)

**How:**
- Each test csproj references xUnit (`xunit`, `xunit.runner.visualstudio`), `Microsoft.NET.Test.Sdk`, `FluentAssertions`. No more dependencies in this phase.
- Add `Directory.Build.props` at `backend/`: enables `TreatWarningsAsErrors` for `Release` config, sets `LangVersion=latest`, sets `<AnalysisLevel>latest-recommended</AnalysisLevel>`.
- `Briscola.Api`'s `Program.cs` is the trivial `WebApplication.CreateBuilder(args).Build().Run()` that returns 200 from `/`. This proves the solution compiles and runs.

**Tests:** add a single placeholder xUnit test in `Briscola.Domain.Tests` that asserts `1 == 1` so `dotnet test` exits 0.

**Acceptance:** `dotnet build backend/Briscola.sln --configuration Release` succeeds with 0 warnings (Release config is where `TreatWarningsAsErrors` applies); `dotnet test backend/Briscola.sln --configuration Release` reports 1 passing test.

---

### Step 0.3 — Frontend skeleton (Angular workspace) [M] [x]

**What:** initialize an Angular 21 standalone workspace with strict TS, SCSS, Vitest (the new CLI default), ESLint, and Prettier. Split into three sequential sub-steps.

**Where:** `frontend/`

#### Step 0.3a — Generate the Angular workspace [S]

- Run from repo root: `npx -p @angular/cli@21 ng new elk-briscola-frontend --directory=frontend --style=scss --strict --routing --skip-git --skip-install --ssr=false --package-manager=npm`.
- `cd frontend && npm install`.
- Replace the (giant) generated `app.html` with a single `<router-outlet />`.
- Trim `app.scss` to empty (kept for future global app styles).
- Trim `app.ts` to a minimal standalone component importing only `RouterOutlet` (drop the `signal('elk-briscola-frontend')` boilerplate).
- Drop unused image assets / favicon noise from `index.html` body if any.
- Verify: `npm run build` succeeds; `npm start` serves on `:4200`.

> Notes on Angular 21 generation defaults:
> - Standalone is the default; the legacy `--standalone` flag is a no-op (don't pass it).
> - The default class names are bare: `App` (not `AppComponent`), in `app.ts` / `app.html` / `app.scss` / `app.spec.ts`. Treat these names as canonical going forward and adjust later TODO steps that mention `AppComponent`.
> - Default test runner is **Vitest** (Karma + Jasmine retired). Step 0.3b reflects this.
> - `prettier` is included by default; we customize its config in Step 0.3c.

#### Step 0.3b — Vitest setup [S]

> **Plan revision (recorded here, not in a separate ADR — too small to warrant one):** the original plan called for a Karma → Jest migration. Angular 21's `ng new` no longer ships Karma; it ships Vitest. Vitest has API parity with Jest for the surface we use (`describe`, `it`/`test`, `expect`, mocks), works with `@testing-library/angular`, and is what the Angular CLI is now optimized for. We use Vitest. Anywhere later TODO steps reference Jest, treat them as Vitest.

- Add devDeps: `@testing-library/angular`, `@testing-library/jest-dom`. (Vitest, jsdom, and Prettier are already installed by `ng new`.)
- Verify `angular.json` already configures the `@angular/build:unit-test` builder with Vitest as the runner.
- Create `frontend/setup-vitest.ts`:
  ```ts
  import '@testing-library/jest-dom/vitest';
  ```
  Wire it via `angular.json` → `architect.test.options.setupFiles` (relative path).
- Replace the generated `src/app/app.spec.ts` with a trivial render test using `@testing-library/angular`:
  ```ts
  import { render } from '@testing-library/angular';
  import { App } from './app';
  test('renders without crashing', async () => {
    await render(App);
  });
  ```
- Update `package.json` scripts: keep `"test": "ng test"` (defers to Vitest via Angular CLI); add `"test:ci": "ng test --reporter=junit --reporter=default --output-file=test-results/junit.xml"`.
- Verify: `npm test -- --run` runs 1 passing test.

#### Step 0.3c — ESLint + Prettier [S]

- `npx ng add @angular-eslint/schematics@21 --skip-confirmation` (this also wires `npm run lint`).
- Add `prettier-plugin-organize-imports` as a devDep (prettier itself is already present).
- Create/replace `.prettierrc.json`: `{ "printWidth": 100, "singleQuote": true, "trailingComma": "all", "plugins": ["prettier-plugin-organize-imports"] }`. (`ng new` produced a `.prettierrc` with different settings — overwrite cleanly.)
- Create `.prettierignore`: `dist/`, `node_modules/`, `coverage/`, `.angular/`, `package-lock.json`.
- Add `package.json` scripts: `"format": "prettier --write ."`, `"format:check": "prettier --check ."`. (`lint` is already added by the schematic.)
- Run `npm run format` once to normalize generated files to our config; commit the result.
- Verify: `npm run lint` clean; `npm run format:check` clean.

**Tests:** the rewritten `app.spec.ts` (one test) passes under Vitest.

**Acceptance:** from `frontend/`, all of these succeed: `npm install`, `npm run build`, `npm test -- --run`, `npm run lint`, `npm run format:check`.

---

### Step 0.4 — GitHub Actions CI [S] [~] [!]

> **[!] partial**: workflow files written and YAML-validated locally; full acceptance ("workflows run and pass on a no-op PR") deferred until a GitHub remote exists. As of post-Phase-2: the same underlying commands all pass locally (`dotnet build/test -c Release`, `npm run lint/build/test:ci/format:check`) — 2247 tests, 0 warnings, 0 lint errors. When a remote is wired up, push to a branch, open a no-op PR, and flip this to `[x]` if the workflows go green.

**What:** wire CI so every PR is gated on build + test + lint.

**Where:**
- `.github/workflows/backend.yml`
- `.github/workflows/frontend.yml`
- `.github/workflows/e2e.yml` (skeleton — just echoes "skipped"; populated in Phase 13)

**How:**
- `backend.yml`: triggers on `pull_request` and `push: branches: [main]`. Steps: checkout → `actions/setup-dotnet@v4` (10.x) → `dotnet restore` → `dotnet build --configuration Release --no-restore` → `dotnet test --configuration Release --no-build --logger "trx" --collect:"XPlat Code Coverage"` → upload coverage artifact.
- `frontend.yml`: triggers same. Steps: checkout → `actions/setup-node@v4` (22.x, `cache: 'npm'`, `cache-dependency-path: frontend/package-lock.json`) → `npm ci` (in `frontend/`) → `npm run lint` → `npm run build` → `npm run test:ci`.
- Add a `concurrency` block keyed on PR number so newer pushes cancel older runs.
- Add a status badge to README in Phase 14.

**Tests:** open a PR with no changes; both workflows must go green.

**Acceptance:** workflows run and pass on a no-op PR.

---

### Step 0.5 — Repo conventions doc cross-link [S] [x]

**What:** ensure `AGENTS.md` exists at repo root (created separately from this plan) and is referenced from `README.md`.

**Where:** `/AGENTS.md`, `/README.md`.

**How:** README's intro paragraph links `[AGENTS.md](AGENTS.md)` for contributors/LLMs.

**Acceptance:** dead-link checker (Phase 14 will add) finds no broken refs.

**Phase 0 exit:** CI green on a no-op PR; both backend and frontend skeletons compile and test; `docs/`, `AGENTS.md`, `README.md`, `TODO.md` all present.

---

## Phase 1 — Domain rules engine

**Goal:** a pure C# library that correctly implements Briscola, with exhaustive unit tests. **No I/O, no DB, no ASP.NET, no JSON, no logging.** The only types referenced from outside the BCL are types defined within `Briscola.Domain` itself.

> The domain layer must be implementable from this section alone. If you find yourself reading the README to know what to build here, file a TODO update.

### Step 1.1 — Value types & enums [S] [x]

**What:** the primitive vocabulary of the engine.

**Where:** `backend/src/Briscola.Domain/Primitives/*.cs`

**How — exact definitions:**

```csharp
namespace Briscola.Domain.Primitives;

public enum Suit { Bastoni, Coppe, Denari, Spade }

// IMPORTANT: Rank carries no implicit numeric semantics.
// Strength and points must be looked up via CardTables.
public enum Rank { Asso, Tre, Re, Cavallo, Fante, Sette, Sei, Cinque, Quattro, Due }

public enum GameMode  { TwoPlayer, FourPlayerTeams }
public enum GamePhase { Dealing, Playing, LastHand, Finished }
public enum GameStatus { Open, Running, Finished, Abandoned }

public readonly record struct Card(Suit Suit, Rank Rank);

public readonly record struct Seat(int Index, Guid? PlayerId);
```

- `Seat.PlayerId` is nullable because the engine itself is identity-agnostic; the application layer fills it in. The engine only cares about `Index`.
- `Card` is a `record struct` for value semantics and hash-equality (used in `HashSet<Card>` for hand membership tests).

**Tests:** none in this step; types are exercised by later steps.

**Acceptance:** file compiles; no using-statements outside `System.*`.

---

### Step 1.2 — `CardTables` (strength + points) [S] [x]

**What:** pure static lookup tables.

**Where:** `backend/src/Briscola.Domain/Primitives/CardTables.cs`

**How:**

```csharp
public static class CardTables
{
    public static int Strength(Rank r) => r switch
    {
        Rank.Asso => 10, Rank.Tre => 9, Rank.Re => 8, Rank.Cavallo => 7,
        Rank.Fante => 6, Rank.Sette => 5, Rank.Sei => 4, Rank.Cinque => 3,
        Rank.Quattro => 2, Rank.Due => 1,
    };

    public static int Points(Rank r) => r switch
    {
        Rank.Asso => 11, Rank.Tre => 10, Rank.Re => 4, Rank.Cavallo => 3, Rank.Fante => 2,
        _ => 0,
    };

    public static IReadOnlyCollection<Card> FullDeck { get; } =
        Enum.GetValues<Suit>()
            .SelectMany(s => Enum.GetValues<Rank>().Select(r => new Card(s, r)))
            .ToArray();

    public const int TotalDeckPoints = 120;
}
```

**Tests** (`Briscola.Domain.Tests/CardTablesTests.cs`):
- `Points_summed_over_full_deck_equals_120`.
- `Strength_returns_distinct_values_per_rank` (all 10 distinct).
- `Strength_ordering_matches_canonical_order` (A>3>K>Q>J>7>6>5>4>2).
- `FullDeck_has_40_distinct_cards`.
- **Regression guard:** `Strength_is_not_derived_from_enum_underlying_int` — assert that `(int)Rank.Asso != Strength(Rank.Asso)` (since `(int)Rank.Asso == 0`). This locks down the original spec bug.

**Acceptance:** all 5 tests green.

---

### Step 1.3 — `IRandomSource` and `SeededRandomSource` [S] [x]

**What:** seedable RNG abstraction; the engine uses it for shuffles.

**Where:**
- `backend/src/Briscola.Domain/Primitives/IRandomSource.cs`
- `backend/src/Briscola.Domain/Primitives/SeededRandomSource.cs`

**How:**

```csharp
public interface IRandomSource
{
    long Seed { get; }
    int Next(int maxExclusive);   // [0, maxExclusive)
}

public sealed class SeededRandomSource : IRandomSource
{
    private readonly Random _rng;
    public long Seed { get; }
    public SeededRandomSource(long seed) { Seed = seed; _rng = new Random(unchecked((int)seed)); }
    public int Next(int maxExclusive) => _rng.Next(maxExclusive);
}
```

- The engine accepts `IRandomSource` *into* `StartGame` (NOT into `BriscolaEngine`'s constructor) so each game is deterministic given its seed, but the engine instance is stateless.

**Tests:** `SameSeed_yields_same_sequence` over 1000 calls.

**Acceptance:** test green.

---

### Step 1.4 — `Deck` shuffler [S] [x]

**What:** Fisher-Yates shuffle of `CardTables.FullDeck` using `IRandomSource`.

**Where:** `backend/src/Briscola.Domain/Primitives/Deck.cs`

**How:**

```csharp
internal static class Deck
{
    public static Card[] Shuffled(IRandomSource rng)
    {
        var deck = CardTables.FullDeck.ToArray();
        for (int i = deck.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
        return deck;
    }
}
```

**Tests:**
- `Shuffled_returns_40_distinct_cards`.
- `Shuffled_with_same_seed_is_identical`.
- `Shuffled_with_different_seeds_differs` (statistical: assert at least one position differs, with overwhelming probability).

**Acceptance:** all green; `Deck` is `internal` (not exposed outside the domain assembly). Add `[assembly: InternalsVisibleTo("Briscola.Domain.Tests")]` in `backend/src/Briscola.Domain/AssemblyInfo.cs` so the test project can call `Deck.Shuffled`.

---

### Step 1.5 — Hand representation [S] [x]

**What:** decide how a per-seat hand is represented in `GameState`.

**Decision:** **no `Hand` wrapper class**. Hands are `ImmutableArray<Card>` directly, used positionally by seat index in `GameState.Hands` (see Step 1.6).

**Why no wrapper:**
- `GameState` is a `record`; record-equality on a wrapper class would require a custom `Equals` because `ImmutableArray<T>` uses reference equality. Either we drop into custom equality everywhere or we just don't introduce the abstraction.
- We do not actually compare `GameState`s for equality in production paths (we serialize them). Tests assert specific fields, not whole-state equality.
- Hand size is at most ~3 cards in steady state; "O(1) membership" is a non-concern.

**Helpers** in `backend/src/Briscola.Domain/Primitives/HandHelpers.cs`:

```csharp
internal static class HandHelpers
{
    public static ImmutableArray<Card> Without(this ImmutableArray<Card> hand, Card c)
    {
        var i = hand.IndexOf(c);
        if (i < 0) throw new InvalidOperationException($"Card {c} not in hand");
        return hand.RemoveAt(i);
    }

    public static ImmutableArray<Card> With(this ImmutableArray<Card> hand, Card c)
        => hand.Add(c);
}
```

**Tests** (`HandHelpersTests.cs`): `With_then_Without_is_identity`; `Without_missing_throws`; `Contains_works_for_all_40_cards` (just `hand.Contains(c)`, asserting we don't need our own membership method).

---

### Step 1.6 — `GameState` immutable record [M] [x]

**What:** the snapshot type; *all* engine transitions take a `GameState` and return a new `GameState`.

**Where:** `backend/src/Briscola.Domain/State/GameState.cs`

**How — exact shape:**

```csharp
public sealed record GameState
{
    public required Guid GameId { get; init; }
    public required GameMode Mode { get; init; }
    public required long ShuffleSeed { get; init; }
    public required int DealerSeat { get; init; }
    public required ImmutableArray<ImmutableArray<Card>> Hands { get; init; }  // index = seat
    public required ImmutableArray<ImmutableArray<Card>> Pozzi { get; init; } // pile per seat
    public required ImmutableArray<Card> Stock { get; init; }      // tail = briscola card
    public required Card BriscolaCard { get; init; }
    public required Suit BriscolaSuit { get; init; }
    public required ImmutableArray<PlayedCard> CurrentTrick { get; init; }
    public required int LeaderSeat { get; init; }
    public required int NextToPlaySeat { get; init; }
    public required GamePhase Phase { get; init; }
    public required int TrickNumber { get; init; }                 // starts at 0, increments after resolution
    public required ImmutableArray<int> SeatScores { get; init; }  // recomputed after each trick resolution
    public GameOutcome? Outcome { get; init; }                     // set only when Phase == Finished
}

public readonly record struct PlayedCard(int SeatIndex, Card Card);

public abstract record GameOutcome
{
    public sealed record Winner(int SeatOrTeam) : GameOutcome;
    public sealed record Draw : GameOutcome;
}
```

- `Pozzi[seat]` is the cumulative pile of cards the seat (or its team — see scoring) has won. For 4p, individual pozzi are tracked; team scores are derived.
- `BriscolaCard` is duplicated (it also lives at `Stock[^1]` until drawn) for ergonomics: external observers always have one place to read it from. Once it is drawn, `BriscolaCard` is unchanged but `Stock` no longer contains it.
- Invariant: `SeatScores.Sum() <= 120` always, `== 120` only when `Phase == Finished`.

**Tests** (in step 1.10's batch): construction sanity; required-init enforcement.

---

### Step 1.7 — `InvalidMoveException` [S] [x]

**What:** typed exception with stable error codes.

**Where:** `backend/src/Briscola.Domain/Errors/InvalidMoveException.cs`

**How:**

```csharp
public enum InvalidMoveCode
{
    NotYourTurn,
    CardNotInHand,
    GameFinished,
    WrongPhase,
    PileViewNotAllowed,
}

public sealed class InvalidMoveException : Exception
{
    public InvalidMoveCode Code { get; }
    public InvalidMoveException(InvalidMoveCode code, string? message = null)
        : base(message ?? code.ToString()) { Code = code; }
}
```

- The application layer maps `Code` → REST `ProblemDetails.type` URI and SignalR error payload. The string codes are part of the **public API contract**.

---

### Step 1.8 — `BriscolaEngine` — `StartGame` [M] [x]

**What:** the deal.

**Where:** `backend/src/Briscola.Domain/Engine/BriscolaEngine.cs`

**Inputs:**

```csharp
public sealed record GameSetup(
    Guid GameId,
    GameMode Mode,
    int DealerSeat,           // valid range: 0..PlayerCount-1
    ImmutableArray<Guid> PlayerIds);  // index = seat
```

**Algorithm:**

1. Validate: `PlayerIds.Length` must be 2 if `Mode == TwoPlayer`, 4 if `FourPlayerTeams`. Otherwise throw `ArgumentException`.
2. Validate: `DealerSeat` in range.
3. `var deck = Deck.Shuffled(rng);` — array of 40 cards.
4. **Deal three cards each, round-robin, starting with the seat immediately after the dealer in seat order.** Three rounds, one card per player per round:
   - Round 1: card `deck[0]` → leader (seat `(dealer+1) % N`); `deck[1]` → next seat; …; `deck[N-1]` → dealer.
   - Round 2: card `deck[N]` → leader; … `deck[2N-1]` → dealer.
   - Round 3: same pattern, cards `deck[2N..3N-1]`.

   This matches the traditional one-at-a-time deal. The result is *not* the same as 3 contiguous cards per player; tests must reflect the round-robin layout.
5. Take the *next* card after the deal as `briscolaCard = deck[3*N]`. Its suit becomes the trump.
6. The remaining stock is `deck[3*N + 1 .. 39]` followed by `briscolaCard` at the **tail** (so the briscola is literally the last element of `Stock` and will be the last drawn).
7. `LeaderSeat = NextToPlaySeat = (DealerSeat + 1) % N`.
8. `Phase = GamePhase.Playing`.
9. `Pozzi` initialized to N empty arrays; `SeatScores` to N zeros; `CurrentTrick` empty; `TrickNumber = 0`.

**Signature:**

```csharp
public sealed class BriscolaEngine : IBriscolaEngine
{
    public GameState StartGame(GameSetup setup, IRandomSource rng);
    public GameState PlayCard(GameState state, int seatIndex, Card card);
    public bool IsLegalMove(GameState state, int seatIndex, Card card);
}
```

`IBriscolaEngine` lives at `backend/src/Briscola.Domain/Engine/IBriscolaEngine.cs`.

**Tests** (step 1.10).

---

### Step 1.9 — `BriscolaEngine` — `PlayCard`, trick resolution, drawing [M] [x]

**What:** the per-move state transition.

**Algorithm — pure-function, returns a new `GameState`:**

1. **Pre-checks:**
   - If `state.Phase == Finished` → throw `InvalidMoveException(GameFinished)`.
   - If `seatIndex != state.NextToPlaySeat` → `NotYourTurn`.
   - If `!state.Hands[seatIndex].Contains(card)` → `CardNotInHand`.
2. **Apply the play:**
   - `hand' = state.Hands[seatIndex].Without(card)` (extension from `HandHelpers`).
   - `hands' = state.Hands.SetItem(seatIndex, hand')`.
   - `currentTrick' = state.CurrentTrick.Add(new PlayedCard(seatIndex, card))`.
3. **If trick is incomplete** (`currentTrick'.Length < N`):
   - `next' = (seatIndex + 1) % N`.
   - return `state with { Hands = state.Hands.SetItem(seatIndex, hand'), CurrentTrick = currentTrick', NextToPlaySeat = next' }`.
4. **If trick is complete** (`currentTrick'.Length == N`):
   - **Resolve winner:**
     - `leadSuit = currentTrick'[0].Card.Suit`.
     - Find any briscola plays: `briscolas = currentTrick'.Where(p => p.Card.Suit == BriscolaSuit)`.
     - If `briscolas.Any()`: winner is `briscolas.MaxBy(p => CardTables.Strength(p.Card.Rank))`.
     - Else: winner is `currentTrick'.Where(p => p.Card.Suit == leadSuit).MaxBy(p => CardTables.Strength(p.Card.Rank))` (this set is non-empty because the leader played a `leadSuit` card).
     - `winnerSeat = winner.SeatIndex`.
   - **Update pozzo:**
     - `pozzi' = state.Pozzi.SetItem(winnerSeat, state.Pozzi[winnerSeat].AddRange(currentTrick'.Select(p => p.Card)))`.
   - **Recompute scores** (always recompute from `pozzi'` to keep this pure):
     - `seatScores' = pozzi'.Select(pile => pile.Sum(c => CardTables.Points(c.Rank))).ToImmutableArray()`.
   - **Drawing phase** (only if `state.Stock.Length > 0`):
     - Build draw order: `[winnerSeat, winnerSeat+1, …, winnerSeat+N-1]` (mod N).
     - For each seat in draw order, while `stock'.Length > 0`, pop the **head** of `stock'` (i.e. `stock'[0]`) into that seat's hand.
       - Why head, not tail: the briscola card sits at the **tail** of the stock array. We consume head-first, so the tail (= the briscola) is the last card to be drawn — exactly what the rules require.
       - Practical implementation: `Queue<Card>` constructed from `state.Stock` works; we just guarantee the briscola is the last item enqueued at setup time (step 1.8 above).
     - Stop early if `stock'` becomes empty mid-trick. This is normal: the trick that exhausts the stock may not deal a card to every seat (e.g. in a 4p game where the stock had only 2 cards left at trick start, only the first two seats in draw order receive a card).
   - **Phase transition:**
     - If `stock'.Length == 0` and every player's hand has count == 3 (i.e. we just drew the last cards from the stock), transition `Phase` to `LastHand`.
     - If every player's hand is empty, `Phase = Finished`; compute `Outcome` (see below).
     - Else stay in `Playing` (or remain in `LastHand` if already there).
   - **Compute `Outcome`** (only on transition to `Finished`):
     - For 2p: compare `seatScores'[0]` vs `seatScores'[1]`.
     - For 4p: `teamA = seatScores'[0] + seatScores'[2]`, `teamB = seatScores'[1] + seatScores'[3]`.
     - Strict greater → `Winner(seat)` for 2p or `Winner(teamId)` for 4p (`teamId == 0` for A, `1` for B).
     - Equal → `Draw`.
   - **Final invariant assert:** if `Phase == Finished`, assert `seatScores'.Sum() == 120`. If not, throw `InvalidOperationException("score-sum invariant violated")`. This is a *runtime* check, not just a test.
   - **Next leader / next-to-play:** `LeaderSeat' = winnerSeat`; `NextToPlaySeat' = winnerSeat`.
   - Return new state with `CurrentTrick = []`, `TrickNumber = state.TrickNumber + 1`, etc.

**`IsLegalMove`:** returns `true` iff none of the pre-checks would throw. Pure boolean version of `PlayCard` validation.

---

### Step 1.10 — `Briscola.Domain.Tests` — exhaustive coverage [L] [x]

**Where:** `backend/tests/Briscola.Domain.Tests/`

**Test classes** (one per concern):

- `CardTablesTests.cs` — see step 1.2.
- `DeckTests.cs` — see step 1.4.
- `EngineSetupTests.cs`:
  - `Deal_2p_gives_3_cards_each` (parametrized over dealer seat ∈ {0,1}).
  - `Deal_4p_gives_3_cards_each` (parametrized over dealer seat ∈ {0..3}).
  - `Briscola_card_is_at_stock_tail`.
  - `Stock_count_initial_2p_equals_33` (40 - 6 - 1, with briscola in stock).
  - `Stock_count_initial_4p_equals_27`.
  - `Leader_is_seat_after_dealer_in_seat_order`.
  - `Leader_hand_equals_deck_indices_0_N_2N` (regression test for the round-robin deal order: leader holds `deck[0]`, `deck[N]`, `deck[2N]`).
- `EngineLegalityTests.cs`:
  - `PlayCard_when_not_my_turn_throws_NotYourTurn`.
  - `PlayCard_card_not_in_hand_throws_CardNotInHand`.
  - `PlayCard_after_finished_throws_GameFinished`.
  - `Any_card_in_hand_is_legal` (no must-follow-suit).
- `TrickResolutionTests.cs` — table-driven xUnit `[Theory]`:
  - All four winner cases:
    1. All four (or both) players play lead suit, no briscolas → highest of lead suit wins.
    2. Exactly one player plays a briscola → that player wins.
    3. Multiple briscolas → highest-strength briscola wins.
    4. No briscolas, mixed suits → highest of lead suit (off-suit non-briscolas don't compete).
  - Verify trick winner becomes new leader.
- `DrawingTests.cs`:
  - `Winner_draws_first_then_seat_order`.
  - `Last_card_drawn_is_the_briscola`.
  - `Stock_count_decreases_by_player_count_per_trick_until_exhausted`.
  - `When_stock_has_fewer_than_player_count_some_seats_skip_draw_in_that_trick`.
- `PhaseTests.cs`:
  - `Phase_is_LastHand_after_stock_and_briscola_drawn`.
  - `Phase_is_Finished_after_all_hands_empty`.
  - `Phase_is_Playing_otherwise`.
- `ScoringTests.cs`:
  - `SeatScores_sum_equals_120_at_finish` (parametrized over 100 random seeds).
  - `Team_score_equals_sum_of_seat_scores` (4p).
  - `Draw_at_60_60_yields_GameOutcome_Draw`.
  - `Higher_score_yields_GameOutcome_Winner`.
- `DeterminismTests.cs`:
  - Helper: `PlayRandomLegalGame(seed)` — picks a random legal move per turn, returns final state.
  - `Same_seed_same_random_choices_yields_same_state` (with a deterministic move-picker).
- `PropertyTests.cs`:
  - For 1000 seeds: `var s = PlayRandomLegalGame(seed); s.Phase.Should().Be(Finished); s.SeatScores.Sum().Should().Be(120); s.Outcome.Should().NotBeNull();`.
- `ScoreSumInvariantTests.cs`:
  - Use a **mutating reflection hack** to corrupt a `GameState` so scores don't sum to 120, feed into a contrived final-trick scenario, assert `InvalidOperationException("score-sum invariant violated")` is thrown. Documents that the invariant is enforced at runtime.

**Acceptance:**
- All tests pass.
- Line coverage on `Briscola.Domain` ≥ 95% (measured via `coverlet.collector` + `XPlat Code Coverage`; CI uploads the report).
- No individual test takes > 500 ms (the property test is the only candidate; tune iteration count if needed). Full domain test suite < 30 s on CI.

**Phase 1 exit:** all tests above green; `dotnet build` clean; `Briscola.Domain` has zero references outside `System.*`.

---

## Phase 2 — Application layer

**Goal:** rules wrapped in use-cases; persistence/identity/clock behind interfaces; orchestrator with single-writer concurrency.

### Step 2.1 — Project setup [S] [x]

**Where:** `backend/src/Briscola.Application/`

**Refs:** `Briscola.Domain` (project), `Microsoft.Extensions.Logging.Abstractions` (NuGet), `Microsoft.Extensions.Options` (NuGet). NO EF Core, NO ASP.NET, NO SignalR.

---

### Step 2.2 — Ports (interfaces) [S] [x]

**Where:** `backend/src/Briscola.Application/Ports/`

**Files:**

- `IClock.cs` — `DateTimeOffset UtcNow { get; }`. Default impl `SystemClock` provided here for non-test use; the application layer's DI registers it.
- `IRandomSource.cs` — re-exposes `Briscola.Domain.Primitives.IRandomSource`.
- `IRandomSourceFactory.cs` — creates a fresh `IRandomSource` per game start so persisted shuffle seeds are replayable.
- `IUserContext.cs` — `Guid UserId { get; }`, `string UserName { get; }`. Implementation lives in `Briscola.Api`.
- `IGameRepository.cs`:
  ```csharp
  Task<GameRecord?> GetAsync(Guid id, CancellationToken ct);
  Task<IReadOnlyList<GameRecord>> ListByStatusAsync(GameStatus status, int take, CancellationToken ct);
  Task CreateAsync(GameRecord record, CancellationToken ct);
  Task<bool> UpdateAsync(GameRecord record, CancellationToken ct);
  Task AppendMoveAsync(Guid gameId, MoveRecord move, CancellationToken ct);
  Task SaveResultAsync(GameResultRecord result, CancellationToken ct);
  ```
- `IChatRepository.cs`:
  ```csharp
  Task AppendAsync(ChatMessageRecord m, CancellationToken ct);
  Task<IReadOnlyList<ChatMessageRecord>> ReadAsync(ChatScope scope, Guid? gameId, int take, CancellationToken ct);
  ```
- `IRankingRepository.cs`:
  ```csharp
  Task<RankingRecord> GetAsync(Guid userId, CancellationToken ct);
  Task UpdateAsync(RankingRecord record, CancellationToken ct);
  ```
- `IGameEventBus.cs` — outbound events, **consumed by the API layer's `GameEventDispatcher` hosted service**:
  ```csharp
  ValueTask PublishAsync(IGameEvent evt, CancellationToken ct = default);
  IAsyncEnumerable<IGameEvent> ReadAllAsync(CancellationToken ct);   // single multiplexed reader
  ```
  - In-memory implementation lives in `Briscola.Application/Bus/InMemoryGameEventBus.cs`: a single `Channel<IGameEvent>(UnboundedChannelOptions { SingleReader = true, SingleWriter = false })`. The dispatcher is the only consumer; it routes by `evt.GameId` to the right SignalR group. Multi-instance horizontal scaling is **not** a v1 requirement.
  - For tests, a `RecordingGameEventBus` captures published events into a list and exposes them synchronously.
- `IGameStateCodec.cs` — opaque snapshot serialization boundary:
  ```csharp
  string Serialize(GameState state);
  GameState Deserialize(string snapshot);
  ```
  The Application layer depends on this port so JSON stays in Infrastructure.
- `IGamePasswordHasher.cs` — game-room password hashing boundary:
  ```csharp
  string Hash(string password);
  bool Verify(string password, string hash);
  ```
  Concrete hashing lives outside Application.

**Records used by ports** (in `Briscola.Application/Persistence/Records.cs`):

```csharp
public sealed record GameRecord(
    Guid Id, GameMode Mode, string Name, GameStatus Status,
    Guid CreatedByUserId, DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt, DateTimeOffset? EndedAt,
    long ShuffleSeed, string StateSnapshotJson,
    Suit BriscolaSuit, bool IsPrivate, string? PasswordHash,
    ImmutableArray<Guid?> SeatUserIds, long Version);

public sealed record MoveRecord(
    Guid Id, Guid GameId, int MoveIndex, int SeatIndex,
    MoveType Type, string PayloadJson, DateTimeOffset CreatedAt);

public enum MoveType { PlayCard, Forfeit, Disconnect, Reconnect, IdleTimeout }

public sealed record GameResultRecord(
    Guid GameId, GameOutcomeKind Kind, int? WinnerKey,
    string SeatScoresJson, string? TeamScoresJson, EndedReason Reason);

public enum GameOutcomeKind { Win, Draw }
public enum EndedReason { Normal, ForfeitDisconnect, ForfeitIdle }

public sealed record ChatMessageRecord(
    Guid Id, ChatScope Scope, Guid? GameId, Guid UserId,
    string Text, DateTimeOffset CreatedAt);

public enum ChatScope { Lobby, Game }

public sealed record RankingRecord(
    Guid UserId, int Elo, int Wins, int Losses, int Draws, int GamesPlayed, DateTimeOffset UpdatedAt);
```

`IRankingRepository` also exposes `HasProcessedGameAsync(gameId, ct)` and `MarkProcessedGameAsync(gameId, ct)` so `RankingService` can enforce result idempotency without coupling itself to a concrete schema.

---

### Step 2.3 — `GameOptions` [S] [x]

**Where:** `backend/src/Briscola.Application/Configuration/GameOptions.cs`

```csharp
public sealed class GameOptions
{
    public const string SectionName = "Game";
    public int ReconnectGraceSeconds { get; init; } = 120;
    public int IdleWarnSeconds { get; init; } = 90;
    public int IdleForfeitSeconds { get; init; } = 180;
    public int OpenLobbyTtlMinutes { get; init; } = 60;
    public string FourPlayerForfeitMode { get; init; } = "TeamForfeit"; // or "WaitForBackup"
}
```

Bound from the config section `Game` in `Briscola.Api`'s `Program.cs`. **Always inject `IOptions<GameOptions>` (snapshot) — never read configuration directly in the application layer.**

---

### Step 2.4 — `GameOrchestrator` & `GameRoom` [M] [x]

**Where:**
- `backend/src/Briscola.Application/Orchestration/GameOrchestrator.cs`
- `backend/src/Briscola.Application/Orchestration/GameRoom.cs`
- `backend/src/Briscola.Application/Orchestration/Commands/*.cs`
- `backend/src/Briscola.Application/Orchestration/Events/*.cs`

**Commands** (records). Note: there is no `JoinGameCommand` or `LeaveGameCommand` in the room — pre-game lobby flow lives entirely on `LobbyService`. The room handles only commands relevant to a `Running` game:

```csharp
public abstract record GameCommand(Guid GameId);
public sealed record PlayCardCommand(Guid GameId, Guid UserId, Card Card) : GameCommand(GameId);
public sealed record ViewOwnPileCommand(Guid GameId, Guid UserId) : GameCommand(GameId);
public sealed record DisconnectCommand(Guid GameId, Guid UserId) : GameCommand(GameId);
public sealed record ReconnectCommand(Guid GameId, Guid UserId) : GameCommand(GameId);
public sealed record IdleTickCommand(Guid GameId, DateTimeOffset At) : GameCommand(GameId);
public sealed record ForfeitOnDisconnectCommand(Guid GameId, int SeatIndex) : GameCommand(GameId);
```

**Events** (records implementing `IGameEvent`):

```csharp
public interface IGameEvent { Guid GameId { get; } DateTimeOffset At { get; } }
public sealed record JoinedEvent(Guid GameId, DateTimeOffset At, RedactedStateForUser Snapshot, Guid TargetUserId) : IGameEvent;
public sealed record StateUpdatedEvent(Guid GameId, DateTimeOffset At, RedactedStateForUser Snapshot, Guid TargetUserId) : IGameEvent;
public sealed record CardPlayedEvent(Guid GameId, DateTimeOffset At, int SeatIndex, Card Card) : IGameEvent;
public sealed record TrickResolvedEvent(Guid GameId, DateTimeOffset At, int WinnerSeat, ImmutableArray<int> NewSeatScores) : IGameEvent;
public sealed record CardsDrawnEvent(Guid GameId, DateTimeOffset At, ImmutableArray<int> CountsBySeat, /* per-recipient hand-delta filled by API layer */ Card? DrawnCard, Guid? TargetUserId) : IGameEvent;
public sealed record PhaseChangedEvent(Guid GameId, DateTimeOffset At, GamePhase NewPhase) : IGameEvent;
public sealed record GameFinishedEvent(Guid GameId, DateTimeOffset At, GameOutcome Outcome, ImmutableArray<int> SeatScores, EndedReason Reason) : IGameEvent;
public sealed record PlayerDisconnectedEvent(Guid GameId, DateTimeOffset At, int SeatIndex, DateTimeOffset GraceDeadlineUtc) : IGameEvent;
public sealed record PlayerReconnectedEvent(Guid GameId, DateTimeOffset At, int SeatIndex) : IGameEvent;
public sealed record ChatMessageEvent(Guid GameId, DateTimeOffset At, Guid FromUserId, string FromDisplayName, ChatScope Scope, string Text) : IGameEvent;
public sealed record InvalidMoveRejectedEvent(Guid GameId, DateTimeOffset At, Guid TargetUserId, InvalidMoveCode Code) : IGameEvent;
public sealed record IdleWarningEvent(Guid GameId, DateTimeOffset At, int SeatIndex, DateTimeOffset ForfeitDeadlineUtc) : IGameEvent;
```

`RedactedStateForUser` is built by the room when a `JoinedEvent` or `StateUpdatedEvent` is emitted: each recipient gets their own copy (their hand visible, others' hand counts only).

```csharp
public sealed record RedactedStateForUser(
    Guid GameId, GameMode Mode, GamePhase Phase, int DealerSeat,
    int LeaderSeat, int NextToPlaySeat, int TrickNumber,
    Card BriscolaCard, Suit BriscolaSuit, int StockCount,
    ImmutableArray<int> HandCountsBySeat,
    ImmutableArray<Card>? MyHand,                 // null for spectators
    ImmutableArray<Card>? MyPozzo,                // available only on viewOwnPile during LastHand
    ImmutableArray<PlayedCard> CurrentTrick,
    ImmutableArray<int> SeatScores,
    GameOutcome? Outcome);
```

**`GameRoom`** — one per active game:

- Holds a `Channel<GameCommand>` (`UnboundedChannel<>` in v1; revisit if backpressure becomes a concern).
- A single background `Task` (`ProcessLoop`) reads commands and applies them serially. **All state mutation happens on this task.** No locks, no shared mutable state.
- Exposes `Task EnqueueAsync(GameCommand cmd)` for callers.
- Holds in-memory: current `GameState`, `Dictionary<Guid, int> userIdToSeat`, `Dictionary<int, ConnectionStatus> seatStatuses`, `Dictionary<int, IDisposable> reconnectTimers`.

**`GameOrchestrator`** — singleton service:

- `ConcurrentDictionary<Guid, GameRoom> _rooms`.
- `GetOrCreate(gameId, lazyFactory)`.
- Hot-reload from persistence on startup (`HydrateAsync`): for each `GameRecord` with `Status == Running`, deserialize `StateSnapshotJson` into a `GameState` and create a `GameRoom`.

**Process loop semantics:**

A room exists only for `Running` (or `Finished`) games. Lobby-phase records live in the repository alone; `LobbyService` mutates them directly without a room.

For each command popped from the channel:
1. Apply via the engine (`PlayCard`) or via internal handlers (`Disconnect`, `Reconnect`, `IdleTick`, `ForfeitOnDisconnect`, `ViewOwnPile`). Unknown command types throw `GameCommandException`.
2. After successful state mutation:
   - Persist `GameRecord.StateSnapshotJson` (single UPSERT on `Games` with optimistic concurrency on `Version`).
   - Persist a `MoveRecord` (for `PlayCard`, `Forfeit`, `Disconnect`, `Reconnect`, `IdleTimeout`). The room hydrates `_moveIndex` from the repository on first run so the log keeps a strictly-increasing sequence across process restarts.
   - Publish `IGameEvent`s to `IGameEventBus`.

**Why a single channel per game and not per process:** keeps the concurrency model trivially correct (per-game serial), avoids global lock contention, scales to many concurrent games on one box.

---

### Step 2.5 — Reconnect & idle timers [M] [x]

**Where:** `backend/src/Briscola.Application/Orchestration/Timers/`

**Mechanism:**
- The `GameRoom` does **not** spawn `Task.Delay`s directly. Instead it schedules `IdleTickCommand` via an injected `ITimerService` that is itself driven by `IClock`.
- `ITimerService.ScheduleAt(DateTimeOffset, Func<CancellationToken, ValueTask>) -> IDisposable`.
- Default impl `SystemTimerService` uses `System.Threading.Timer`. Test impl `FakeTimerService` with virtual time advanced by tests.

**On `DisconnectCommand`:**
- `seatStatuses[seat] = Disconnected`.
- `deadline = clock.UtcNow + GameOptions.ReconnectGraceSeconds`.
- Schedule a callback that enqueues a `ForfeitOnDisconnectCommand(seat)` if status is still `Disconnected` at deadline.
- Emit `PlayerDisconnectedEvent(seat, deadline)`.

**On `ReconnectCommand`:**
- `seatStatuses[seat] = Connected`.
- Cancel the disconnect timer.
- Emit `PlayerReconnectedEvent(seat)` and a fresh `JoinedEvent` (full snapshot) to the reconnecting user.

**On `IdleTickCommand`:**
- Compute `idleSince = state's last move-completion timestamp`.
- If `idleSince + IdleForfeitSeconds <= now` and the seat at `NextToPlaySeat` is the one idling → `Forfeit`.
- Else if `idleSince + IdleWarnSeconds <= now` and not yet warned → emit a soft warning event (no state change).

**Forfeit on disconnect / idle:**
- For 2p: opposing seat wins; record `EndedReason.ForfeitDisconnect` or `ForfeitIdle`.
- For 4p with `FourPlayerForfeitMode = TeamForfeit` (default): the disconnected player's team forfeits; the other team wins.
- `Phase = Finished`; emit `GameFinishedEvent`. Persist `GameResultRecord`.

---

### Step 2.6 — `LobbyService` [S] [x]

**Where:** `backend/src/Briscola.Application/Lobby/LobbyService.cs`

**Methods:**

```csharp
Task<GameRecord> CreateAsync(CreateGameRequest req, Guid creatorUserId, CancellationToken ct);
Task<IReadOnlyList<GameSummary>> ListAsync(GameStatus status, CancellationToken ct);
Task<GameRecord> JoinAsync(Guid gameId, Guid userId, string? password, CancellationToken ct);
Task LeaveAsync(Guid gameId, Guid userId, CancellationToken ct);
```

**`JoinAsync` flow:**
1. Load record.
2. Validate `Status == Open`.
3. If `IsPrivate`, validate password against stored hash (BCrypt or PBKDF2 — same scheme as user passwords).
4. Add user to next free seat (transactionally — see "Race conditions" below).
5. If seats are now full: transition `Status` to `Running`, set `StartedAt`, call `BriscolaEngine.StartGame`, persist new state, hand off to `GameOrchestrator.GetOrCreate(gameId, ...)`.

**Race conditions:** two users may both call `JoinAsync` for the last seat at the same time. Solution: optimistic concurrency token (a `RowVersion`/`Xmin` column on `Games`) + retry on `DbUpdateConcurrencyException`. The `IGameRepository.UpdateAsync` returns `bool` indicating whether the update applied; on `false`, refetch and retry up to 3 times before giving up.

**`LeaveAsync`:** allowed only when `Status == Open` (returns 409-equivalent for `Running` games — the Application returns a typed `LobbyConflictException`; the API maps to HTTP 409).

**Mid-game quit:** there is no "leave a running game" REST endpoint in v1. A player who wants to abandon a `Running` game closes the tab / disconnects; the standard disconnect-grace-then-forfeit path applies. This is documented in README's Disconnect / reconnect handling section.

**`OpenLobbyTtl` background service:** a `BackgroundService` in `Briscola.Application/Background/OpenLobbyJanitor.cs` runs every minute; transitions any `Open` game older than `OpenLobbyTtlMinutes` to `Abandoned`.

---

### Step 2.7 — `RankingService` [S] [x]

**Where:** `backend/src/Briscola.Application/Ranking/RankingService.cs`

**Algorithm — Elo, K=24:**

```
expected(rA, rB) = 1 / (1 + 10 ** ((rB - rA) / 400))
new_rA = rA + K * (score - expected(rA, rB))
score: 1 = win, 0.5 = draw, 0 = loss
```

**For 2p:** straightforward; both players' ratings updated.

**For 4p teams:** average each team's rating to compute expected score, then apply the same delta to each member of the team.

```
rA_avg = (rating[seat0] + rating[seat2]) / 2
rB_avg = (rating[seat1] + rating[seat3]) / 2
delta = K * (score_A - expected(rA_avg, rB_avg))
rating[seat0] += delta; rating[seat2] += delta
rating[seat1] -= delta; rating[seat3] -= delta
```

(Yes, the delta is symmetric — that follows from `expected(a,b) + expected(b,a) == 1`.)

**Forfeits** count as a loss for the forfeiting side — the math is identical, just `score = 0` for the forfeiter.

**Update statistics:** `Wins`, `Losses`, `Draws`, `GamesPlayed`, `UpdatedAt` (UTC).

**Idempotency:** `RankingService.ApplyResultAsync(gameRecord, result)` checks whether `result.GameId` has already been applied (via a `ProcessedGames` set in the `Rankings` schema, or a flag on `GameResults`). Re-application is a no-op.

---

### Step 2.8 — `MatchHistoryService` [S] [x]

Trivial wrapper that, on game end, persists a `GameResultRecord`. `GameMoves` is already persisted incrementally by the orchestrator. No additional logic.

---

### Step 2.9 — `Briscola.Application.Tests` [M] [x]

**Where:** `backend/tests/Briscola.Application.Tests/`

**Doubles:**
- `FakeClock : IClock` — settable `UtcNow`.
- `FakeRandomSource : IRandomSource` — fixed seed for reproducibility.
- `InMemoryGameRepository`, `InMemoryChatRepository`, `InMemoryRankingRepository` — `Dictionary`-backed.
- `FakeTimerService` — virtual time; `AdvanceAsync(TimeSpan)` triggers due callbacks.
- `RecordingGameEventBus` — captures events for assertions.

**Test classes:**

- `OrchestratorConcurrencyTests`:
  - **1000 interleaved commands** test: spawn 8 producer tasks, each enqueueing valid room commands on a single room; assert final state is consistent.
  - `Single_writer_invariant`: two `PlayCardCommand`s for the same seat in the same trick → only the first succeeds, second yields `InvalidMoveRejectedEvent`.
- `GameOrchestratorTests`:
  - Hydrate running games from persistence and route commands to the room.
  - Missing game and lazy factory behavior.
- `GameRoomEventTests`:
  - Trick-resolution, draw, final-result, view-own-pile, stale persistence, and forfeit event paths.
- `ReconnectTests`:
  - Happy path: disconnect → reconnect inside grace → state resumes; outstanding turn unaffected.
  - Idempotent reconnect: 5 consecutive `ReconnectCommand`s emit one `PlayerReconnectedEvent` and 5 `JoinedEvent` snapshots (snapshots are idempotent broadcasts).
  - Forfeit on deadline: `FakeClock` advanced past deadline → `GameFinishedEvent.Reason == ForfeitDisconnect`; opposing seat wins.
- `IdleTimeoutTests`:
  - Warn at `IdleWarnSeconds`, forfeit at `IdleForfeitSeconds`. Only the seat that's actually `NextToPlay` is forfeited; if a different seat is idle (impossible under correct play, but tested), nothing happens.
- `RankingServiceTests`:
  - Numerical: `applyResult({A:1500,B:1500}, win=A, K=24) == {A:1512, B:1488}`.
  - Draw: `{A:1500,B:1500}, draw == {A:1500,B:1500}`.
  - Asymmetric: `{A:1700,B:1500}, win=B, K=24 == {A:?, B:?}` — assert exact deltas computed by hand and pinned in the test.
  - 4p: same delta to both teammates; opposing team loses the same delta.
  - Idempotency: applying the same `gameId` twice yields the same final ratings.
- `LobbyServiceTests`:
  - Auto-start when seats fill.
  - Race: two simultaneous `JoinAsync` for the last seat — exactly one succeeds.
  - Cannot leave a `Running` game.
  - Private game requires correct password.
  - Open-lobby janitor abandons games > TTL.
- `EventBusTests`:
  - Single-reader FIFO semantics.
- `MatchHistoryServiceTests`:
  - Saves a `GameResultRecord` through `IGameRepository`.

**Acceptance:** all tests green; line coverage on `Briscola.Application` ≥ 85% (verified at 98.6%).

**Phase 2 exit:** application layer fully exercised in tests against in-memory fakes; no API/persistence code yet.

### Phase 2 follow-up items (deferred for later phases)

These came out of the post-implementation review; none block Phase 3, but they should be addressed before v1 ships:

- **Stale-record recovery.** `GameRoom.PersistStateAsync` throws `GameCommandException` when `IGameRepository.UpdateAsync` returns false (Version mismatch). The room state and `_record.Version` are NOT refreshed on failure, so subsequent commands on the same room keep failing — the room is effectively poisoned. Fix in Phase 3 or 5: on concurrency conflict, refetch the record, evict the room from `GameOrchestrator._rooms`, and surface a structured error to the caller so the API layer can ask the client to retry.
- **Hydrate orphan task.** `GameOrchestrator.HydrateAsync` calls `_rooms.TryAdd(record.Id, GameRoom.FromRecord(...))`. If `TryAdd` returns false (room already exists), the freshly-constructed `GameRoom` leaks: its `ProcessLoopAsync` task spins idle forever and its idle-check timer fires reconnect/idle commands into a dead channel. Memory leak, not crash. Fix: check `TryAdd` return value and `Dispose` the orphan (will require `GameRoom` to be `IDisposable`).
- **4p team Elo magnitude.** `RankingService.ApplyFourPlayerAsync` adds `+delta` to each of two teammates and `-delta` to each of two opponents. Net team rating change is `2*delta`. For K=24 this is plausible for a 4-player game (more variance than 1v1) but is worth re-examining when Phase 10 wires up real ranked matches: consider halving the delta for teams to keep per-team K constant, or document the choice as intentional.
- **Snapshot codec coverage.** `InMemoryGameStateCodec` is a dictionary-backed pass-through; it doesn't verify real JSON round-tripping. Phase 3 must add an EF-side codec implementation AND an integration test that round-trips a full `GameState` through it.

---

## Phase 3 — Infrastructure (EF Core, Identity, JWT)

**Goal:** persistence and auth wired up against SQLite (dev) and Postgres (prod).

### Step 3.1 — Project setup [S] [x]

**Where:** `backend/src/Briscola.Infrastructure/`

**Refs:** `Briscola.Application` (project). NuGet packages, all on the .NET 10 release line where they have one:

- `Microsoft.EntityFrameworkCore` (10.x), `Microsoft.EntityFrameworkCore.Sqlite` (10.x), `Microsoft.EntityFrameworkCore.Design` (10.x; PrivateAssets=all).
- `Npgsql.EntityFrameworkCore.PostgreSQL` (matching 10.x).
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore` (10.x).
- `Microsoft.AspNetCore.Authentication.JwtBearer` (10.x).
- `Serilog` + `Serilog.AspNetCore` + `Serilog.Sinks.Console` + `Serilog.Sinks.File` (latest stable).
- `Serilog.Formatting.Compact` (for prod JSON output).
- `BCrypt.Net-Next` (for game-room passwords — distinct from ASP.NET Identity's user-password hashing).

If a 10.x version isn't on NuGet at implementation time, pin to the highest stable that targets `net10.0` and add a note to a new `backend/README.md` (we should create one when version pins start accumulating).

---

### Step 3.2 — `BriscolaDbContext` & entity types [M] [x]

**Where:**
- `backend/src/Briscola.Infrastructure/Persistence/BriscolaDbContext.cs`
- `backend/src/Briscola.Infrastructure/Persistence/Entities/*.cs`
- `backend/src/Briscola.Infrastructure/Persistence/Configurations/*.cs`

**Entities (one file each):**

- `ApplicationUser : IdentityUser<Guid>` — `DisplayName` (required, 1..32), `ActiveCardSetId` (string, default `"placeholder"`), `CreatedAt` (UTC).
- `RefreshTokenEntity` — `Id`, `UserId`, `TokenHash` (SHA-256 hex), `ExpiresAt`, `RevokedAt?`, `ReplacedByTokenId?`.
- `GameEntity` — mirrors `GameRecord` from `Briscola.Application.Persistence`, including:
  - `Id`, `Mode`, `Name`, `Status`, `CreatedByUserId`, `CreatedAt`, `StartedAt?`, `EndedAt?`,
  - `ShuffleSeed` (`long`), `StateSnapshotJson` (`text` on SQLite / `jsonb` on Postgres), `BriscolaSuit`, `IsPrivate`, `PasswordHash?`,
  - `Version` (`long`, application-managed optimistic-concurrency token — see below).
  - **No** `RowVersion`/`xmin`. The application contract talks in `long Version`; mapping it to a provider-specific concurrency token would force the application port to learn about `byte[]`.
- `GameSeatEntity` — composite key `(GameId, SeatIndex)`, plus `UserId?`, `JoinedAt`, `LeftAt?`. The `EfGameRepository` projects `GameSeats` rows into `GameRecord.SeatUserIds: ImmutableArray<Guid?>` on read and writes them back as a set on update.
- `GameMoveEntity` — `Id` (PK), `GameId`, `MoveIndex`, `SeatIndex`, `Type`, `PayloadJson`, `CreatedAt`. Unique `(GameId, MoveIndex)`. The repository's `GetNextMoveIndexAsync(gameId)` returns `MAX(MoveIndex) + 1` (or 0 if empty).
- `GameResultEntity` — `GameId` (PK), `Kind`, `WinnerKey?`, `SeatScoresJson`, `TeamScoresJson?`, `Reason`.
- `ChatMessageEntity` — `Id`, `Scope`, `GameId?`, `UserId`, `Text` (`varchar(500)`), `CreatedAt`.
- `RankingEntity` — `UserId` (PK, FK Users), `Elo`, `Wins`, `Losses`, `Draws`, `GamesPlayed`, `UpdatedAt`.
- `RankingProcessedGameEntity` — `GameId` (PK). Single-row marker that drives `IRankingRepository.HasProcessedGameAsync` / `MarkProcessedGameAsync`. Required for Elo idempotency per Phase 2's `RankingService`.

**Configurations** in `IEntityTypeConfiguration<T>` classes (NOT inline in `OnModelCreating` — keeps the DbContext thin):

- All `string` columns get explicit `HasMaxLength`.
- All `DateTimeOffset` columns: SQLite uses `HasConversion<DateTimeOffsetToBinaryConverter>`; Postgres uses raw `timestamptz`.
- `GameEntity.StateSnapshotJson`: `text` on SQLite, `jsonb` on Postgres.
- Indexes per the README schema section.
- `GameEntity.Version`: plain `long`, explicitly NOT `IsRowVersion()`. `EfGameRepository.UpdateAsync` does the read-modify-write and increments the column inside a transaction; returns `false` on mismatch.
- `GameMoveEntity` unique index on `(GameId, MoveIndex)` is load-bearing — it's how we detect bugs in the move-log hydration path.

**Provider switching:**

```csharp
// In Briscola.Api/Program.cs (Phase 4)
var provider = builder.Configuration["ConnectionStrings:Provider"] ?? "Sqlite";
builder.Services.AddDbContext<BriscolaDbContext>((sp, opts) =>
{
    var cs = builder.Configuration.GetConnectionString("Default");
    if (provider == "Sqlite") opts.UseSqlite(cs, b => b.MigrationsAssembly("Briscola.Infrastructure.Sqlite.Migrations"));
    else if (provider == "Postgres") opts.UseNpgsql(cs, b => b.MigrationsAssembly("Briscola.Infrastructure.Postgres.Migrations"));
    else throw new InvalidOperationException($"Unknown provider: {provider}");
});
```

---

### Step 3.3 — Migration assemblies [M] [x]

**Where:**
- `backend/src/Briscola.Infrastructure.Sqlite.Migrations/` (csproj refs `Briscola.Infrastructure`)
- `backend/src/Briscola.Infrastructure.Postgres.Migrations/`

**How:**
- Each assembly is a class library with `Microsoft.EntityFrameworkCore.Design` and the relevant provider package.
- Generate the initial migration twice:
  ```bash
  dotnet ef migrations add Initial --project src/Briscola.Infrastructure.Sqlite.Migrations --startup-project src/Briscola.Api -- --provider=Sqlite
  dotnet ef migrations add Initial --project src/Briscola.Infrastructure.Postgres.Migrations --startup-project src/Briscola.Api -- --provider=Postgres
  ```
- The startup project reads a `--provider` argument (or env var `EF_PROVIDER`) to select which DbContext to use.

**Schema-parity test:** `backend/tests/Briscola.Api.IntegrationTests/SchemaParityTests.cs` boots both providers, runs migrations, and compares the EF model snapshot. Discrepancies (different column types beyond the mapped equivalents, missing indexes) fail the test.

---

### Step 3.4 — ASP.NET Identity wiring [M] [x]

- `IdentityCore<ApplicationUser>` with `AddEntityFrameworkStores<BriscolaDbContext>()`.
- Password options: `RequireDigit=true`, `RequiredLength=10`, `RequireNonAlphanumeric=false`, `RequireUppercase=false`, `RequireLowercase=false`. (Length over complexity.)
- Username options: `AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-"`, `RequireUniqueEmail=true`.
- `UserManager<ApplicationUser>` available via DI.

---

### Step 3.5 — JWT issuance & refresh tokens [M] [x]

**Where:** `backend/src/Briscola.Infrastructure/Auth/`

- `JwtOptions` bound from config: `Issuer`, `Audience`, `SigningKey` (≥ 32 bytes, base64 in config), `AccessTokenLifetimeMinutes` (15), `RefreshTokenLifetimeDays` (14).
- `JwtIssuer` service: `CreateAccessToken(ApplicationUser)`, `CreateRefreshToken(userId) -> (token, hash, expiresAt)`.
- `RefreshTokenService`:
  - `RotateAsync(currentRefreshToken) -> (newAccess, newRefresh)`. Atomic in a transaction: mark old token revoked with `ReplacedByTokenId = newId`.
  - Revoke chain on detected reuse (if a revoked token is re-presented, revoke the entire chain — defense against token theft).
- Access token claims: `sub` (user id), `name` (username), `display_name`, `card_set` (active id), `iat`, `exp`, `iss`, `aud`, `security_stamp`. The validator (in `Briscola.Api`) re-checks `security_stamp` against the user's current stamp on every request — password change rotates the stamp, invalidating outstanding access tokens.

---

### Step 3.6 — Repositories and adapter ports (Infrastructure implementations) [M]

**Where:**
- `backend/src/Briscola.Infrastructure/Persistence/Repositories/Ef*Repository.cs`
- `backend/src/Briscola.Infrastructure/Codecs/JsonGameStateCodec.cs`
- `backend/src/Briscola.Infrastructure/Auth/BCryptGamePasswordHasher.cs`

**Repositories implementing `Briscola.Application.Ports.*`:**

- `EfGameRepository : IGameRepository` — implements all 7 methods including `GetNextMoveIndexAsync(gameId)` (a single `MAX(MoveIndex)` query). Reads `GameSeats` rows and projects into `GameRecord.SeatUserIds`. Writes seat changes as a set-based update inside the same transaction as the parent `Games` row update.
  - `UpdateAsync` reads the current `Version`, compares to the incoming record, writes `record with { Version = current.Version + 1 }` only if matched, returns `false` on mismatch. Surfaces `DbUpdateConcurrencyException` as `ConcurrencyConflictException`. Orchestrator and lobby handlers already retry up to 3 times.
- `EfChatRepository : IChatRepository`.
- `EfRankingRepository : IRankingRepository` — `HasProcessedGameAsync` / `MarkProcessedGameAsync` operate on `RankingProcessedGameEntity` (single-row dedup). `GetAsync` returns the existing ranking or auto-creates one at default Elo (mirrors `InMemoryRankingRepository` from Phase 2 tests).

**Adapter ports also implemented here** (kept out of Application by design):

- `JsonGameStateCodec : IGameStateCodec` — `System.Text.Json` round-trip of `GameState` with `JsonStringEnumConverter` and `ImmutableArrayJsonConverter` (custom — `ImmutableArray<T>` doesn't round-trip out of the box; needed because `GameState.Hands` and `Pozzi` are `ImmutableArray<ImmutableArray<Card>>`).
- `BCryptGamePasswordHasher : IGamePasswordHasher` — wraps `BCrypt.Net-Next`. NOT shared with ASP.NET Identity's user-password hashing; lobby-game passwords are a separate trust boundary.

**Tests** under `Briscola.Api.IntegrationTests/Persistence/`:

- `JsonGameStateCodecTests` — round-trip a fully populated `GameState` (including 4p with non-empty pozzi and a captured `GameOutcome.Winner`) and assert deep equality field-by-field. This closes the Phase 2 follow-up "snapshot codec coverage" item — `InMemoryGameStateCodec` was a pass-through.
- `EfGameRepositoryConcurrencyTests` — two concurrent `UpdateAsync` calls on the same `Version`: exactly one succeeds, the other gets `false`. Run against Testcontainers Postgres.
- `EfGameRepositoryMoveIndexTests` — append moves at indices 0, 1, 2, then call `GetNextMoveIndexAsync` and assert it returns 3.
- `EfRankingRepositoryIdempotencyTests` — `HasProcessedGameAsync` returns false initially, true after `MarkProcessedGameAsync`.

---

### Step 3.7 — Serilog config [S]

**Where:** `backend/src/Briscola.Infrastructure/Logging/SerilogSetup.cs`

- Dev: console (compact) + rolling file `logs/briscola-.log` (1-day rolling, 14-day retention, 100 MB cap).
- Prod: console (JSON, `Serilog.Formatting.Compact.CompactJsonFormatter`) only.
- Enrichers: `WithMachineName`, `WithEnvironmentName`, `WithThreadId`, `WithCorrelationIdHeader` (custom enricher reading `X-Correlation-Id` from the current `HttpContext`).
- `Microsoft.AspNetCore` set to `Warning`; `Microsoft.EntityFrameworkCore.Database.Command` to `Information` in dev only.

---

### Step 3.8 — Integration smoke test [M]

**Where:** `backend/tests/Briscola.Api.IntegrationTests/AuthSmokeTests.cs`

Uses `WebApplicationFactory<Program>` with a test config overriding `ConnectionStrings:Provider=Postgres` and pointing at a Testcontainers Postgres instance (`Testcontainers.PostgreSql` NuGet, started in a class fixture).

**Tests:**
- Register a user → 201.
- Login with that user → 200 + tokens.
- Refresh → 200 + new tokens; old refresh token rejected on second use.
- Change password → old access token still valid for ≤ 15 min? **No** — `security_stamp` validation must reject the old access token immediately. Assert that.
- Hit a `[Authorize]` echo endpoint with the new access → 200.

**Acceptance:** all green; integration suite runs in < 60 s on CI.

**Phase 3 exit:** can register and authenticate against both providers; migrations run cleanly on both; schema-parity test green.

---

## Phase 4 — REST API

**Goal:** REST surface complete enough to drive the lobby end-to-end via curl.

### Step 4.1 — `Briscola.Api` host [S]

**Where:** `backend/src/Briscola.Api/Program.cs`, plus `appsettings.json` and `appsettings.Development.json`.

**Config files** (committed; **NO secrets**):
- `appsettings.json` — production-safe defaults. `ConnectionStrings.Provider = "Sqlite"` (overridden in compose), no signing key. Logging defaults `Information`.
- `appsettings.Development.json` — `Migrations.RunOnStartup = true`, more verbose logging, dev SQLite path `Data Source=./briscola-dev.db`.
- All secrets (`Authentication__Jwt__SigningKey`, DB password) come from environment variables only — never appsettings.

**Order of operations in `Program.cs`** (precise):

1. `var builder = WebApplication.CreateBuilder(args);`
2. Bind options: `builder.Services.Configure<GameOptions>(builder.Configuration.GetSection(GameOptions.SectionName));` plus `JwtOptions`, `CorsOptions`.
3. Serilog: `builder.Host.UseSerilog((ctx, lc) => SerilogSetup.Configure(lc, ctx.Configuration));`.
4. `AddDbContext<BriscolaDbContext>` per provider.
5. `AddIdentityCore<ApplicationUser>...`.
6. `AddAuthentication(JwtBearerDefaults).AddJwtBearer(opts => { ... })` with `OnMessageReceived` to read `?access_token=` query for SignalR endpoints (path starts with `/hubs`).
7. `AddAuthorization`.
8. `AddRateLimiter` with the policies described in step 4.7.
9. `AddCors` with the named policy.
10. `AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));`.
11. `AddSignalR()` (hubs registered in Phase 5).
12. `AddSwaggerGen` (dev only, conditional).
13. Application-layer service registrations via `services.AddBriscolaApplication()` (the extension defined in `Briscola.Application/DependencyInjection.cs` — already present from Phase 2). It registers:
    - Singletons: `IClock` (`SystemClock`), `IGameEventBus` (`InMemoryGameEventBus`), `ITimerService` (`SystemTimerService`), `IRandomSourceFactory` (`SystemRandomSourceFactory`), `IBriscolaEngine` (`BriscolaEngine`), `GameOrchestrator`.
    - Scopes: `LobbyService`, `RankingService`, `MatchHistoryService`.
    - Hosted: `OpenLobbyJanitor`, `GameEventDispatcher` (added in Phase 5 — fans `IGameEventBus` events out to SignalR).
14. Infrastructure registrations: `services.AddBriscolaInfrastructure(configuration)` registers EF repositories (`EfGameRepository`, `EfChatRepository`, `EfRankingRepository`), `IGameStateCodec` (`JsonGameStateCodec`), `IGamePasswordHasher` (`BCryptGamePasswordHasher`), JWT issuer / refresh-token service, and any options the infrastructure exposes.
15. Build the app.
16. Middleware order: `UseSerilogRequestLogging` → `UseExceptionHandler("/error")` → `UseSecurityHeaders()` → `UseHsts()` (prod) → `UseHttpsRedirection()` (prod) → `UseStaticFiles()` (for `wwwroot/card-sets/`) → `UseRouting` → `UseCors` → `UseRateLimiter` → `UseAuthentication` → `UseAuthorization` → `MapControllers` → `MapHubs` → `MapHealthChecks`.
17. **Migrations on startup** behind `Migrations:RunOnStartup=true` flag (default false in prod); for dev/SQLite it's true. After migrations, call `await orchestrator.HydrateAsync(ct)` to load any games left in `Status = Running` from a previous process restart.
18. `app.Run();`

The class is partial-class friendly: `public partial class Program {}` for `WebApplicationFactory<Program>` to find it.

---

### Step 4.2 — Versioning & route prefix [S]

- All controllers attribute-routed under `[Route("api/v1/[controller]")]`.
- `[ApiController]` everywhere.
- `[Produces("application/json")]` globally.
- API version 1 is hardcoded; we do not use `Microsoft.AspNetCore.Mvc.Versioning` for v1.

---

### Step 4.3 — DTOs [S]

**Where:** `backend/src/Briscola.Api/Dtos/`

For every endpoint a request DTO and a response DTO. Examples:

- `RegisterRequest { string Username, string Email, string Password, string? DisplayName }`
- `LoginRequest { string UsernameOrEmail, string Password }`
- `TokenResponse { string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt }`
- `MeResponse { Guid Id, string Username, string DisplayName, string ActiveCardSetId, RankingDto Ranking }`
- `MePatchRequest { string? DisplayName, string? ActiveCardSetId }`
- `CreateGameRequest { GameMode Mode, string Name, bool IsPrivate, string? Password }`
- `JoinGameRequest { string? Password }`
- `GameSummary` — application-layer record produced by `LobbyService.ListAsync`. Real shape (from Phase 2): `Guid Id, GameMode Mode, string Name, GameStatus Status, int OccupiedSeats, int TotalSeats, bool IsPrivate, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt`. The REST controller may choose to enrich it with `string CreatedByDisplayName` (a join against `Users.DisplayName`) for UI display — that's a controller-layer concern, not an application-port one.
- `GameDetail` (extends `GameSummary` with seat list and, if running and the caller is a participant, an authoritative `RedactedStateForUser`).

Enums serialized as strings (`JsonStringEnumConverter`).

---

### Step 4.4 — Controllers [S each, M for Games]

**`AuthController`** at `api/v1/auth`:
- `POST register` (anonymous, rate-limited).
- `POST login` (anonymous).
- `POST refresh` (anonymous; binds refresh token from body or `HttpOnly` cookie — body for v1).
- `POST logout` (authenticated; revokes the refresh token whose hash matches body).
- `POST change-password` (authenticated; calls `UserManager.ChangePasswordAsync`, which rotates `SecurityStamp`).

**`MeController`** at `api/v1/me`:
- `GET /` (authenticated).
- `PATCH /` (authenticated).
- `GET /history?page=1&size=20` (authenticated).
- `GET /ranking` (authenticated).

**`CardSetsController`** at `api/v1/card-sets`:
- `GET /` (anonymous): returns the manifest list scanned at startup. Manifests are cached in a singleton `CardSetCatalog` populated from `wwwroot/card-sets/*/manifest.json` at app start.

**`GamesController`** at `api/v1/games`:
- `GET /?status=open|running` (authenticated).
- `POST /` (authenticated).
- `POST /{id}/join` (authenticated).
- `POST /{id}/leave` (authenticated; 409 if status is not `Open`).
- `GET /{id}` (authenticated).
- `POST /{id}/spectate` (authenticated; 409 if status is not `Running`).

**`HealthController`** at `/healthz`, `/readyz` (anonymous, separate route prefix).

**Error handling:** every controller defers to a `ProblemDetailsFactory` configured with stable error codes. `InvalidMoveException` → 400 `type: https://briscola.example/errors/invalid-move`, `extensions: { code: "NotYourTurn" }`. `ConcurrencyConflictException` → 409. `ValidationException` (FluentValidation) → 422.

---

### Step 4.5 — FluentValidation [S]

- `RegisterRequestValidator`, `LoginRequestValidator`, etc. — one per DTO.
- Username regex `^[a-zA-Z0-9_-]{3,32}$`; password length ≥ 10, must contain ≥ 1 letter and ≥ 1 digit.
- Wired via `AddFluentValidationAutoValidation()`.

---

### Step 4.6 — CORS, security headers [S]

- `CorsOptions { string[] AllowedOrigins }`. The named policy allows those origins, methods `GET POST PATCH DELETE OPTIONS`, headers `Authorization, Content-Type, X-Correlation-Id`, credentials enabled.
- `SecurityHeadersMiddleware` writes:
  - `Strict-Transport-Security: max-age=31536000; includeSubDomains; preload` (prod only).
  - `X-Content-Type-Options: nosniff`.
  - `Referrer-Policy: strict-origin-when-cross-origin`.
  - `Permissions-Policy: ()`.
  - `Content-Security-Policy: default-src 'self'; img-src 'self' data:; connect-src 'self' wss:; script-src 'self'; style-src 'self' 'unsafe-inline'` (the `'unsafe-inline'` for styles is unfortunate but Angular emits inline styles; revisit in v2 with hashed CSP).

---

### Step 4.7 — Rate limiting [S]

Use `Microsoft.AspNetCore.RateLimiting` (built into ASP.NET Core since .NET 7; we're on .NET 10).

```csharp
builder.Services.AddRateLimiter(opts =>
{
    opts.RejectionStatusCode = 429;
    opts.AddPolicy("auth-login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
    // similar for auth-register (3/hour per IP), auth-refresh (30/min per user), ...
});
```

Apply per-action via `[EnableRateLimiting("auth-login")]`.

---

### Step 4.8 — Angular dev proxy [S]

**Where:** `frontend/proxy.conf.json`

```json
{
  "/api":        { "target": "http://localhost:5080", "secure": false, "changeOrigin": true },
  "/hubs":       { "target": "http://localhost:5080", "secure": false, "ws": true, "changeOrigin": true },
  "/card-sets":  { "target": "http://localhost:5080", "secure": false, "changeOrigin": true }
}
```

Wire in `angular.json` under `serve.options.proxyConfig`.

---

### Step 4.9 — `Briscola.Api.IntegrationTests` (REST slice) [M]

**Tests:**

- `AuthFlowTests` — register, login, refresh, change-password invalidates old access token, logout revokes refresh, replayed refresh detects reuse.
- `LobbyControllerTests` — create open game, list it, join from second account, auto-start when 2p fills, cannot leave running, cannot join started.
- `PrivateGameTests` — wrong password rejected, right password accepted.
- `SecurityHeadersTests` — every response carries the headers above.
- `RateLimitTests` — 6th login in a minute returns 429.
- `ValidationTests` — short password / invalid username → 422 with stable error code.

**Acceptance:** all green; coverage on `Briscola.Api` controllers ≥ 80%.

**Phase 4 exit:** REST contract is fully usable from `curl`/Postman; integration tests green.

---

## Phase 5 — SignalR hubs

**Goal:** real-time multiplayer fully working server-side.

### Step 5.1 — `LobbyHub` [S]

**Where:** `backend/src/Briscola.Api/Hubs/LobbyHub.cs`

```csharp
[Authorize]
public sealed class LobbyHub : Hub<ILobbyClient>
{
    public Task SubscribeOpen() => Groups.AddToGroupAsync(Context.ConnectionId, "lobby:open");
    public Task UnsubscribeOpen() => Groups.RemoveFromGroupAsync(Context.ConnectionId, "lobby:open");
    public async Task SendChat(string text) { /* validate, persist, broadcast */ }
}

public interface ILobbyClient
{
    Task GameCreated(GameSummaryDto summary);
    Task GameUpdated(GameSummaryDto summary);
    Task GameStarted(Guid gameId);
    Task GameEnded(Guid gameId);
    Task ChatMessage(LobbyChatMessageDto msg);
}
```

`LobbyService` (or a thin event-fanout service) calls `IHubContext<LobbyHub, ILobbyClient>` to broadcast.

---

### Step 5.2 — `GameHub` [M]

```csharp
[Authorize]
public sealed class GameHub : Hub<IGameClient>
{
    public Task JoinGame(Guid gameId) { /* enqueue JoinGame/Reconnect, add to group game:{gameId} */ }
    public Task PlayCard(Guid gameId, Card card) { /* enqueue PlayCardCommand */ }
    public Task ViewOwnPile(Guid gameId) { /* enqueue ViewOwnPileCommand */ }
    public Task SendChat(Guid gameId, string text) { /* validate scope/spectator, persist, broadcast */ }
    public Task LeaveGame(Guid gameId) {
        // For Open games: removes the seat (delegates to LobbyService.LeaveAsync).
        // For Running games: behaves as a disconnect (enqueues DisconnectCommand;
        //   the grace timer is started; client tab closure has identical semantics).
    }

    public override async Task OnDisconnectedAsync(Exception? ex)
    {
        // For each game this connection participated in, enqueue DisconnectCommand.
    }
}

public interface IGameClient
{
    Task Joined(RedactedStateForUserDto snapshot);
    Task StateUpdated(RedactedStateForUserDto snapshot);
    Task CardPlayed(CardPlayedDto evt);
    Task TrickResolved(TrickResolvedDto evt);
    Task CardsDrawn(CardsDrawnDto evt);
    Task PhaseChanged(string newPhase);
    Task GameFinished(GameFinishedDto evt);
    Task PlayerDisconnected(int seatIndex, DateTimeOffset graceDeadlineUtc);
    Task PlayerReconnected(int seatIndex);
    Task ChatMessage(GameChatMessageDto msg);
    Task InvalidMove(string code);
    Task PileSnapshot(int seatIndex, IReadOnlyList<Card> cards);
}
```

---

### Step 5.2b — `GameEventDispatcher` hosted service [M]

**Where:** `backend/src/Briscola.Api/Hubs/GameEventDispatcher.cs`

The bridge between the application layer's `IGameEventBus` and SignalR. **The only code that calls `IHubContext<GameHub, IGameClient>` for game events.** Keeping all SignalR concerns out of `GameOrchestrator` / `GameRoom` is what lets the application layer stay framework-agnostic.

**Shape:**

```csharp
public sealed class GameEventDispatcher(
    IGameEventBus bus,
    IHubContext<GameHub, IGameClient> hub,
    ILogger<GameEventDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (IGameEvent evt in bus.ReadAllAsync(ct).WithCancellation(ct))
        {
            try { await DispatchAsync(evt, ct); }
            catch (Exception ex) { logger.LogError(ex, "Dispatch failed for {Event}", evt.GetType().Name); }
        }
    }

    private Task DispatchAsync(IGameEvent evt, CancellationToken ct) => evt switch
    {
        JoinedEvent j           => hub.Clients.User(j.TargetUserId.ToString()).Joined(ToDto(j.Snapshot)),
        StateUpdatedEvent s     => hub.Clients.User(s.TargetUserId.ToString()).StateUpdated(ToDto(s.Snapshot)),
        CardPlayedEvent c       => hub.Clients.Group(GameGroup(c.GameId)).CardPlayed(ToDto(c)),
        TrickResolvedEvent t    => hub.Clients.Group(GameGroup(t.GameId)).TrickResolved(ToDto(t)),
        CardsDrawnEvent d       => DispatchDrawAsync(d, ct),
        PhaseChangedEvent p     => hub.Clients.Group(GameGroup(p.GameId)).PhaseChanged(p.NewPhase.ToString()),
        GameFinishedEvent f     => hub.Clients.Group(GameGroup(f.GameId)).GameFinished(ToDto(f)),
        PlayerDisconnectedEvent pd => hub.Clients.Group(GameGroup(pd.GameId)).PlayerDisconnected(pd.SeatIndex, pd.GraceDeadlineUtc),
        PlayerReconnectedEvent pr  => hub.Clients.Group(GameGroup(pr.GameId)).PlayerReconnected(pr.SeatIndex),
        ChatMessageEvent ch     => hub.Clients.Group(GameGroup(ch.GameId)).ChatMessage(ToDto(ch)),
        InvalidMoveRejectedEvent im => hub.Clients.User(im.TargetUserId.ToString()).InvalidMove(im.Code.ToString()),
        IdleWarningEvent iw     => hub.Clients.Group(GameGroup(iw.GameId)).IdleWarning(iw.SeatIndex, iw.ForfeitDeadlineUtc),
        _                       => Task.CompletedTask,
    };

    private static string GameGroup(Guid id) => $"game:{id}";
}
```

**Notes:**

- Spectators join `game:{id}:spectators`. For broadcast events that carry hand-derived data (e.g. `StateUpdatedEvent` carries a `RedactedStateForUser` that already has hand counts only for non-targets — that's fine), spectator-targeted variants are produced by the room when needed (see Step 5.3). The dispatcher routes; it does not redact.
- `CardsDrawnEvent.TargetUserId` is non-null for the per-recipient draw notification (only that user sees the actual `DrawnCard`). Broadcast a count-only variant to everyone else in the same group. Implementation detail of `DispatchDrawAsync` — write a unit test.
- The dispatcher catches per-event exceptions and logs them. It does NOT die on a single bad event; the bus is a long-running stream.
- Registered in DI as `services.AddHostedService<GameEventDispatcher>()` from `Briscola.Api`'s `Program.cs` (Step 4.1 is already updated to know about this).

**Tests** in `Briscola.Api.IntegrationTests/Hubs/GameEventDispatcherTests.cs`:

- Publishing a targeted event sends to exactly one connection.
- Publishing a broadcast event sends to every connection in the group, including spectators (with their redacted variant if applicable).
- An event whose handler throws is logged and the dispatcher keeps consuming.
- `CardsDrawnEvent` with a non-null `DrawnCard` reaches only the `TargetUserId`'s connection; the broadcast variant carries a null `DrawnCard` for everyone else.

---

### Step 5.3 — Per-recipient redaction [S]

The dispatcher knows each event's `TargetUserId` (or null for broadcast). For broadcast events that contain hand information, the dispatcher loops over the room's seats and constructs **per-seat redacted snapshots** (using the orchestrator's `BuildSnapshotForUser(state, userId)` helper).

**Spectators** join the group `game:{id}:spectators` and receive only spectator-redacted snapshots (counts only for everyone).

---

### Step 5.4 — `viewOwnPile` semantics [S]

- Hub method enqueues a `ViewOwnPileCommand`.
- The room validates `Phase == LastHand`. If not, emits `InvalidMoveRejectedEvent(code: PileViewNotAllowed)` targeted at the caller.
- If allowed, the room emits a `PileSnapshotEvent(userId, cards)` consumed by the dispatcher and sent only to that user.

---

### Step 5.5 — Spectator chat policy [S]

`GameHub.SendChat`:
- If caller is in `game:{id}:spectators` group **only** (not a player), reject with an `InvalidMove("SpectatorsCannotChat")` to that connection. Do not throw — just no-op + targeted error.

---

### Step 5.6 — Hub-method rate limits [S]

Use a per-connection sliding window kept in `ConnectionItems`:

- `playCard`: 1 per second; excess → `InvalidMove("RateLimited")` to the caller.
- `sendChat`: 5 per 10 seconds; excess → same.

(Note: ASP.NET's `RateLimiter` middleware is HTTP-only; for hub methods we implement a small in-memory limiter ourselves. Keep it within `Briscola.Api/Hubs/Limits/`.)

---

### Step 5.7 — Hub integration tests [M]

**Where:** `backend/tests/Briscola.Api.IntegrationTests/Hubs/`

**Setup helper:** `HubTestHarness` boots `WebApplicationFactory<Program>`, registers two test users, returns two `HubConnection`s authenticated as those users.

**Tests:**

- `TwoPlayerGame_RunsToCompletion`: drive a deterministic game by feeding both clients a fixed move list (precomputed against a fixed seed). Assert the final `GameFinishedDto`.
- `Disconnect_within_grace_resumes`: client A disconnects, reconnects within 5 s; assert state restored.
- `Disconnect_past_deadline_forfeits`: advance the test clock; assert `GameFinishedDto.Reason == ForfeitDisconnect` and B wins.
- `Idle_timeout_forfeits_idle_seat`: same idea via clock advancement.
- `Spectator_redacted_state`: spectator client receives no `MyHand`, `HandCountsBySeat` only.
- `Spectator_cannot_chat`: `SendChat` from a spectator → `InvalidMove("SpectatorsCannotChat")`.
- `Cheating_attempt_rejected`: A asks to play a card that's in B's hand → `InvalidMove("CardNotInHand")`.
- `ViewOwnPile_outside_LastHand_rejected`.
- `ViewOwnPile_inside_LastHand_returns_pile`.

**Acceptance:** all green; total hub-test runtime < 90 s.

**Phase 5 exit:** automated multiplayer tests green; orchestrator survives concurrent commands across hub connections.

---

## Phase 6 — Angular foundation

**Goal:** SPA shell with auth, routing, and a working SignalR client wrapper.

### Step 6.1 — Workspace polish [S]

- Strict TS (`"strict": true, "noUncheckedIndexedAccess": true, "noImplicitReturns": true`).
- `tsconfig.json` paths: `@app/*`, `@core/*`, `@features/*`, `@shared/*`.
- ESLint: extend `@angular-eslint/recommended` + `@typescript-eslint/strict`.
- Prettier: `printWidth: 100, singleQuote: true, trailingComma: 'all'`.

---

### Step 6.2 — Core module: HTTP, auth, SignalR [S]

**Where:** `frontend/src/app/core/`

- `auth.service.ts` — signals: `currentUser`, `isAuthenticated`. Methods: `login`, `register`, `logout`, `refresh`, `changePassword`. Stores tokens in memory + refresh token in `localStorage` (acceptable trade-off for a hobby-tier game; XSS risk is the well-known caveat — documented in `docs/security.md`).
- `http-token.interceptor.ts` — attaches `Authorization: Bearer …`; on 401 with a still-valid refresh, calls `refresh` and retries the request once.
- `correlation-id.interceptor.ts` — generates `crypto.randomUUID()` per request, sets `X-Correlation-Id`.
- `signalr-client.ts`:
  ```ts
  export function createHubConnection(path: string, getAccessToken: () => Promise<string>): HubConnection {
    return new HubConnectionBuilder()
      .withUrl(path, { accessTokenFactory: getAccessToken })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Information)
      .build();
  }
  ```
- `route-guards.ts` — `authGuard`, `guestGuard`.
- `error-toast.service.ts` — exposed via signal, consumed by a root toast component.

---

### Step 6.3 — Auth pages [S]

**Where:** `frontend/src/app/features/auth/`

- `login.component.ts` — reactive form, calls `AuthService.login`, navigates to `/lobby` on success.
- `register.component.ts` — reactive form, validation mirrors backend rules.
- Tests: form validation, submit-disabled-while-pending, error message rendering.

---

### Step 6.4 — i18n scaffolding [S]

- `frontend/src/assets/i18n/en.json` (populated).
- `frontend/src/assets/i18n/it.json` (skeleton with same keys, English fallback values).
- Tiny runtime: `I18nService { current = signal<'en'|'it'>('en'); t(key: string): string }`. Component-friendly `<span>{{ t('lobby.title') }}</span>` pattern.

**Phase 6 exit:** can register, log in, see a placeholder authenticated `/home` page. Lint, build, test all green.

---

## Phase 7 — Lobby UI

### Step 7.1 — `LobbyService` [S]

**Where:** `frontend/src/app/features/lobby/lobby.service.ts`

- Signals: `openGames: Signal<GameSummary[]>`, `runningGames: Signal<GameSummary[]>`.
- On bootstrap: fetch via REST, then connect to `LobbyHub` and call `subscribeOpen()`. Apply server pushes to the signals.
- Methods: `createGame`, `joinGame`, `leaveGame`.

### Step 7.2 — `LobbyPage` [S]

- Shows two lists (Open / Running). Each row: mode chip, name, players, created-by, created-at (relative).
- "Create" button opens a dialog (`CreateGameDialog`) — fields: mode, name, isPrivate (toggle), password (visible only when private).
- "Join" button → POSTs join, then routes to `/game/:id`.

### Step 7.3 — Lobby chat panel [S]

- Sidebar; reads `chatMessage` events from `LobbyHub`; sends via `SendChat`.

### Step 7.4 — Component tests [S]

- Filter logic.
- CreateGameDialog form validation.
- Join navigates on success.

**Phase 7 exit:** two browsers can create + join the same 2p game and arrive at `/game/:id`.

---

## Phase 8 — Game table UI

### Step 8.1 — `GameService` [M]

**Where:** `frontend/src/app/features/game/game.service.ts`

- Signals:
  - `state: Signal<RedactedStateForUserDto | null>`
  - `myHand: Signal<Card[]>` (derived)
  - `legalMoves: Signal<Set<string>>` (derived; in v1, this is just "any card in hand on my turn")
  - `disconnectDeadline: Signal<Date | null>`
  - `chatLog: Signal<GameChatMessage[]>`
- Methods: `connect(gameId)`, `disconnect()`, `play(card)`, `viewPile()`, `sendChat(text)`.
- Reconnect logic uses `withAutomaticReconnect` from SignalR; on `onreconnected`, re-call `JoinGame` to fetch fresh state.

### Step 8.2 — `GameTablePage` layout [M]

- Two layouts via CSS Grid:
  - **2p:** opponent at top, player at bottom, briscola/stock at center-left, trick area center.
  - **4p:** opponents arranged top-left/top-right/right; player bottom; partner cues highlighted.
- Responsive: collapses to a column layout < 720 px.

### Step 8.3 — Components [M]

Each component is standalone and signal-driven.

- `Card` (`<bri-card>`):
  - `@Input() card: Card | null` (null = back).
  - Reads `CardSetService.activeSet()` and resolves the asset URL.
  - Uses `<img>` with `loading="eager"` (we want them up immediately) and `decoding="sync"`.
  - Falls back to placeholder asset if the active set's manifest doesn't include the card; emits a console warning at most once per `(set, card)` pair.

- `MyHand`:
  - Renders `myHand` cards in a fan.
  - Click handler emits `play(card)` only if `legalMoves()` allows it.
  - Disabled state when not my turn.

- `OpponentArea`:
  - Renders N face-down cards with a count badge.
  - For 4p, partner gets a small "🤝" chip.

- `TrickArea`:
  - Animates plays in: each `cardPlayed` event triggers a CSS transition from the relevant opponent area to the trick zone.
  - On `trickResolved`, fades cards into the winner's pozzo.

- `BriscolaIndicator`:
  - Shows the briscola card perpendicular under the stock; fades out when stock empties.

- `Stock`:
  - Renders a small stack of card-backs with a count badge.

- `Scoreboard`:
  - Per-seat score in 2p; team scores in 4p (derived from seat scores).

- `ChatPanel`:
  - Tabs: "All" (game scope). In `LastHand` for 4p, banner highlights "Tactical phase — coordinate with your partner" but the underlying transport is the same.

- `ReconnectBanner`:
  - Visible when any seat has `Disconnected`. Shows countdown.

- `EndGameDialog`:
  - Shows winner banner, score breakdown, "Back to lobby" button.

### Step 8.4 — Animations [S]

- `@angular/animations` for card fly-ins.
- Reduced motion: `@media (prefers-reduced-motion)` disables transitions.

### Step 8.5 — Component tests [S]

- `Card`: renders correct asset; falls back for missing.
- `MyHand`: disables interactions when not my turn; click emits `play`.
- `Scoreboard`: 4p team math.
- `ReconnectBanner`: countdown.

**Phase 8 exit:** two real users can play a full 2p game in the browser, then a full 4p game. Manual smoke + Playwright in Phase 13 for automation.

---

## Phase 9 — Card sets

### Step 9.1 — Backend manifest discovery [S]

**Where:**
- `backend/src/Briscola.Api/wwwroot/card-sets/{setId}/manifest.json` (+ asset files) — physical assets live in the API project so static-file middleware can serve them at `/card-sets/{setId}/{file}`.
- `backend/src/Briscola.Api/CardSets/CardSetCatalog.cs` — the scanner / accessor.
- `backend/src/Briscola.Api/Controllers/CardSetsController.cs` — REST surface.

The catalog and the controller live in `Briscola.Api`, NOT in the application layer — the application layer doesn't know about `wwwroot` or HTTP. If `LobbyService` or any other application-layer service ever needs to validate a card-set id, it gets a small port (`ICardSetCatalog` in `Briscola.Application.Ports`) with the implementation registered from `Briscola.Api`.

**`CardSetCatalog`:**
- Singleton (`AddSingleton<CardSetCatalog>`).
- Scans `wwwroot/card-sets/*/manifest.json` at startup. Logs any malformed manifests at `Warning`. **Refuses to start** if `placeholder` is missing — that's an installation bug, not a runtime warning.
- Exposes `IReadOnlyList<CardSetManifest> All()` and `bool Contains(string id)`.

**`GET /api/v1/card-sets`** returns the cached catalog as a list of DTOs (id, name, license, preview path). Response is the same for every caller (no auth state changes the result), so it can be served behind a 5-minute response-cache header in production.

### Step 9.2 — `placeholder` SVG set [S]

**Where:** `backend/src/Briscola.Api/wwwroot/card-sets/placeholder/`

- 40 SVGs `{suit}-{rank}.svg` (e.g. `bastoni-asso.svg`, `coppe-tre.svg`, …).
- Each SVG: 200×280 viewBox, 8px corner radius, suit symbol centered, rank label top-left + bottom-right (rotated 180°).
- `back.svg`: a simple geometric pattern.
- `preview.png`: 600×280 montage of 4 suits × asso for the picker thumbnail.
- `manifest.json` per the README schema.

### Step 9.3 — Frontend `CardSetService` [S]

- On bootstrap (after auth), fetches `/api/v1/card-sets`, builds `CardSet` instances.
- `activeSet` signal seeded from `currentUser.activeCardSetId`, persists changes via `PATCH /me`.

### Step 9.4 — `CardSetPicker` [S]

- Located at `/profile`. Shows preview thumbnails; click selects.

### Step 9.5 — Piacentine slot [S]

- `wwwroot/card-sets/piacentine/manifest.json` exists with the metadata; no images. Resolver falls back to `placeholder` per missing card and warns once.

### Step 9.6 — `docs/card-sets.md` [S]

Document: directory layout, manifest schema, suit/rank slugs, file naming, licensing checklist, "how to add a new set" steps.

**Phase 9 exit:** users can switch between bundled sets; adding a new set is a content-only change.

---

## Phase 10 — Match history, ranking, spectator

### Step 10.1 — `/profile` history & ranking [S]

- `GET /me/history` paginated; renders a list with date, mode, opponents, result, score.
- `GET /me/ranking` shows Elo + W/L/D + games played.

### Step 10.2 — Spectator route [S]

- `/game/:id/spectate`.
- `GameService.connectAsSpectator(gameId)` calls `POST /games/{id}/spectate` then joins the `GameHub` and listens for spectator-redacted snapshots.
- UI shows the same table as a player but without a `MyHand`.

### Step 10.3 — Visibility-rule E2E (lightweight) [S]

- Component tests assert that `MyHand` component is not rendered when state has no `myHand`.
- Integration test (already in Phase 5) covers the wire-level redaction.

**Phase 10 exit:** profile and spectator features visible and tested.

---

## Phase 11 — Hardening

### Step 11.1 — Logging review [S]

- Grep for `_logger.Log*` calls; ensure none log: `password`, `token`, `accessToken`, `refreshToken`, `card`, `hand`, `chatText`. Add a unit test that uses Serilog's test sink to assert.
- Correlation ID middleware; ensure ID is on every log line and propagated to outbound calls.

### Step 11.2 — Health & readiness [S]

- `/healthz`: returns 200 OK if process is alive (no checks).
- `/readyz`: pings DB via `dbContext.Database.CanConnectAsync()`. Returns 503 on failure.
- `Microsoft.Extensions.Diagnostics.HealthChecks` package is overkill for v1; hand-rolled controllers are fine.

### Step 11.3 — OpenTelemetry [S]

- `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `…EntityFrameworkCore`, `…Runtime`.
- Default exporter: console in dev, OTLP in prod (configurable endpoint).
- Custom metrics: `briscola.active_games` (UpDownCounter), `briscola.connected_players`, `briscola.moves_total`.

### Step 11.4 — Security review checklist [S]

Walk `docs/security.md` line by line; each item maps to a test or a review note. Items:
- No card data in non-self-targeted messages (asserted by hub tests).
- All hub callbacks validate caller identity vs. seat.
- Rate limits on chat and play-card.
- HSTS, CSP, security headers (asserted by integration test).
- Dependency audit (`dotnet list package --vulnerable --include-transitive`, `npm audit --production`). Fail build on high/critical.
- JWT secret from env var only; cannot start without it in prod.

### Step 11.5 — Stress test [S]

- A small NBomber or k6 script: 50 concurrent 2p games for 30 minutes.
- Assert: no growth in active-games counter beyond expected; no `OutOfMemoryException`; CPU < 70% on a 2-core VM.
- Document baseline numbers in `docs/deployment.md`.

### Step 11.6 — Graceful shutdown [S]

- `IHostApplicationLifetime.ApplicationStopping` callback drains hubs (refuses new connections, lets in-flight commands complete with a 10 s budget), snapshots all active rooms (already done per move; this is a final flush), closes DB.

**Phase 11 exit:** ops checklist green; metrics dashboard exists in `docs/deployment.md`.

---

## Phase 12 — Containerization & deploy

### Step 12.1 — Backend Dockerfile [S]

**Where:** `backend/Dockerfile`

- Multi-stage:
  - Stage 1 (`mcr.microsoft.com/dotnet/sdk:10.0`): copy csprojs, restore, copy rest, publish.
  - Stage 2 (`mcr.microsoft.com/dotnet/aspnet:10.0`): copy publish output; install `curl` (`apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*`) so HEALTHCHECK can run; non-root user (`uid:1000`); `USER 1000`; `EXPOSE 8080`; `HEALTHCHECK --interval=30s --timeout=3s --start-period=15s CMD curl -fsS http://localhost:8080/healthz || exit 1`.
  - Alternative if image size matters more than convenience: use `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra` (chiseled image with `curl` pre-installed). Document the choice in `docs/deployment.md`.
- `wwwroot/card-sets/placeholder/` is included in the publish output via `<Content Include="wwwroot\**\*">` in the csproj.

### Step 12.2 — Frontend Dockerfile [S]

**Where:** `frontend/Dockerfile`

- Stage 1 (`node:22-alpine`): `npm ci`, `ng build --configuration production`.
- Stage 2 (`nginx:alpine`): copy `dist/elk-briscola/` to `/usr/share/nginx/html`; custom `nginx.conf` for SPA fallback (`try_files $uri $uri/ /index.html`), gzip, long cache on hashed assets, `index.html` no-cache.

### Step 12.3 — `docker-compose.yml` [S]

```yaml
services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: briscola
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      POSTGRES_DB: briscola
    volumes: [pgdata:/var/lib/postgresql/data]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U briscola -d briscola"]
  api:
    build: { context: ./backend, dockerfile: Dockerfile }
    environment:
      ConnectionStrings__Provider: Postgres
      ConnectionStrings__Default: "Host=postgres;Username=briscola;Password=${POSTGRES_PASSWORD};Database=briscola"
      Authentication__Jwt__SigningKey: ${JWT_SIGNING_KEY}
      Authentication__Jwt__Issuer: briscola
      Authentication__Jwt__Audience: briscola
      Cors__AllowedOrigins: ${ALLOWED_ORIGINS}
      Migrations__RunOnStartup: "true"
    depends_on: { postgres: { condition: service_healthy } }
    healthcheck:
      test: ["CMD-SHELL", "curl -fsS http://localhost:8080/healthz || exit 1"]
      interval: 30s
      timeout: 3s
      start_period: 15s
      retries: 3
  frontend:
    build: { context: ./frontend, dockerfile: Dockerfile }
    depends_on: { api: { condition: service_healthy } }
    ports: ["8080:80"]
volumes: { pgdata: {} }
```

`.env.example` lists every variable.

### Step 12.4 — `docker-compose.dev.yml` [S]

- Just `postgres`, exposing 5432 to localhost.

### Step 12.5 — `docs/deployment.md` [S]

- Step-by-step prod deploy (env vars, secrets, backup `pg_dump`, restore, migration job, rolling restart procedure).
- Document the JWT signing-key rotation procedure (overlap window: accept old + new for a while, then drop old).

### Step 12.6 — CI: build & push images on tag [S]

- New workflow `release.yml` triggered on `push: tags: [v*]`.
- Builds both images, pushes to GHCR with tag `v*` and `latest`.

**Phase 12 exit:** clean machine + Docker → working game in under 10 minutes following the doc.

---

## Phase 13 — End-to-end golden path

### Step 13.1 — Playwright config [M]

**Where:** `frontend/e2e/playwright.config.ts`, `frontend/e2e/tests/`

- Two projects: `chromium-A`, `chromium-B` — same browser, different storage state, run in parallel.
- `webServer`: launches `npm start` for the frontend AND points at a docker-compose-started backend (alternative: a dedicated `playwright.test.compose.yml`).

### Step 13.2 — 2p golden path [M]

**File:** `frontend/e2e/tests/2p-golden-path.spec.ts`

Pseudocode:

```ts
test('two players play a 2p game to completion', async ({ browser }) => {
  const ctxA = await browser.newContext();
  const ctxB = await browser.newContext();
  const A = await ctxA.newPage();
  const B = await ctxB.newPage();

  await registerAndLogin(A, 'alice');
  await registerAndLogin(B, 'bob');

  await A.goto('/lobby');
  await A.click('text=Create');
  await A.fill('[name=name]', 'Alice vs Bob');
  await A.click('text=Create game');

  await B.goto('/lobby');
  await B.click('text=Join'); // joins the only open game

  // Both arrive at /game/:id
  await expect(A).toHaveURL(/\/game\//);
  await expect(B).toHaveURL(/\/game\//);

  // Drive moves: each page plays the first legal card on its turn until end.
  await playToCompletion(A, B);

  // Whichever wins, both pages show end-game dialog with a winner banner.
  await expect(A.getByTestId('end-game-dialog')).toBeVisible();
  await expect(B.getByTestId('end-game-dialog')).toBeVisible();
});
```

`playToCompletion` polls each page for "is it my turn" and clicks the first legal card.

### Step 13.3 — Reconnect E2E [M]

- Same setup, but mid-game, close ctxA, wait 3 s, reopen, navigate to `/game/:id` — expect state restored. Continue play. Assert end.

### Step 13.4 — CI integration [S]

- `.github/workflows/e2e.yml`:
  - Trigger on PR.
  - Spin up `docker-compose.test.yml` (Postgres + API + frontend served by nginx).
  - Run `npx playwright test`.
  - Upload HTML report on failure.

**Phase 13 exit:** E2E green in CI.

---

## Phase 14 — Documentation polish & release

### Step 14.1 — `docs/architecture.md` [S]

- C4-style diagrams (system context, container, component) using Mermaid.
- Match the implementation; cite `Phase`/file paths.

### Step 14.2 — `docs/game-rules.md` [S]

- Mirror of the README's rules section, but extended with engine-implementation notes (e.g. "the Strength function is `CardTables.Strength`") so it's the natural reading order for someone trying to understand the engine.

### Step 14.3 — `docs/api.md` [S]

- REST: include the OpenAPI YAML (`api/v1/openapi.yaml` produced by Swashbuckle at build).
- SignalR: hand-written; method-by-method. Examples of payloads.

### Step 14.4 — ADRs [S]

- `0002-server-authoritative-game-state.md`.
- `0003-signalr-over-raw-websockets.md`.
- `0004-pluggable-card-sets.md`.
- `0005-snapshot-store-not-event-sourcing.md`.

### Step 14.5 — Release [S]

- Update `README.md` Definition-of-Done to all checked.
- Tag `v1.0.0`.
- Auto-generated release notes via GitHub Actions.

**Phase 14 exit:** the [Definition of done](README.md#definition-of-done) checklist is fully ticked.

---

## Out of scope for v1

- Tournaments / brackets / multi-hand sessions.
- Card swaps, viewing teammates' hands, hard "no talking" enforcement (last-hand regional variants beyond the implemented subset).
- Multiple regional rule sets selectable per game.
- Mobile native apps (PWA support is implicit but not optimized).
- Friends list / private messaging.
- Custom card backs per player.
- Replay viewer (data is captured via `ShuffleSeed` + `GameMoves`; UI is v2).
- Bots / single-player mode.
- Profanity / abuse moderation tooling.
- Multilingual UI (`it.json` stub committed but not populated in v1).

---

## Risk register

| Risk | Mitigation |
|---|---|
| Rules ambiguity (regional variants) | One canonical spec in `docs/game-rules.md` and Phase-1 tests; deviations require an ADR. |
| Concurrency bugs in the orchestrator | Single-writer channel per room; property tests with random interleavings; optimistic-concurrency `Version` field with retry on seat-fill races. |
| Card art licensing | Plug-in architecture means we ship without it; sourcing is a content task with a checklist; per-card fallback to `placeholder`. |
| Disconnect storms | Server-authoritative timers; reconnect is idempotent; snapshots persisted per move; in-memory event bus is per-process (horizontal scale is v2). |
| Cheating clients | Server is the only source of truth; redacted state per recipient; `CardNotInHand` errors logged and metered. |
| Migration drift between SQLite and Postgres | Two migration assemblies; integration suite runs against Postgres; schema-parity test boots both and compares model snapshots. |
| Lobby spam / chat abuse | Rate limits; profanity filter is out of scope; message length capped at 500 chars; admin tooling deferred to v2. |
| Stuck `Open` games | Background `OpenLobbyJanitor` moves them to `Abandoned` after `Game:OpenLobbyTtlMinutes`. |
| JWT secret leakage | Env-var-only, rotation procedure documented; access tokens short-lived; refresh-token reuse triggers chain revocation. |
| Single-process orchestrator becomes a bottleneck | Designed to run as one instance for v1; a future v2 can shard rooms across instances using a redis-backed channel; today the in-memory bus is acceptable. |
| FluentAssertions license trap (v8+ commercial) | Pinned to 7.2.2 (last Apache-2.0). AGENTS.md and README tech-stack table both call this out. If we need v8 features, we either accept the license or migrate to AwesomeAssertions (community fork) — that's a deliberate decision, not a casual `dotnet add package` bump. |
| WSL toolchain trap (Windows shims on `PATH`) | Documented in AGENTS.md § Tooling environment; `~/.elk-env.sh` prepends Linux bins. Any PR adding a new `npx`/`dotnet` invocation in scripts must source the prelude or document why not. |
| Stale `GameRoom._record` after a concurrency conflict | Phase 2 follow-up item: room is currently "poisoned" if an external write bumps `Version` — subsequent commands keep failing with `GameCommandException`. Phase 3 / 5 will add eviction-from-orchestrator + refetch on conflict. |
