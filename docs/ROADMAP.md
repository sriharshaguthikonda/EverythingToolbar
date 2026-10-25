# ROADMAP — Everything 1.5 parity inside EverythingToolbar

Mission: near-complete native frontend for Everything 1.5 inside the fork, preserving taskbar UX and typo-tolerant search. Full brief and status detail: `docs/everything-1.5-parity-matrix.md`.

Phases (from the 2026-10-25 run brief):

| Phase | Scope | Status (2026-10-25, session 2) |
|---|---|---|
| A Preflight | repo audit, skills (T-REX + REA), baseline build/tests | DONE — REA MCP live after restart; T-REX invoked (trace in `.investigations/toolbar-search-pipeline/`) |
| B Inventory | Everything feature inventory + parity matrix | DONE + maintained per commit (`docs/everything-1.5-parity-matrix.md`) |
| C Prior-art | inspect Lertaro, SearchLight, Flow.Launcher, Files, QuickLook, PowerToys | DONE — `docs/Research/prior-art-expanded-ui.md` (licence conflicts flagged: QuickLook GPL, Files MIT/MPL split, SearchLight no LICENSE) |
| D Expanded UI | menu bar, virtualized result table, property columns, tabs, sidebars, preview, keyboard, context menu | **5 features landed session 2** (255/255 tests): native shell context menu (a818812), tabs (f37149b), folder sidebar (f186153) + session-1 details table/expanded window (e9938d6, 1b40717). Live elevated acceptance still pending (user UAC) |
| E Advanced features | bookmarks, sessions, export, batch rename, file ops, advanced search UI | bookmarks (f88866b), export (d5879e7), sessions (bfa5ae5), Edit-menu file ops w/ undo (c578ef8), advanced search dialog (e180861) DONE; remaining: batch rename (PowerRename adapt), Index Journal / file list editor launch items |
| F Stability | IPC recovery, sleep/hibernate (issue #4), DPI, perf budgets | queued; issues #3 (file dialog) stays a separate module |

Key constraints (do not re-litigate):
- One search engine: everything goes through `SearchSession` + `IEverythingClient` (TypoFallbackClient decorator). No second index, no second backend client.
- Expanded window must reuse the compact popup's session/state, not fork it.
- SDK3 pipe requires elevation (verified); all live acceptance runs elevated.
- Backend-owned features (index/settings/plugins) stay Delegated — launch Everything, never rebuild.
- PR #2 (head 500e3b5) stays unmerged pending owner GUI acceptance; do not merge PRs or push to GitHub without explicit owner approval (brief §15).
