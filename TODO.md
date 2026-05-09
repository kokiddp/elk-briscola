# TODO — exhaustive, step-by-step development plan for elk-briscola

This is the **single source of truth for implementation work**. It is intentionally verbose and prescriptive: any LLM or human contributor should be able to pick up a sub-task and implement it without re-deriving design choices. When this document and [README.md](README.md) ever disagree, **README.md wins on architecture/contract questions** and this file is updated to match.

How to read this file:

- Phases are sequential. **Do not start phase N+1 until phase N's exit criteria are green.** Inside a phase, sub-steps within a "Step N" block are usually sequential too; sub-steps in different "Step N" blocks of the same phase can be parallelized only if explicitly noted.
- Every step has: **What**, **Where** (paths), **How** (concrete actions), **Why** (rationale, if non-obvious), **Tests**, **Acceptance**.
- Status markers: `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked. Update inline as work proceeds (see [AGENTS.md](AGENTS.md) for protocol).
- Size legend: `S` ≤ half a day · `M` ≤ two days · `L` 2–5 days. These are *informational only*; the only hard rule is "complete the step or break it down further; don't leave it half-done."

> Conventions used everywhere below: namespace prefix `Briscola.*`; C# 14 / .NET 10 (LTS); Angular 21 standalone components; Node 22 LTS; SCSS; UTC timestamps; no `cd` chains in scripts (use absolute paths). All code identifiers in this document are the **canonical** names — implement them exactly as written.

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

### Step 0.2 — Backend skeleton (solution + projects, no code) [S]

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

### Step 0.3 — Frontend skeleton (Angular workspace) [M]

**What:** initialize an Angular 21 standalone workspace with strict TS, SCSS, Jest, ESLint, and Prettier. Split into three sequential sub-steps.

**Where:** `frontend/`

#### Step 0.3a — Generate the Angular workspace [S]

- Run from repo root: `npx -p @angular/cli@21 ng new elk-briscola-frontend --directory=frontend --style=scss --strict --routing --skip-git --skip-install --ssr=false --package-manager=npm`.
- `cd frontend && npm install`.
- Strip `app.component.html` to a single `<router-outlet />`.
- Strip `app.component.ts` to a minimal standalone component importing `RouterOutlet`.
- Verify: `npm run build` succeeds; `npm start` serves on `:4200`.

> Note: as of Angular 17, standalone is the default and the legacy `--standalone` flag is a no-op. Don't pass it.

#### Step 0.3b — Karma → Jest migration [S]

- Add devDeps (pin to versions matching Angular 21; if `jest-preset-angular` for Angular 21 is not yet on npm at implementation time, use the latest published and pin in `package.json`. Document the version chosen in `frontend/README.md`):
  - `jest`, `jest-preset-angular`, `jest-junit`, `@types/jest`, `@testing-library/angular`, `@testing-library/jest-dom`.
- Remove Karma deps from `package.json`: `karma`, `karma-chrome-launcher`, `karma-coverage`, `karma-jasmine`, `karma-jasmine-html-reporter`, `jasmine-core`, `@types/jasmine`.
- Delete `karma.conf.js` and the `test` block from `angular.json`.
- Create `frontend/jest.config.cjs`:
  ```js
  module.exports = {
    preset: 'jest-preset-angular',
    setupFilesAfterEach: ['<rootDir>/setup-jest.ts'],
    moduleNameMapper: { '^@app/(.*)$': '<rootDir>/src/app/$1' },
    testEnvironment: 'jsdom',
  };
  ```
- Create `frontend/setup-jest.ts`:
  ```ts
  import 'jest-preset-angular/setup-jest';
  import '@testing-library/jest-dom';
  ```
- Rewrite `app.component.spec.ts` to a trivial Jest test:
  ```ts
  import { render } from '@testing-library/angular';
  import { AppComponent } from './app.component';
  test('renders without crashing', async () => {
    await render(AppComponent);
  });
  ```
- Update `package.json` scripts: `"test": "jest"`, `"test:ci": "jest --ci --reporters=default --reporters=jest-junit"`.
- Verify: `npm test` runs 1 passing test.

> **Fallback if jest-preset-angular for Angular 21 is unavailable:** keep Karma+Jasmine for v1; revisit at Phase 14. Document the decision as an ADR (`docs/adr/0006-test-runner-choice.md`). The frontend test surface in v1 is small enough that either runner works.

#### Step 0.3c — ESLint + Prettier [S]

- `npx ng add @angular-eslint/schematics --skip-confirmation`.
- Add `prettier` and `prettier-plugin-organize-imports` as devDeps.
- Create `.prettierrc.json`: `{ "printWidth": 100, "singleQuote": true, "trailingComma": "all", "plugins": ["prettier-plugin-organize-imports"] }`.
- Create `.prettierignore`: `dist/`, `node_modules/`, `coverage/`.
- Add `package.json` scripts: `"lint": "ng lint"`, `"format": "prettier --write ."`, `"format:check": "prettier --check ."`.
- Verify: `npm run lint` clean; `npm run format:check` clean.

**Tests:** the rewritten `app.component.spec.ts` (one test) passes under Jest.

**Acceptance:** from `frontend/`, all of these succeed: `npm install`, `npm run build`, `npm test`, `npm run lint`, `npm run format:check`.

---

### Step 0.4 — GitHub Actions CI [S]

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

### Step 0.5 — Repo conventions doc cross-link [S]

**What:** ensure `AGENTS.md` exists at repo root (created separately from this plan) and is referenced from `README.md`.

**Where:** `/AGENTS.md`, `/README.md`.

**How:** README's intro paragraph links `[AGENTS.md](AGENTS.md)` for contributors/LLMs.

**Acceptance:** dead-link checker (Phase 14 will add) finds no broken refs.

**Phase 0 exit:** CI green on a no-op PR; both backend and frontend skeletons compile and test; `docs/`, `AGENTS.md`, `README.md`, `TODO.md` all present.

---

## Phase 1 — Domain rules engine

**Goal:** a pure C# library that correctly implements Briscola, with exhaustive unit tests. **No I/O, no DB, no ASP.NET, no JSON, no logging.** The only types referenced from outside the BCL are types defined within `Briscola.Domain` itself.

> The domain layer must be implementable from this section alone. If you find yourself reading the README to know what to build here, file a TODO update.

### Step 1.1 — Value types & enums [S]

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

### Step 1.2 — `CardTables` (strength + points) [S]

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

### Step 1.3 — `IRandomSource` and `SeededRandomSource` [S]

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

### Step 1.4 — `Deck` shuffler [S]

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

### Step 1.5 — Hand representation [S]

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

### Step 1.6 — `GameState` immutable record [M]

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

### Step 1.7 — `InvalidMoveException` [S]

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

### Step 1.8 — `BriscolaEngine` — `StartGame` [M]

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

### Step 1.9 — `BriscolaEngine` — `PlayCard`, trick resolution, drawing [M]

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

### Step 1.10 — `Briscola.Domain.Tests` — exhaustive coverage [L]

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

### Step 2.1 — Project setup [S]

**Where:** `backend/src/Briscola.Application/`

**Refs:** `Briscola.Domain` (project), `Microsoft.Extensions.Logging.Abstractions` (NuGet), `Microsoft.Extensions.Options` (NuGet). NO EF Core, NO ASP.NET, NO SignalR.

---

### Step 2.2 — Ports (interfaces) [S]

**Where:** `backend/src/Briscola.Application/Ports/`

**Files:**

- `IClock.cs` — `DateTimeOffset UtcNow { get; }`. Default impl `SystemClock` provided here for non-test use; the application layer's DI registers it.
- `IRandomSource.cs` — re-exposes `Briscola.Domain.Primitives.IRandomSource`.
- `IUserContext.cs` — `Guid UserId { get; }`, `string UserName { get; }`. Implementation lives in `Briscola.Api`.
- `IGameRepository.cs`:
  ```csharp
  Task<GameRecord?> GetAsync(Guid id, CancellationToken ct);
  Task<IReadOnlyList<GameRecord>> ListByStatusAsync(GameStatus status, int take, CancellationToken ct);
  Task CreateAsync(GameRecord record, CancellationToken ct);
  Task UpdateAsync(GameRecord record, CancellationToken ct);
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

**Records used by ports** (in `Briscola.Application/Persistence/Records.cs`):

```csharp
public sealed record GameRecord(
    Guid Id, GameMode Mode, string Name, GameStatus Status,
    Guid CreatedByUserId, DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt, DateTimeOffset? EndedAt,
    long ShuffleSeed, string StateSnapshotJson,
    Suit BriscolaSuit, bool IsPrivate, string? PasswordHash);

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

---

### Step 2.3 — `GameOptions` [S]

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

### Step 2.4 — `GameOrchestrator` & `GameRoom` [M]

**Where:**
- `backend/src/Briscola.Application/Orchestration/GameOrchestrator.cs`
- `backend/src/Briscola.Application/Orchestration/GameRoom.cs`
- `backend/src/Briscola.Application/Orchestration/Commands/*.cs`
- `backend/src/Briscola.Application/Orchestration/Events/*.cs`

**Commands** (records):

```csharp
public abstract record GameCommand(Guid GameId);
public sealed record JoinGameCommand(Guid GameId, Guid UserId, int? PreferredSeat) : GameCommand(GameId);
public sealed record LeaveGameCommand(Guid GameId, Guid UserId) : GameCommand(GameId);
public sealed record PlayCardCommand(Guid GameId, Guid UserId, Card Card) : GameCommand(GameId);
public sealed record ViewOwnPileCommand(Guid GameId, Guid UserId) : GameCommand(GameId);
public sealed record DisconnectCommand(Guid GameId, Guid UserId) : GameCommand(GameId);
public sealed record ReconnectCommand(Guid GameId, Guid UserId) : GameCommand(GameId);
public sealed record IdleTickCommand(Guid GameId, DateTimeOffset At) : GameCommand(GameId);
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
```

`RedactedStateForUser` is built by the room when a `JoinedEvent` or `StateUpdatedEvent` is emitted: each recipient gets their own copy (their hand visible, others' hand counts only).

```csharp
public sealed record RedactedStateForUser(
    Guid GameId, GameMode Mode, GamePhase Phase, int DealerSeat,
    int LeaderSeat, int NextToPlaySeat, int TrickNumber,
    Card BriscolaCard, Suit BriscolaSuit, int StockCount,
    ImmutableArray<int> HandCountsBySeat,
    ImmutableArray<Card>? MyHand,                 // null for spectators
    ImmutableArray<int>? MyPozzo,                 // available only on viewOwnPile during LastHand
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

For each command popped from the channel:
1. If room has no `GameState` yet (lobby phase) and the command is `JoinGameCommand`/`LeaveGameCommand`, handle via `LobbyService` collaborator.
2. Else apply via the engine (`PlayCard`) or via internal handlers (`Disconnect`, `Reconnect`, `IdleTick`).
3. After successful state mutation:
   - Persist `GameRecord.StateSnapshotJson` (single UPSERT on `Games`).
   - Persist a `MoveRecord` (only for `PlayCard`, `Forfeit`, `Disconnect`, `Reconnect`, `IdleTimeout`).
   - Publish `IGameEvent`s to `IGameEventBus`.

**Why a single channel per game and not per process:** keeps the concurrency model trivially correct (per-game serial), avoids global lock contention, scales to many concurrent games on one box.

---

### Step 2.5 — Reconnect & idle timers [M]

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

### Step 2.6 — `LobbyService` [S]

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

### Step 2.7 — `RankingService` [S]

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

**Idempotency:** `RankingService.ApplyResultAsync(gameId, result)` checks whether `gameId` has already been applied (via a `ProcessedGames` set in the `Rankings` schema, or a flag on `GameResults`). Re-application is a no-op.

---

### Step 2.8 — `MatchHistoryService` [S]

Trivial wrapper that, on game end, persists a `GameResultRecord`. `GameMoves` is already persisted incrementally by the orchestrator. No additional logic.

---

### Step 2.9 — `Briscola.Application.Tests` [M]

**Where:** `backend/tests/Briscola.Application.Tests/`

**Doubles:**
- `FakeClock : IClock` — settable `UtcNow`.
- `FakeRandomSource : IRandomSource` — fixed seed for reproducibility.
- `InMemoryGameRepository`, `InMemoryChatRepository`, `InMemoryRankingRepository` — `Dictionary`-backed.
- `FakeTimerService` — virtual time; `Advance(TimeSpan)` triggers due callbacks.
- `RecordingGameEventBus` — captures events for assertions.

**Test classes:**

- `OrchestratorConcurrencyTests`:
  - **1000 interleaved commands** test: spawn 8 producer tasks, each enqueueing valid moves on a single room; assert final state is consistent (`SeatScores.Sum() == 120`).
  - `Single_writer_invariant`: two `PlayCardCommand`s for the same seat in the same trick → only the first succeeds, second yields `InvalidMoveRejectedEvent`.
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
  - Pub/sub multi-subscriber semantics.

**Acceptance:** all tests green; line coverage on `Briscola.Application` ≥ 85%.

**Phase 2 exit:** application layer fully exercised in tests against in-memory fakes; no API/persistence code yet.

---

## Phase 3 — Infrastructure (EF Core, Identity, JWT)

**Goal:** persistence and auth wired up against SQLite (dev) and Postgres (prod).

### Step 3.1 — Project setup [S]

**Where:** `backend/src/Briscola.Infrastructure/`

**Refs:** `Briscola.Application` (project); NuGet: `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Sqlite`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Serilog`, `Serilog.AspNetCore`, `Serilog.Sinks.Console`, `Serilog.Sinks.File`, `BCrypt.Net-Next` (for game-room passwords; user passwords use Identity's PBKDF2).

---

### Step 3.2 — `BriscolaDbContext` & entity types [M]

**Where:**
- `backend/src/Briscola.Infrastructure/Persistence/BriscolaDbContext.cs`
- `backend/src/Briscola.Infrastructure/Persistence/Entities/*.cs`
- `backend/src/Briscola.Infrastructure/Persistence/Configurations/*.cs`

**Entities (one file each):**

- `ApplicationUser : IdentityUser<Guid>` — `DisplayName` (required, 1..32), `ActiveCardSetId` (string, default `"placeholder"`), `CreatedAt` (UTC).
- `RefreshTokenEntity` — `Id`, `UserId`, `TokenHash` (SHA-256 hex), `ExpiresAt`, `RevokedAt?`, `ReplacedByTokenId?`.
- `GameEntity` — mirrors `GameRecord` plus `RowVersion` (`byte[]`, EF concurrency token; for Postgres mapped to `xmin`).
- `GameSeatEntity` — composite key `(GameId, SeatIndex)`, plus `UserId`, `JoinedAt`, `LeftAt?`.
- `GameMoveEntity` — `Id` (PK), `GameId`, `MoveIndex`, `SeatIndex`, `Type`, `PayloadJson`, `CreatedAt`. Unique `(GameId, MoveIndex)`.
- `GameResultEntity` — `GameId` (PK), `Kind`, `WinnerKey?`, `SeatScoresJson`, `TeamScoresJson?`, `Reason`.
- `ChatMessageEntity` — `Id`, `Scope`, `GameId?`, `UserId`, `Text` (`varchar(500)`), `CreatedAt`.
- `RankingEntity` — `UserId` (PK, FK Users), `Elo`, `Wins`, `Losses`, `Draws`, `GamesPlayed`, `UpdatedAt`.

**Configurations** in `IEntityTypeConfiguration<T>` classes (NOT inline in `OnModelCreating` — keeps the DbContext thin):

- All `string` columns get explicit `HasMaxLength`.
- All `DateTimeOffset` columns get `HasConversion<DateTimeOffsetToBinaryConverter>` on SQLite, raw `timestamptz` on Postgres.
- Indexes per the README schema section.
- `GameEntity.RowVersion` configured with `IsRowVersion()`.

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

### Step 3.3 — Migration assemblies [M]

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

### Step 3.4 — ASP.NET Identity wiring [M]

- `IdentityCore<ApplicationUser>` with `AddEntityFrameworkStores<BriscolaDbContext>()`.
- Password options: `RequireDigit=true`, `RequiredLength=10`, `RequireNonAlphanumeric=false`, `RequireUppercase=false`, `RequireLowercase=false`. (Length over complexity.)
- Username options: `AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-"`, `RequireUniqueEmail=true`.
- `UserManager<ApplicationUser>` available via DI.

---

### Step 3.5 — JWT issuance & refresh tokens [M]

**Where:** `backend/src/Briscola.Infrastructure/Auth/`

- `JwtOptions` bound from config: `Issuer`, `Audience`, `SigningKey` (≥ 32 bytes, base64 in config), `AccessTokenLifetimeMinutes` (15), `RefreshTokenLifetimeDays` (14).
- `JwtIssuer` service: `CreateAccessToken(ApplicationUser)`, `CreateRefreshToken(userId) -> (token, hash, expiresAt)`.
- `RefreshTokenService`:
  - `RotateAsync(currentRefreshToken) -> (newAccess, newRefresh)`. Atomic in a transaction: mark old token revoked with `ReplacedByTokenId = newId`.
  - Revoke chain on detected reuse (if a revoked token is re-presented, revoke the entire chain — defense against token theft).
- Access token claims: `sub` (user id), `name` (username), `display_name`, `card_set` (active id), `iat`, `exp`, `iss`, `aud`, `security_stamp`. The validator (in `Briscola.Api`) re-checks `security_stamp` against the user's current stamp on every request — password change rotates the stamp, invalidating outstanding access tokens.

---

### Step 3.6 — Repositories (port implementations) [S]

**Where:** `backend/src/Briscola.Infrastructure/Persistence/Repositories/`

- `EfGameRepository : IGameRepository`.
- `EfChatRepository : IChatRepository`.
- `EfRankingRepository : IRankingRepository`.

`EfGameRepository.UpdateAsync` returns the row count from `SaveChangesAsync` and surfaces `DbUpdateConcurrencyException` as a `ConcurrencyConflictException` defined in the application layer; orchestrator/lobby handlers retry up to 3 times.

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
13. Application-layer service registrations: `AddSingleton<GameOrchestrator>`, `AddScoped<LobbyService>`, …
14. Build the app.
15. Middleware order: `UseSerilogRequestLogging` → `UseExceptionHandler("/error")` → `UseSecurityHeaders()` → `UseHsts()` (prod) → `UseHttpsRedirection()` (prod) → `UseStaticFiles()` (for `wwwroot/card-sets/`) → `UseRouting` → `UseCors` → `UseRateLimiter` → `UseAuthentication` → `UseAuthorization` → `MapControllers` → `MapHubs` → `MapHealthChecks`.
16. **Migrations on startup** behind `Migrations:RunOnStartup=true` flag (default false in prod); for dev/SQLite it's true.
17. `app.Run();`

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
- `GameSummary { Guid Id, GameMode Mode, string Name, GameStatus Status, int Players, int MaxPlayers, string CreatedByDisplayName, DateTimeOffset CreatedAt, bool IsPrivate }`
- `GameDetail` (extends `GameSummary` with seat list and (if running and caller is a participant) authoritative state).

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

A background `GameEventDispatcher` `IHostedService` subscribes to `IGameEventBus` and fans out to `IHubContext<GameHub, IGameClient>` groups. The dispatcher is the **only** code that calls `IHubContext` for game events — keeps the orchestrator clean of SignalR.

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

- `CardSetCatalog` singleton scans `wwwroot/card-sets/*/manifest.json` at startup; logs any malformed manifests; refuses to start if `placeholder` is missing.
- `GET /api/v1/card-sets` returns the catalog.

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
| Concurrency bugs in the orchestrator | Single-writer channel per room; property tests with random interleavings; explicit RowVersion concurrency on seat-fill races. |
| Card art licensing | Plug-in architecture means we ship without it; sourcing is a content task with a checklist; per-card fallback to `placeholder`. |
| Disconnect storms | Server-authoritative timers; reconnect is idempotent; snapshots persisted per move; in-memory event bus is per-process (horizontal scale is v2). |
| Cheating clients | Server is the only source of truth; redacted state per recipient; `CardNotInHand` errors logged and metered. |
| Migration drift between SQLite and Postgres | Two migration assemblies; integration suite runs against Postgres; schema-parity test boots both and compares model snapshots. |
| Lobby spam / chat abuse | Rate limits; profanity filter is out of scope; message length capped at 500 chars; admin tooling deferred to v2. |
| Stuck `Open` games | Background `OpenLobbyJanitor` moves them to `Abandoned` after `Game:OpenLobbyTtlMinutes`. |
| JWT secret leakage | Env-var-only, rotation procedure documented; access tokens short-lived; refresh-token reuse triggers chain revocation. |
| Single-process orchestrator becomes a bottleneck | Designed to run as one instance for v1; a future v2 can shard rooms across instances using a redis-backed channel; today the in-memory bus is acceptable. |
