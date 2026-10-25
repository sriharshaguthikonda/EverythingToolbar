# Everything 1.5 → EverythingToolbar Parity Matrix

Reference application: voidtools Everything **1.5.0.1396a** (installed, running elevated, `C:\Program Files\Everything 1.5a`).
Latest upstream 1.5: **1.5.0.1424b** (differs from installed; feature deltas from 1.5a upgrade guide, forum t=17663). Do not upgrade the installed backend without owner approval.

Reference evidence sources:
- Official docs: Using Everything, Results, Properties, Command Line Options (voidtools.com, fetched 2026-10-25)
- Installed app behaviour (1.5.0.1396a)
- SDK headers in repo: `EverythingSDK3/include/Everything3.h` (IPC), `EverythingSDK/include/Everything_IPC.h` (2.x fallback)

Status values: **Full** / **Partial** / **Missing** / **Delegated** (backend owns it; we surface it) / **Blocked**.
Current-implementation evidence cites fork source (branch `fix/typo-vocabulary-bootstrap` @ 500e3b5).

## 1. Search entry and state

| Feature | Reference behaviour | Version | Current implementation | Status | Backend interface | Prior art | Work required |
|---|---|---|---|---|---|---|---|
| Search box instant search | Type → instant results | all | `Controls/SearchBox.xaml` → `SearchState.SearchTerm` → `SearchSession.Rebuild()` (App/Search/SearchSession.cs:172-191) | Full | SDK3 pipe query | existing | — |
| Query history | Dropdown of past searches, persist | all | `App/Services/SearchHistory.cs` | Full | local | existing | — |
| Filters (Audio/Video/Pics/Docs/Exe/Folder) | Predefined match macros | all | `FilterSelector.xaml`, `DefaultFilterProvider`, `EverythingFilterProvider` (AppServices.cs:86-88) | Full | query text macros | existing | — |
| Custom filter bookmarks | User-defined saved filters | 1.4/1.5 | Missing | Missing | local ini | Everything bookmarks; Flow.Launcher? | settings UI + persistence |
| Match case / whole word / match path / regex / diacritics toggles | Search menu checkboxes | all | `SearchState.BuildSearchQuery()` carries 7 flags (`SearchSession.cs:23`) — UI toggles exist for some in settings | Partial | query flags | existing flags, need per-session UI | status-bar indicator row + toggles in expanded window |
| Advanced search dialog | Form-based query builder | 1.5 | Missing | Missing | local | Everything advanced dialog; PowerToys? | form → query text compiler |
| Search operators & functions syntax (size:, dm:, content:, dupe:, etc.) | Full syntax supported by backend | 1.5 | Passthrough (raw query sent verbatim) | Delegated | query syntax | existing | syntax help panel only |
| Typo-tolerant fallback (our enhancement) | n/a (fork feature) | n/a | `TypoFallbackClient` decorator + planner (`App/Search/TypoFallbackClient.cs:19`), distances 1-7, live-verified | Full (ours) | SDK3 pipe + SymSpell | existing | — |

## 2. Results presentation

| Feature | Reference behaviour | Version | Current implementation | Status | Backend interface | Prior art | Work required |
|---|---|---|---|---|---|---|---|
| Details view (multi-column, sortable headers) | Name/Path/Size/Date Modified/… columns, click header sorts | 1.5 | `Controls/SearchResultsDataView.xaml` (new, commit 1b40717): virtualized GridView (Name/Path/Size/Modified/Created/Ext/Type), header-click sort cycling via `SortCycle` → `SearchState.SetSort`; backend requests 9 properties (e9938d6) and sends sort | Partial — implemented, live elevated check pending | SDK3 result properties (EVERYTHING3 IPC) | Lertaro `App/Views/Controls/Results/ResultsControl*` (MIT, REUSE-grade grid machinery); Files `DataGridHeader.xaml.cs` (MIT) | live acceptance; dynamic column chooser; multi-sort (secondary) |
| Column chooser (right-click header) | Show/hide columns | 1.5 | Missing | Missing | local + SDK properties | Files | column visibility model + menu |
| Multi-sort (secondary/tertiary) | 1.5: -secondary-sort etc. | 1.5b | Missing | Missing | SDK3 sort | — | sort spec in query |
| List view / thumbnails | Icon list; thumbnail pane | 1.5 | Missing (icon list only) | Partial | shell thumbnails | SearchLight thumbnails; Files | thumbnail extractor adapter (IShellItemImageFactory) |
| Result count + timing in status bar | "N objects (M ms)" | all | TotalCount exposed (`SearchSession.cs:44`), minimal display | Partial | SDK count | existing | status bar with counts, timing, active search-option indicators (CASE/WORD/PATH/REGEX) |
| Status bar option indicators | CASE etc. shown, double-click to toggle | 1.5 | Missing | Missing | local | — | bind to SearchState flags |
| Selection: multi-select, Ctrl/Shift | Standard | all | Partial (keyboard nav exists: MoveUp/Down/PageUp/PageDown `SearchSession.cs:95-142`) | Partial | local | Flow.Launcher, Files | extended selection model |
| Drag & drop results out | Drag file(s) to Explorer | all | exists upstream (retained) | Full | shell OLE | existing | — |
| Preview pane | IPreviewHandler host | 1.5 | `SearchResultPreviewPane.xaml` exists (upstream) — verify handler hosting | Partial | shell | existing + SearchLight IPreviewHandler | verify/extend to full preview pane in expanded window |

## 3. Result actions

| Feature | Reference behaviour | Version | Current implementation | Status | Backend interface | Prior art | Work required |
|---|---|---|---|---|---|---|---|
| Open / Open path | Double-click opens; Open file location | all | `SearchResultActions.cs` (App/Search) | Full | shell | existing | — |
| Native context menu (shell IContextMenu) | Full Explorer menu incl. extensions | all | `Platform/Shell/ShellContextMenuService.cs` (commit a818812, adapted from Lertaro MIT): full menu on right-click/Shift+F10 in expanded window, acts on whole selection (Extended multi-select), owner-draw subclassing, failures logged. Popup keeps quick-actions | Partial — implemented, live check pending | shell IContextMenu | Lertaro `App/services/ShellMenu/Presenter/ShellContextMenuHelper.cs` (MIT, REUSE-grade) | live acceptance; drag-drop context menu |
| Copy name / copy path | Edit menu | all | exists (quick actions) | Full | local | existing | — |
| Rename (single) | inline/field dialog | all | Missing | Missing | shell | Files | — |
| Advanced Rename / batch rename with preview | 1.5 dialog (-copyto/-moveto/-rename) | 1.5 | Missing | Missing | shell IFileOperation + local pattern engine | PowerToys PowerRename (MIT) | ADAPT PowerRename pattern engine or invoke Everything.exe -rename on selection |
| Delete / move / copy with progress + undo | Explorer-consistent ops | all | delete exists via shell? verify | Partial | IFileOperation | Files | IFileOperation wrapper with progress + Explorer undo |
| File properties dialog | shell properties | all | exists? verify | Partial | shell | existing | — |
| Find duplicates (dupe: search) | Tools menu | 1.5 | Delegated — dupe: syntax passes through | Delegated | query syntax | — | menu item that sets dupe: query |

## 4. Windows, tabs, panes

| Feature | Reference behaviour | Version | Current implementation | Status | Backend interface | Prior art | Work required |
|---|---|---|---|---|---|---|---|
| Compact taskbar popup | existing | all | `SearchWindow.xaml` + `TaskbarWindow.xaml` | Full | — | existing | preserve |
| Expanded full window (menu bar layout) | Everything main window | 1.5 | `ExpandedSearchWindow.xaml` (new, commit 1b40717): normal resizable Window, menu bar (File/View/Search/Help), SearchBox + FilterSelector, SearchResultsDataView + collapsible preview pane, status bar (count/busy/fallback-hint/option chips), keyboard set (Esc/Ctrl+F/Ctrl+P/Ctrl+Shift+C|W|U|R/E). Consumes the SAME SearchSession singleton; compact popup untouched. `ISearchWindowController` gained ShowExpanded/ToggleExpanded | Partial — implemented, live elevated check pending | local | Lertaro layout patterns (MIT) | tabs inside expanded window; folder/filter sidebars; bookmark menu; resx localisation of hardcoded strings |
| Tabs | Ctrl+T, per-tab query/filter/sort | 1.5 | Implemented (commit f37149b): tab strip in expanded window, per-tab snapshot (query/filter/effective sort/10 flags) swapped through the single SearchState — one pipeline, no per-tab sessions; tabs.json versioned/atomic in %APPDATA%; Ctrl+T/W/Tab; close-last clears | Partial — implemented, live check pending | local | Lertaro absent (report) → native design | per-tab match-flag isolation (flags currently ride global settings.ini, same as sort override); preview/filter state per tab |
| Folder sidebar (tree) | 1.5 folder pane | 1.5 | Missing | Missing | SDK3 folder-size/paths? or shell tree | Files, Lertaro | investigate SDK3 path-listing support first |
| Filter/bookmark sidebar | left panes | 1.5 | Missing | Missing | local | existing FilterSelector | extend into sidebar |
| Menu bar (File/Edit/View/Search/Bookmarks/Tools/Help) | standard | all | Missing | Missing | local | standard WPF Menu | build with commands wired to session |
| Keyboard shortcuts (Ctrl+N/T/W, F3, F12…) | full set | all | partial (hotkeys for popup) | Partial | local | — | InputBindings + menu accelerators |
| Multi-monitor / DPI | popup + window behave | all | upstream handles | Partial | local | existing | verify in acceptance |

## 5. Bookmarks, sessions, tools

| Feature | Reference behaviour | Version | Current implementation | Status | Backend interface | Prior art | Work required |
|---|---|---|---|---|---|---|---|
| Search bookmarks (name+query+filter+sort) | Bookmarks menu, bookmark bar | all | Missing | Missing | local ini | Everything format | bookmark store + menu + bar |
| Sessions (saved search states) | 1.5 sessions | 1.5 | Missing | Missing | local | — | JSON session store |
| Export results (CSV/TXT/EFU) | File > Export | all | Missing | Missing | SDK rows local export or Everything.exe -export | es.exe export behaviour | export writer from result stream |
| Find Windows tools (Index Journal, File List Editor) | Tools menu | 1.5 | Missing | Delegated | launch Everything.exe flags | — | menu items launching backend tools |
| Settings: backend (index, NTFS, exclusions, plugins, hotkeys) | Everything options | 1.5 | Delegated — open Everything settings window | Delegated | Everything.exe -options | — | menu item + verify elevation story |
| Settings: toolbar-side (existing fork settings incl. typo distances) | n/a fork | n/a | full settings window + settings search (PR #2) | Full (ours) | local | existing | — |

## 6. Backend ownership notes

- Everything owns: indexing, NTFS/ReFUS monitoring, property indexing, exclusions, plugins, server mode. EverythingToolbar must never build a second index (brief §14). These stay **Delegated** with launch-into-Everything menus.
- SDK3 IPC (pipe `1.5a` instance) currently used for: query, count, first-page cache, 5 properties, viewport paging (`EverythingPipeClient.cs`). Gap to close for parity: property enumeration (`EVERYTHING3` property list request), per-property sort requests, larger property fetch per row, optional live-update notifications.
- Elevation: pipe ACLs reject non-elevated clients (verified 2026-09; memory + live tests). Expanded window runs inside existing Launcher elevation.

## 7. Verification plan per feature (summary)

Real Windows UI acceptance per brief §12: measure with live elevated Everything 1.5.0.1396a; destructive ops only on generated test fixtures; every status above moves Missing→Partial→Full only with a passing check recorded in this matrix's Result column.

## Result log

| Date | Feature | Result |
|---|---|---|
| 2026-10-25 | (baseline) | Matrix created; 186/186 FuzzySearch tests green @ 500e3b5 |
| 2026-10-25 | Details backend (4 extra properties) | Commit e9938d6: Extension/Type/DateCreated/Attributes requested + mapped, 11 unit tests, 197/197 green. P/Invoke signatures verified against Everything3.h:1070-1078. Live elevated check pending (pipe ACL rule) |
| 2026-10-25 | Bundled Everything3.dll provenance | 171/171 exports are `Everything3_*` matching voidtools MIT header (174 header decls incl. 3 non-export helpers); locally built, no version resource. No unexpected exports |
| 2026-10-25 | SDK3 filter/bookmark enumeration | NOT exposed by Everything3.h — bookmark/filter surfaces stay local-store or CSV import (EverythingFilterProvider FileSystemWatcher pattern) |
| 2026-10-25 | Prior-art sweep (Phase C) | Full report committed: `docs/Research/prior-art-expanded-ui.md`. Picks: Lertaro (MIT) grid+preview+shell-menu REUSE; Flow.Launcher (MIT) context-menu verb helper; Files MIT files only (`ShellFileOperations2.cs` for IFileOperation+undo) — repo is MIT/MPL-2.0 split (~233 MPL files), SidebarViewModel is MPL; QuickLook GPL-3.0 → reference-only, never copy; SearchLight MIT unverified (no LICENSE file) → verify before shipping |
| 2026-10-25 | Expanded window + details table (Phase D slice 1+2) | Commit 1b40717 (11 files, +828): ExpandedSearchWindow, SearchResultsDataView (virtualized, sort cycling), SortCycle, controller ShowExpanded/ToggleExpanded, AppServices registration. 202/202 tests, XamlStyler PASS, csharpier clean. Known simplification: SearchState.SetSort persists sort into settings ini (shared single session) — revisit if per-tab sort lands with tabs |
