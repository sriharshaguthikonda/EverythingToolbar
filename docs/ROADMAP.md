# ROADMAP — Everything 1.5 parity inside EverythingToolbar

Mission: near-complete native frontend for Everything 1.5 inside the fork, preserving taskbar UX and typo-tolerant search. Full brief and status detail: `docs/everything-1.5-parity-matrix.md`.

Phases (from the 2026-10-25 run brief):

| Phase | Scope | Status (2026-10-25) |
|---|---|---|
| A Preflight | repo audit, skills (T-REX + REA), baseline build/tests | DONE — branch `fix/typo-vocabulary-bootstrap` @ 500e3b5, 186/186 tests green; T-REX v3.3 installed + hash-verified + first trace done (`.investigations/toolbar-search-pipeline/`); REA 6.8.0 CLI + skill installed, MCP registered for Z Code (needs session restart to surface tools) |
| B Inventory | Everything feature inventory + parity matrix | DONE (first pass) — matrix created, ~45 features across 6 groups |
| C Prior-art | inspect Lertaro, SearchLight, Flow.Launcher, Files, QuickLook, PowerToys | IN PROGRESS — shallow clones + component report (ornith worker) |
| D Expanded UI | menu bar, virtualized result table, property columns, tabs, sidebars, preview, keyboard | NEXT — start with: expanded window shell consuming `SearchSession` + `ISearchWindowController`, then virtualized details table (SDK3 property columns) |
| E Advanced features | bookmarks, sessions, export, batch rename, file ops, advanced search UI | queued |
| F Stability | IPC recovery, sleep/hibernate (issue #4), DPI, perf budgets | queued; issues #3 (file dialog) stays a separate module |

Key constraints (do not re-litigate):
- One search engine: everything goes through `SearchSession` + `IEverythingClient` (TypoFallbackClient decorator). No second index, no second backend client.
- Expanded window must reuse the compact popup's session/state, not fork it.
- SDK3 pipe requires elevation (verified); all live acceptance runs elevated.
- Backend-owned features (index/settings/plugins) stay Delegated — launch Everything, never rebuild.
- PR #2 (head 500e3b5) stays unmerged pending owner GUI acceptance; do not merge PRs or push to GitHub without explicit owner approval (brief §15).
