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
| Advanced search dialog | Form-based query builder | 1.5 | Implemented (commit e180861): Search > Advanced Search… (Ctrl+Shift+A), themed form dialog (filename/path/extension/size range/dm+dc date ranges/attributes) composed by a pure builder into Everything syntax; all-empty disabled; 29 composer tests | Partial — implemented, live check pending | local | native (Everything advanced dialog as shape reference) | unit selector for sizes; field persistence; "everything else" advanced options |
| Search operators & functions syntax (size:, dm:, content:, dupe:, etc.) | Full syntax supported by backend | 1.5 | Passthrough (raw query sent verbatim) | Delegated | query syntax | existing | syntax help panel only |
| Typo-tolerant fallback (our enhancement) | n/a (fork feature) | n/a | `TypoFallbackClient` decorator + planner (`App/Search/TypoFallbackClient.cs:19`), distances 1-7, live-verified | Full (ours) | SDK3 pipe + SymSpell | existing | — |

## 2. Results presentation

| Feature | Reference behaviour | Version | Current implementation | Status | Backend interface | Prior art | Work required |
|---|---|---|---|---|---|---|---|
| Details view (multi-column, sortable headers) | Name/Path/Size/Date Modified/… columns, click header sorts | 1.5 | `Controls/SearchResultsDataView.xaml` (new, commit 1b40717): virtualized GridView (Name/Path/Size/Modified/Created/Ext/Type), header-click sort cycling via `SortCycle` → `SearchState.SetSort`; backend requests 9 properties (e9938d6) and sends sort | Partial — implemented, live elevated check pending | SDK3 result properties (EVERYTHING3 IPC) | Lertaro `App/Views/Controls/Results/ResultsControl*` (MIT, REUSE-grade grid machinery); Files `DataGridHeader.xaml.cs` (MIT) | live acceptance; dynamic column chooser; multi-sort (secondary) |
| Column chooser (right-click header) | Show/hide columns | 1.5 | Implemented (1eeab0a): header right-click checkable menu, 6 persisted Config.Net options (Name fixed), width-collapse with saved-width restore (min 40), Reset Columns; right-click can never sort | Partial — implemented, live check pending | local | native | — |
| Multi-sort (secondary/tertiary) | 1.5: -secondary-sort etc. | 1.5b | Implemented (53627c0): SearchQuery.SecondarySorts (≤2), pipe client issues repeated Everything3_AddSearchSort; SearchState SetSecondarySort/ClearSecondarySorts (list replaced never mutated — result-set reuse preserved); tab snapshots carry them; View > Sort By gains Secondary/Tertiary + Clear sections. Session-only, not persisted | Partial — implemented, live check pending | SDK3 multi-sort (Everything3.h:980) | native | per-column secondary glyphs; persistence |
| List view / thumbnails | Icon list; thumbnail pane | 1.5 | Details table (icon column) + Thumbnails view (92d1b70): View > Details/Thumbnails radio (persisted), 48px IShellItemImageFactory tiles (async, cached, icon fallback), row virtualization intact. True multi-column grid deferred (needs custom virtualizing panel) | Partial — implemented, live check pending | shell thumbnails | written from Win32 API contract (no third-party code) | multi-column virtualizing thumbnail grid; size options |
| Drag & drop results out | Drag file(s) to Explorer | all | Popup had it; details table now too (4005399): multi-select FileDrop drag after popup's threshold pattern | Full | shell OLE | existing | — |
| Rename (single) | inline/field dialog | all | Implemented (b0fbab0): F2 → themed dialog → IFileOperation RenameItem (FOF_ALLOWUNDO); pure WindowsPathValidator gates invalid names | Partial — implemented, live check pending | shell IFileOperation | Files MIT pattern | Explorer-style extension preselection |
| Advanced Rename / batch rename with preview | 1.5 dialog (-copyto/-moveto/-rename) | 1.5 | BOTH paths shipped: Tools > Advanced Rename… delegates to Everything.exe -rename (622e3fa, D12); Tools > Batch Rename… is a native PowerRename-semantics dialog (604ed7e engine + 511ca30 UI): search/replace/regex/case/match-all/extension/enumerate \${start,increment,padding} counters, debounced virtualized preview, single PerformOperations apply | Partial — implemented, live check pending on FIXTURES ONLY | local engine + shell IFileOperation | Microsoft PowerToys PowerRename (MIT, semantics from src/modules/powerrename/lib) | case-changing ops; import/export rename rules |
| Find duplicates (dupe: search) | Tools menu | 1.5 | Tools > Find Duplicate Names sets dupe: in the active tab (622e3fa) | Full | query syntax | — | — |
| Multi-sort (secondary/tertiary) | 1.5: -secondary-sort etc. | 1.5b | Missing | Missing | SDK3 sort | — | sort spec in query |
| List view / thumbnails | Icon list; thumbnail pane | 1.5 | Missing (icon list only) | Partial | shell thumbnails | SearchLight thumbnails; Files | thumbnail extractor adapter (IShellItemImageFactory) |
| Result count + timing in status bar | "N objects (M ms)" | all | Implemented (5a23d77): StatusBarText.CountAndLatency helper; count fetch timed at the EverythingItemsProvider seam → SearchSession.LastQueryMilliseconds → status bar. 622e3fa added the Tools menu (Everything / Advanced Rename via -rename / dupe:); 4580890 Copy Name/Path variants; c578ef8 + b0fbab0 + 511ca30 Edit-ops/rename/batch-rename (see rows below); ff9d636 sleep/hibernate recovery (issue #4) | Partial — implemented, live check pending | SDK count + timing seam | existing | — |
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
| Delete / move / copy with progress + undo | Explorer-consistent ops | all | Implemented (commit c578ef8): Edit menu (Cut/Copy/Paste/Delete) + gestures (Ctrl+X/C/V, Del) on selection via minimal IFileOperation (adapted from Files Community MIT) — delete = FOF_ALLOWUNDO with shell confirm left on (recycle bin + Explorer Edit>Undo, matching Everything's delegated-undo docs); clipboard CF_HDROP + Preferred DropEffect (pure codec). Copy-path moved to Ctrl+Shift+C, Match Case now Ctrl+I | Partial — implemented, live check pending (destructive ops on fixtures only) | shell IFileOperation + OLE clipboard | Files `ShellFileOperations2.cs` (MIT, REUSE) | progress sink UI (shell native dialog used v1); rename op |
| File properties dialog | shell properties | all | exists? verify | Partial | shell | existing | — |
| Find duplicates (dupe: search) | Tools menu | 1.5 | Delegated — dupe: syntax passes through | Delegated | query syntax | — | menu item that sets dupe: query |

## 4. Windows, tabs, panes

| Feature | Reference behaviour | Version | Current implementation | Status | Backend interface | Prior art | Work required |
|---|---|---|---|---|---|---|---|
| Compact taskbar popup | existing | all | `SearchWindow.xaml` + `TaskbarWindow.xaml` | Full | — | existing | preserve |
| Expanded full window (menu bar layout) | Everything main window | 1.5 | `ExpandedSearchWindow.xaml` (new, commit 1b40717): normal resizable Window, menu bar (File/View/Search/Help), SearchBox + FilterSelector, SearchResultsDataView + collapsible preview pane, status bar (count/busy/fallback-hint/option chips), keyboard set (Esc/Ctrl+F/Ctrl+P/Ctrl+Shift+C|W|U|R/E). Consumes the SAME SearchSession singleton; compact popup untouched. `ISearchWindowController` gained ShowExpanded/ToggleExpanded | Partial — implemented, live elevated check pending | local | Lertaro layout patterns (MIT) | tabs inside expanded window; folder/filter sidebars; bookmark menu; resx localisation of hardcoded strings |
| Tabs | Ctrl+T, per-tab query/filter/sort | 1.5 | Implemented (commit f37149b): tab strip in expanded window, per-tab snapshot (query/filter/effective sort/10 flags) swapped through the single SearchState — one pipeline, no per-tab sessions; tabs.json versioned/atomic in %APPDATA%; Ctrl+T/W/Tab; close-last clears | Partial — implemented, live check pending | local | Lertaro absent (report) → native design | per-tab match-flag isolation (flags currently ride global settings.ini, same as sort override); preview/filter state per tab |
| Folder sidebar (tree) | 1.5 folder pane | 1.5 | Implemented (commit f186153): collapsible sidebar (Ctrl+B, persisted), lazy tree — roots = ready drives, expansion runs `parent:"..." folder:` per node through the DI client (double-load/stale guards), click navigates via `parent:"..."`. Classifier pass-through pinned by test | Partial — implemented, live check pending | SDK3 query (`parent:` syntax; NO tree IPC in Everything3.h — verified) | native design | icons; filter/bookmark sidebars (same pattern) |
| Filter/bookmark sidebar | left panes | 1.5 | Missing | Missing | local | existing FilterSelector | extend into sidebar |
| Menu bar (File/Edit/View/Search/Bookmarks/Tools/Help) | standard | all | Missing | Missing | local | standard WPF Menu | build with commands wired to session |
| Keyboard shortcuts (Ctrl+N/T/W, F3, F12…) | full set | all | partial (hotkeys for popup) | Partial | local | — | InputBindings + menu accelerators |
| Multi-monitor / DPI | popup + window behave | all | upstream handles | Partial | local | existing | verify in acceptance |

## 5. Bookmarks, sessions, tools

| Feature | Reference behaviour | Version | Current implementation | Status | Backend interface | Prior art | Work required |
|---|---|---|---|---|---|---|---|
| Search bookmarks (name+query+filter+sort) | Bookmarks menu, bookmark bar | all | Implemented (commit f88866b): Bookmarks menu in expanded window, Add Bookmark… (Ctrl+D, themed dialog prefilled with query), alphabetical items run into active tab (term+filter+sort, flags untouched), Remove submenu, (no bookmarks) placeholder; bookmarks.json versioned/atomic/corrupt-safe | Partial — implemented, live check pending | local (SDK3 has NO bookmark API — verified) | native design mirroring SearchTabStore | bookmark bar; organize dialog; import Everything's own bookmarks (ini parse) |
| Sessions (saved search states) | 1.5 sessions | 1.5 | Implemented (commit bfa5ae5): File > Sessions — Save Session… captures the whole tab set (deep-copied incl. active snapshot), alphabetical restore (destructive replace via SearchTabManager.ReplaceAll, persisted to tabs.json), Delete submenu; sessions.json versioned/atomic/corrupt-safe with filter re-resolution on load | Partial — implemented, live check pending | local | native design (mirrors tabs store) | import Everything's own sessions; session auto-save |
| Export results (CSV/TXT/EFU) | File > Export | all | Implemented (commit d5879e7): File > Export… (Ctrl+S) streams the active query's full set via the DI client (1000/page, status-bar progress, Esc cancels). CSV = header + metadata columns (sortable local dates); TXT = one path per line; EFU = voidtools layout (FILETIME dates, empty-never-zero unknowns). Pure streaming writers unit tested incl. fake-client paging + cancellation | Partial — implemented, live check pending | SDK3 paging (existing) | native (es.exe export behaviour as reference) | CSV column chooser; export-dialog options (Everything's full export dialog) |
| Find Windows tools (Index Journal, File List Editor) | Tools menu | 1.5 | Missing | Delegated | launch Everything.exe flags | — | menu items launching backend tools |
| Settings: backend (index, NTFS, exclusions, plugins, hotkeys) | Everything options | 1.5 | Delegated — Tools > Everything opens the backend main window (622e3fa). Standalone `-options` flag verified ABSENT from voidtools command-line docs (2026-10-25), so no options-window item; Everything.exe with no args shows the window whose Tools menu opens Options | Delegated | Everything.exe launch (path setting + browse fallback) | — | — |
| Sleep/hibernate search recovery | Results recover after resume | n/a (fork issue #4) | Implemented (ff9d636): PowerModeChanged(Resume) → InvalidateForResume() (idempotent DisconnectLocked path) + 1s grace + best-effort reconnect + session rebuild on UI thread; per-keystroke paths stay sleep-free | Partial — implemented, real sleep/wake acceptance pending | SDK3 pipe lifecycle | — | — |
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
| 2026-10-25 | Session 4 ("don't stop until all features are built"): nine more features | 5a23d77 status timing ("N objects (M ms)"), 622e3fa Tools menu (-options verified absent, omitted honestly), 4580890 Copy Name/Path, 1eeab0a column chooser, eb616f4 Sort By menu, 4005399 multi-select drag, b0fbab0 F2 rename, ff9d636 sleep recovery (issue #4), 604ed7e+511ca30 batch rename (PowerRename semantics from src/modules/powerrename/lib). Tests 390→434; all committed after supervisor review + independent test runs |
| 2026-10-26 | Session 5 ("all approved" + delegation): D20 multi-sort (53627c0), D21 thumbnails view (92d1b70), D22 per-tab flag isolation (5efd0fd), D23 bookmark bar (bc8f44f), D24 export dialog w/ CSV columns (742158a) | Pushed + PR #2 merged (a26d86b) + main XAML repair (da0b4a6, CI green) + Ghidra 12.1.4/REA native verified + Everything 1424b downloaded+SHA-verified (install blocked on user closing Everything window — Q&A). Tests 434→473. Remaining honest gaps: true multi-column thumbnail grid, case ops in batch rename, Everything-format bookmark/session import, live elevated acceptance |
| 2026-10-25 | Session 2 (by-delegation run): native context menu, tabs, bookmarks, export, folder sidebar | Commits a818812 (IContextMenu from Lertaro MIT, Extended multi-select), f37149b (tab strip + snapshot-swap through single SearchState, tabs.json), f88866b (Bookmarks menu + bookmarks.json), d5879e7 (Export CSV/TXT/EFU, CSV upgraded to metadata columns by supervisor), f186153 (folder sidebar via parent: queries, classifier pass-through pinned). Tests 202→255, all green; xstyler+csharpier clean per commit |
