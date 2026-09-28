# Typo-tolerant search design (Everything 1.5 / EverythingToolbar fork)

Status: working design, branch `feature/typo-tolerant-search`.
Baseline: upstream `srwi/EverythingToolbar` `develop` @ `4752441cdfb219bcef0aec42716ff97249775cc7`.
Local Everything: **1.5.0.1396a** x64 at `C:\Program Files\Everything 1.5a\Everything64.exe`, instance `1.5a`, running elevated. Verified 2026-09-29.
Prior-art gate record: `docs/Research/landscape.yaml`.

## Baseline facts (verified by audit, 2026-09-29)

- TFMs: all managed projects `net8.0-windows10.0.17763.0` via `Directory.Build.props`; LangVersion 12, nullable enable, strong-name signing, central package management (`Directory.Packages.props`).
- No test projects, no test packages anywhere. CI: nuget restore + MSBuild per-arch, CSharpier check, XamlStyler.
- Formatter: CSharpier (defaults). Build: MSBuild on `EverythingToolbar.sln` with `-p:Platform=x64 -p:SignAssembly=false` (repo has no `EverythingToolbar.snk` locally; CONTRIBUTING says disable signing to build; restore must run with the same Platform, else NETSDK1112). Native `EverythingSDK3.vcxproj` produces `Everything3.dll`, xcopy steps drop it next to outputs.

### Current search flow (exact hops)

```
SearchBox (SearchWindow.xaml) / SearchBoxViewModel (EverythingToolbar/ViewModels/SearchBoxViewModel.cs)
  -> SearchState (EverythingToolbar.App/Search/SearchState.cs)  — holds SearchTerm, Filter, match flags;
     BuildSearchQuery() applies macros + global modifiers, produces Core.Search.SearchQuery
  -> SearchSession (EverythingToolbar.App/Search/SearchSession.cs) — Rebuild() on state change;
     constructs EverythingItemsProvider + VirtualizingCollection<SearchResult>; exposes TotalCount
  -> EverythingItemsProvider (EverythingToolbar.App/Search/EverythingItemsProvider.cs)
     IItemsProvider<T>: FetchCount -> IEverythingClient.QueryCount*, FetchRange -> QueryRange*,
     TryFetchCachedFirstPage -> TryReadCachedFirstPage
  -> IEverythingClient (EverythingToolbar.Core/Search/IEverythingClient.cs)
  -> EverythingClientRouter (EverythingToolbar.Platform/Search/EverythingClientRouter.cs)
     prefers EverythingPipeClient (SDK3, Everything3.dll, instance "1.5a" fallback),
     falls back to EverythingIpcClient (SDK2, Everything64.dll, >= 1.4.1)
  -> Everything 1.5a
```

Zero results is detectable at `IEverythingClient.QueryCount*` (returns 0) and at `SearchSession.TotalCount == 0`.
No fuzzy/typo logic exists anywhere upstream today.

## Architecture

Everything stays authoritative: index, query grammar, filters, properties, sorting, result retrieval, filesystem truth.
Our layer owns only: token vocabulary, typo candidates, aliases, fallback query construction, correction metadata.

```
user query (raw, preserved)
   |
   +--> SafeLiteralClassifier   (EverythingToolbar.FuzzySearch)  — which terms may be corrected
   +--> AliasStore              (EverythingToolbar.FuzzySearch)  — explicit alternatives, not rewrites
   +--> ITypoCandidateProvider  (EverythingToolbar.FuzzySearch)  — SymSpell over filename vocabulary
   |
   v
TypoFallbackClient (EverythingToolbar.App/Search)  — IEverythingClient decorator around the router
   |
   +--> QueryCount(raw):  raw count >= 1 -> return raw behaviour untouched
   |                      raw count == 0 and fallback allowed -> plan fallback query, count it,
   |                      remember mapping raw -> active (thread-safe), report correction
   +--> QueryRange(raw):  use remembered active query for this raw query (deterministic planner:
   |                      same raw query always plans the same fallback, so mappings are recomputable)
   +--> TryReadCachedFirstPage: same mapping rule
   v
EverythingClientRouter -> SDK3 pipe / SDK2 IPC   (unchanged)
```

Raw query wins. Fallback fires only when the raw query returned zero results. If the user typed an
exact real token (`form` vs `from`), the raw query matched something, so fallback never runs.

## Components (new)

New project `EverythingToolbar.FuzzySearch` (pure logic, no WPF, inherits repo TFM; added to sln):

| Component | File | Responsibility |
|---|---|---|
| `SafeLiteralClassifier` | `SafeLiteralClassifier.cs` | Whitespace/quote-aware tokenizer; decides per term whether it is a correctable plain filename/path literal. |
| `AliasStore` | `AliasStore.cs` | Deterministic alias lookup; JSON file in the ConfigDir; raw match always preferred. |
| `FilenameTokenizer` | `FilenameTokenizer.cs` | Filename/path -> tokens (spaces, `-`, `_`, dots, CamelCase, letter↔digit boundaries). |
| `TokenVocabulary` | `TokenVocabulary.cs` | normalized form -> (display casing, frequency); thread-safe; compact. |
| `ITypoCandidateProvider` | `ITypoCandidateProvider.cs` | `FindCandidates(term, maxResults, ct)`; SymSpell adapter is default impl. |
| `SymSpellCandidateProvider` | `SymSpellCandidateProvider.cs` | NuGet `SymSpell` (MIT, netstandard2.0) over TokenVocabulary. |
| `FallbackQueryPlanner` | `FallbackQueryPlanner.cs` | raw query + zero results -> corrected query; never touches unsafe tokens. |
| `EditDistancePolicy` | `EditDistancePolicy.cs` | len <= 3: no correction; 4–7: max distance 1; >= 8: max 2. Tunable, evidence first. |

New project `EverythingToolbar.FuzzySearch.Tests` (xUnit), added to sln. Pure components need no Everything.

### Query safety (conservative v1)

A term is correctable only when ALL hold:

- plain `[A-Za-z0-9_-]`, not starting with `-` or a digit;
- no `:` (property/function/macro), no wildcards `*?`, no regex chars, not quoted, no `< > |` grouping, no path separators;
- not `AND` / `OR` / `NOT` (case-insensitive);
- not pure numeric/date/size-looking;
- term length >= 4;
- whole-query regex mode (match flag or `regex:` usage) -> no correction at all;
- negated terms left unchanged.

Uncertain -> leave unchanged. Everything behaviour outranks correction coverage.

### Vocabulary source (Everything-native, no crawler)

1. Learn tokens from every materialized result batch (name + path of rows the UI actually shows). Cheap, no extra queries.
2. Idle-time bootstrap/refresh: a small service connects through the same SDK3 pipe (own `Everything3_ConnectW` client, same instance resolution) and samples broad queries in small viewports at low priority; `Everything3_IsResultListChange` is deliberately NOT used in v1 — per-query result lists here are ephemeral and change-tracking wiring is riskier than a cheap periodic refresh. Documented trade-off, revisit later.
3. No filesystem watcher, no second index, no path database. Word+frequency only.

### Ranking

Fallback only runs on zero raw results, so exact/raw cannot be displaced. Within fallback results,
Everything's sort stays authoritative; our ordering only affects candidate choice (distance, then
local frequency). If the user picked an explicit Everything sort, we never reorder results ourselves.

## Settings + UX (minimal)

- `IToolbarSettings.IsTypoTolerantSearchEnabled` (Config.Net `[Option]`, default true) in `ToolbarSettings.cs`; toggle on `EverythingToolbar/Settings/Search.xaml` following the `SettingItem` card pattern.
- Alias editor: multiline TextBox (one `wrong=right` per line) persisted to `aliases.json` in the ConfigDir.
- Correction hint: subtle one-line "Showing results for `<correction>`" near the results count, only when a fallback query is active; driven by correction state exposed from the fallback client through `SearchSession`/`SearchState`.
- Logging via existing NLog: one INFO line per fallback activation (raw query, corrected query, count).

## Testing strategy

- xUnit for: classifier, aliases, tokenizer, vocabulary, candidate selection (representative typos incl. the 8 required cases), planner, exact/raw protection (`form` vs `from`), advanced-syntax preservation (`ext:pdf`, `size:>100mb`, `!draft`, `<foo|bar>`, `regex:...`, quoted paths, wildcards).
- Integration tests (separate, live-machine only): create uniquely named temp files, poll Everything, verify raw + typo + advanced queries through the real pipe client, delete only our files.
- Perf: micro-benchmarks run locally (candidate lookup p50/p95/p99, vocabulary build time/RAM, warm end-to-end). Targets: candidate p50 < 5 ms, p95 < 15 ms; warm end-to-end < 30–50 ms to first page.

## Commit plan (atomic, each builds + passes its tests)

1. `docs: define typo-tolerant Everything search design` — this document.
2. `test: add fuzzy search core regression harness` — FuzzySearch + Tests projects wired into sln, first failing tests.
3. `feat: classify safe literal Everything query terms`
4. `feat: add preferred spelling aliases`
5. `feat: build filename spelling vocabulary`
6. `feat: add typo candidate lookup` (SymSpell adapter + all typo cases)
7. `feat: plan typo fallback queries`
8. `feat: integrate typo fallback search` (decorator + DI wiring, cancellation/paging/sort preserved)
9. `feat: rank typo fallback results` (candidate ordering; no raw displacement)
10. `feat: refresh spelling vocabulary from Everything` (result-batch learning + idle refresh)
11. `feat: add typo search settings and correction hint`
12. `perf: optimize typo candidate search` (+ SymSpell vs frizbee decision note)
13. `test: cover Everything 1.5a typo search integration`
14. `docs: document typo-tolerant search`

## Risks / constraints

- Signing (`.snk`) + `GenerateDocumentationFile` + `EnforceCodeStyleInBuild` + CSharpier apply to new code too.
- `VirtualizingCollection` first-page cache assumes Everything owns the result set; the decorator must keep one query identity per raw query so cache/viewport reuse stays consistent.
- Router retries every 5 s; decorator must not break the retry loop (it wraps the router, not each client).
- SDK2 fallback path gets the same decorator semantics via `IEverythingClient`.
- GPL code (QuickLook etc.) must not be copied. SymSpell is MIT. frizbee is benchmark-only.
