# Typo-tolerant search — user guide (fork feature)

Everything stays the search engine. This fork adds a spelling layer that only appears when a
search returns **zero** results. Design and audit trail: `docs/typo-tolerant-search-design.md`,
prior-art record: `docs/Research/landscape.yaml`.

## How it works

1. Your query runs in Everything exactly as typed. If it matches anything, nothing changes.
2. Only on zero results: terms that are plain filename words (no `:`, wildcards, quotes, paths,
   operators, negations; >= 4 chars) are looked up in a SymSpell index built from a filename-token
   vocabulary learned from Everything itself.
3. Explicit aliases/preferred spellings are checked first, then typo candidates (edit distance,
   then local file-name frequency). Raw text is never deleted — it stays inside the query as an
   OR group (`neurosicence` becomes `<neurosicence|neuroscience>`).
4. Everything re-runs the corrected query. The tab bar shows a subtle
   `Showing results for: <corrections>` hint. One NLog INFO line records the activation.

## Required typo cases (all green in tests)

neurosicence→neuroscience, docuemnt→document, attachement→attachment, repomsp→repomaps,
powertyo→powertoy, guthikodna→Guthikonda, ollma→ollama, kanataa→kanata.

`repomsp` needs two edits (missing `a` + transposition) at length 7 — this drove the distance
policy: nothing shorter than 4 chars is ever corrected; everything else allows distance 2, with
SymSpell's closest-match verbosity plus the zero-result gate keeping precision. Exact tokens
(`form` vs `from`) can never be displaced because a matching raw query never triggers fallback.

## Settings (Settings > Search)

- **Typo-tolerant search** — on/off (default on). Off = byte-for-byte stock behaviour.
- **Preferred spellings** — one rule per line: `canonical = alias1, alias2`
  (e.g. `repomaps = repo map, repo-map`). Saved to `aliases.json` in the config directory and
  hot-reloaded. A whole query that equals an alias is replaced by its canonical form; single-word
  aliases expand into the OR group next to the raw term. Multi-word canonicals apply only as
  whole-query aliases.

## Vocabulary (Everything-native, no second index)

Word+frequency list only — no path database, no filesystem watcher, no crawler.
Sources: every result page the UI materializes, plus an idle sampler that reads five 256-row
pages at random offsets straight from the Everything index (startup + every 6 h). The SymSpell
index rebuilds lazily on first use and in the background every 2000 learned paths.
Everything3 result-list change tracking was evaluated and deliberately not used: per-query result
lists here are ephemeral, so a cheap periodic refresh is the lower-risk route.

## Measured performance (this machine, synthetic 50k-word vocabulary)

| Metric | Value | Target |
|---|---|---|
| vocabulary build (50k paths) | 208 ms | — |
| SymSpell index rebuild | 883 ms (background only) | — |
| candidate lookup p50 | 0.010 ms | < 5 ms |
| candidate lookup p95 | 0.030 ms | < 15 ms |
| candidate lookup p99 | 0.044 ms | — |

Frizbee (SIMD Rust matcher) was benchmarked as an alternative and not adopted: SymSpell already
answers two orders of magnitude under target, so native packaging, build complexity and arch
matrix bring no material benefit. Harness: `FUZZY_BENCH=1 dotnet test --filter Category=Benchmark`.

## CI / installer (this fork)

Canonical repo: `sriharshaguthikonda/EverythingToolbar`, default branch `main`
(upstream `srwi/EverythingToolbar` stays as the `upstream` remote; never force-pushed).

GitHub Actions (`build.yml` -> `_build.yml`) on every push/PR/dispatch:

1. NuGet restore + full MSBuild Release build per arch (x64 + ARM64) — the hosted
   `windows-2025`/`windows-11-arm` runners carry a Roslyn new enough for CsWin32 0.3.335, so
   `EverythingToolbar.Platform` compiles normally and `FuzzyManagedOnlyBuild` stays a
   local-dev-only escape hatch.
2. `EverythingToolbar.FuzzySearch.Tests` runs via `dotnet test -p:FuzzyManagedOnlyBuild=true`
   (reuses the MSBuild-built native DLLs); failures fail the run. The live Everything test is
   env-gated and skips in CI — run it on a machine with Everything 1.5a (see below).
3. On `main` pushes and manual dispatches the Inno Setup x64/ARM64 installers compile and upload
   as artifacts named `EverythingToolbar-Installer-<arch>-<full-commit-sha>`; the exe inside is
   `EverythingToolbar-<arch>-<full-commit-sha>.exe` (traceable to the exact commit).
4. Release workflow unchanged: tagged builds sign via the `PFX_CERTIFICATE_FILE` secret; a fork
   without that secret simply skips signing (`sign: false` path) and never fails main CI.

Installer defaults to **Launcher** mode on Windows 11. On upgrades the deskband auto-resume now
requires the deskband COM server DLL to actually exist at its registered path — a stale CLSID
registration from a removed StartAllBack-era install can no longer silently pick Deskband
(`/mode=launcher` also forces Launcher explicitly).

## Upstream sync flow

```bash
git fetch upstream
git log --oneline main..upstream/develop   # review deliberately
git checkout main && git merge upstream/develop   # or cherry-pick; keep typo-search changes
git push origin main                        # CI re-validates everything
```

## Build & test

```powershell
# Native SDKs (needs VS/MSBuild.exe; once):
MSBuild EverythingToolbar.sln -t:Restore -p:Configuration=Debug -p:Platform=x64
MSBuild EverythingToolbar.sln -p:Configuration=Debug -p:Platform=x64 -p:SignAssembly=false

# Managed chain (dotnet CLI; CsWin32 0.3.335 needs Roslyn >= 4.11, VS 17.10 ships 4.9):
dotnet build EverythingToolbar/EverythingToolbar.csproj -p:FuzzyManagedOnlyBuild=true -p:SignAssembly=false -p:Platform=x64

# Unit tests (no Everything required):
dotnet test EverythingToolbar.FuzzySearch.Tests/EverythingToolbar.FuzzySearch.Tests.csproj -p:SignAssembly=false -p:FuzzyManagedOnlyBuild=true -p:Platform=x64

# Live integration (needs Everything 1.5a running; elevated shell on this machine):
LIVE_EVERYTHING=1 dotnet test ... --filter Category=LiveEverything
```

`-p:FuzzyManagedOnlyBuild=true` (this fork) reuses the MSBuild.exe-built `Everything3.dll`/
`Everything64.dll` and skips evaluating the .vcxproj references, which the dotnet CLI cannot
load. Inert without the flag; CI and Visual Studio builds are unaffected. Launcher/Deskband
still require MSBuild.exe (COM reference resolution).

## Compatibility & limits

- Everything 1.5.0.1396a x64 (installed: `C:\Program Files\Everything 1.5a`), instance `1.5a`;
  SDK2 fallback (Everything >= 1.4.1) gets the same decorator semantics.
- An **elevated** Everything owns its SDK3 pipe with an admin-only ACL: non-elevated clients get
  access denied (Windows integrity boundary — unchanged upstream behaviour, not fixable without
  UAC changes). Run the toolbar/test elevated in that setup.
- Regex-mode queries are never corrected. Negations, quoted text, property/size/date filters,
  wildcards and paths pass through byte-exact (24+ classifier tests).
- Result ordering is always Everything's (explicit user sorts are never overridden).
- Multi-word canonical aliases do not expand in term position (whole-query only) — v1 limit.
- No GPL code was copied: SymSpell (MIT), everything else is fork-original or upstream.

## Upstream notes

Base: `srwi/EverythingToolbar` `develop` @ `4752441`. Branch `feature/typo-tolerant-search`,
local commits only, not pushed. Fork delta: new `EverythingToolbar.FuzzySearch` +
`.Tests` projects, one decorator (`TypoFallbackClient`), one refresher
(`VocabularyRefresher`), settings/UX additions, two csproj conditions, `.gitattributes`.
