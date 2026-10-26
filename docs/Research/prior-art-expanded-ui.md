# Prior-Art Report — Reusable C#/WPF Components for an Everything-1.5-style Search Window

**Target (read-only, not inspected for reuse):** `C:/Tools/Search_everything_Levenshtein/EverythingToolbar` (MIT, .NET 8 / `net8.0-windows`, WPF).
**Goal:** find reusable UI components for an expanded Everything-1.5-style window — menu bar, virtualized multi-column result table, tabs, folder/filter/bookmark sidebars, preview pane, native context menus, status bar.
**Staging root:** `C:/Users/deletable/AppData/Local/Temp/parity-staging/`

## Methodology & hard-rules compliance
- All 5 repos were **shallow-cloned** (`git clone --depth 1`) into the staging root. Every clone succeeded (no failures to record).
- Inspection was **source-only**: no build, no installer script execution, no `dotnet build`.
- Nothing under `C:/Tools/Search_everything_Levenshtein/EverythingToolbar` was touched (read-only).
- No git commit / push / branch operations were performed anywhere.
- Only C#/WPF-relevant files were read; search engines/indexers were deliberately not researched.

**Feature key:** a) virtualized multi-column result table · b) tabbed interface · c) preview pane · d) native Windows context menu · e) folder tree / filter sidebar · f) file operations with progress + undo · g) status bar (result counts / timing).

---

## 1. Lertaro — `github.com/Lertaro/Lertaro`
**License:** MIT (`LICENSE` present, "Copyright (c) 2026"). **Framework:** `net10.0-windows`, WPF + WPF-UI. **Nature:** plugin-based launcher with a full preview subsystem and an extensible results grid.

| Item | Verdict | Key files (relative) | Coupling | Recommendation |
|---|---|---|---|---|
| a | **Strong match** | `App/Views/Controls/Results/ResultsControl.xaml` · `App/Views/Controls/Results/ResultsControlColumns.cs` · `App/Views/SearchWindow/SearchWindowColumnActivation.cs` · `App/ViewModels/Search/SearchResultSorter.cs` · `PluginSdk/Abstractions/Plugins/IResultColumnProvider.cs` | Moderate — grid logic in `ResultsControlColumns` is self-contained; column *population* is driven by `PluginManager.ResultColumnProviders` (plugin wiring) | **REUSE** the grid + dynamic-columns + sort-arrow machinery and **ADAPT** the plugin-provider wiring into EverythingToolbar's own result model |
| b | Absent | — (no `<TabItem>`/`TabControl` anywhere in App/Core) | — | **SKIP** |
| c | **Best-match WPF preview host** | `Plugins/CoreExtensions/Preview/Handlers/PreviewHandlerHost.cs` · `.../PreviewHandlerInterop.cs` · `.../PreviewHandlerPool.cs` · `.../ShellPreviewHandlerProvider.cs` · `.../PreviewHandlerRegistry.cs` · also `App/Services/QuickLookManager*.cs`, `Plugins/QuickLookBridge/*` | The `HwndHost` host + interop are self-contained & MIT; pooling rides on the PluginSdk preview service | **REUSE** the `HwndHost` IPreviewHandler host and interop surface (cleanest WPF reference found); **ADAPT** the pool |
| d | **Strong match** | `App/services/ShellMenu/Presenter/ShellContextMenuHelper.cs` | Low — pure Win32/COM P/Invoke, no WinForms dependency; adds menu dedup + window subclassing for owner-draw/tooltips | **REUSE / ADAPT** (drop-in style) |
| e | Partial | `App/Resources/Styles/Windows/SearchWindow.xaml` (`SidebarFilterItemStyle`) — filter sidebar only; **no deep folder tree** | Low | **ADAPT** the filter-item style pattern only; **REIMPLEMENT** any real folder tree |
| f | Absent | — (no `IFileOperation`/`SHFileOperation`) | — | **SKIP** |
| g | Absent | — (launcher, no persistent status bar) | — | **SKIP** |

**Notes:** Uses WPF `ListView` + `GridView` with `VirtualizingPanel.IsVirtualizing="True"` / `VirtualizationMode="Recycling"`, 3 built-in columns (Name/Path/DateModified), and a plugin column API (`IResultColumnProvider` → `ResultColumnDefinition` with `VisibilityPredicate`, `SortComparer`, `OnDoubleClick`). This is the single most directly reusable item-(a) + item-(c) source in the set, and it is MIT.

---

## 2. SearchLight — `github.com/sam-siddiqui/SearchLight`
**License:** **MIT claimed in `README.md` (line 165); NO standalone `LICENSE` file committed.** **Framework:** `net9.0-windows`, WPF + WinForms + WPF-UI. Bundles `lib/Everything64.dll` (the actual Everything engine) — i.e. a complete Everything client.

| Item | Verdict | Key files (relative) | Coupling | Recommendation |
|---|---|---|---|---|
| a | Partial | `MainWindow/MainWindow.xaml` (`ListBox`, `VirtualizingPanel.IsVirtualizing="True"`) · `MainWindow/ResultTemplate.cs` (code-built `DataTemplate`: icon + filename + parent-dir) · `MainWindow/Models.cs` (`ResultRow`) | Low — virtualization + result-row template are reusable; **not** a multi-column grid with sort menus | **REUSE** the virtualization + result-row DataTemplate pattern; **REIMPLEMENT** a true multi-column sortable grid |
| b | Absent | — | — | **SKIP** |
| c | **Strong match (MIT)** | `PreviewWindow/PreviewManager.cs` · `PreviewWindow/PreviewFileHandler.cs` (IPreviewHandler via registry CLSID lookup) · `PreviewWindow/ThumbnailHandler.cs` (`IShellItemImageFactory` thumbnails + `IPropertyStore` metadata) · `PreviewWindow/NativeMethods.cs` · `PreviewWindow/PreviewWindow.xaml` | Moderate — hosts the handler in a WinForms panel; COM logic is clean and MIT | **REUSE** the IPreviewHandler hosting pattern (MIT); note it uses a WinForms host control |
| d | Absent | — | — | **SKIP** |
| e | Absent | — (search-only list; `CategoryBuilder` only groups results) | — | **SKIP** |
| f | Absent | — | — | **SKIP** |
| g | Absent | — (no visible status bar / counts) | — | **SKIP** |

**Notes:** Clean, compact IPreviewHandler reference. Because it is MIT (per README), its preview code is a viable reuse candidate — but see the license caveat below before shipping.

---

## 3. Flow.Launcher — `github.com/Flow-Launcher/Flow.Launcher`
**License:** MIT (`LICENSE` present, "Copyright (c) 2019 Flow-Launcher"; note its `ShellContextMenu.cs` is adapted from a CodeProject article by Andreas Johansson — preserve that attribution). **Framework:** `net9.0-windows10.0.19041.0`, WPF (+ WinForms).

| Item | Verdict | Key files (relative) | Coupling | Recommendation |
|---|---|---|---|---|
| a | Partial | `Flow.Launcher/ResultListBox.xaml` (+ `.cs`) — custom virtualized `ListBox` (`IsVirtualizing` + `Standard` mode) with a Grid data template (icon/title/subtitle/hotkey) | Low — virtualization + result-item pattern reusable; **not** a multi-column grid/sort | **REUSE** the virtualization + result-item pattern; **REIMPLEMENT** multi-column grid + sort |
| b | Absent (main app) | — | — | **SKIP** |
| c | Weak for COM hosting | `Flow.Launcher/MainWindow.xaml` preview region (`PreviewSubTitle`) shows plugin-provided text only — **no IPreviewHandler COM hosting** | — | **SKIP** the COM-hosting aspect (reference only for layout) |
| d | **Best-match WPF context menu** | `Plugins/Flow.Launcher.Plugin.Explorer/Helper/ShellContextMenuDisplayHelper.cs` · `.../ContextMenu.cs` · `.../Search/ResultManager.cs` (`ShowNativeContextMenu`) · `.../Helper/ShellContextMenu.cs` (older Win32 `TrackPopupMenuEx` path via `NativeWindow`) | Low–moderate — `ShellContextMenuDisplayHelper` reads native shell verbs **+ icons** into managed `ContextMenuItem(Label, ImageSource, CommandId)` and executes by id; WPF-native, modern | **REUSE / ADAPT** `ShellContextMenuDisplayHelper` (top pick for building a WPF context menu from shell verbs with icons) |
| e | Absent | — (Explorer plugin is search-based; no folder tree) | — | **SKIP** |
| f | Absent | — | — | **SKIP** |
| g | Weak | No persistent status bar with result counts/timing | — | **SKIP** |

**Notes:** `ShellContextMenuDisplayHelper` is the cleanest "render native shell context menu inside WPF" reference in the set (MIT). Keep original CodeProject attribution for the sibling `ShellContextMenu.cs`.

---

## 4. Files — `github.com/files-community/Files`
**License: DUAL — `LICENSE-MIT` + `LICENSE-MPL` (MPL-2.0).** Distribution across `src`: **~831 MIT files, ~233 MPL-2.0 files.** **Framework:** `net10.0-windows`, WinAppSDK / WinUI (`Microsoft.UI.Xaml`).

> **Component-level license split matters here** (see §6). Representative **MPL-2.0** locations: `src/Files.App.Controls/*` (BreadcrumbBar, Omnibar, SamplePanel, `Sidebar/FlatSidebarItem.cs`, ThemedIcon), `src/Files.App/ViewModels/UserControls/SidebarViewModel.FlatTree.cs`, `SidebarViewModel.TabExpansion.cs`, much of `Data/Contracts`, `Data/Enums`, `Storage/Windows/Interop/*`, and the bulk-actions/ recycle-bin code. **MIT-covered** reusable files include `Utils/Shell/ShellFileOperations2.cs`, `UserControls/DataGridHeader.xaml.cs`, `UserControls/StatusBar.xaml.cs`.

| Item | Verdict | Key files (relative) | License of file(s) | Coupling | Recommendation |
|---|---|---|---|---|---|
| a | **Strong architecture** | `Views/Layouts/DetailsLayoutPage.xaml` (+ `.cs`) — dynamic columns (Name/Size/Type/Git/…) with MenuFlyout sort toggles + `ColumnsViewModel`; `UserControls/DataGridHeader.xaml.cs` (sort-direction states) | `DataGridHeader` = MIT; layout/grid = WinUI | **WinUI controls do not port to WPF** — REIMPLEMENT the WPF UI; ADAPT the `ColumnsViewModel`/column-model/sort logic |
| b | Present but different concept | Tabbed *file-explorer* navigation (`ShellViewModel`/`ContentPage`); `SidebarViewModel.TabExpansion.cs` (MPL) | MPL | These are folder-pane tabs, not search-session tabs | **REIMPLEMENT** for search tabs |
| c | Reference only | `Services/PreviewPopupProviders/PowerToysPeekProvider.cs`, `PreviewPopupService.cs` — preview-popup architecture; no drop-in IPreviewHandler host | MPL | WinUI + architecture-level | Use as design reference; **REIMPLEMENT** the COM host |
| e | **Strong tree model (MPL)** | `ViewModels/UserControls/SidebarViewModel.FlatTree.cs` (+ `Controls/Sidebar/FlatSidebarItem.cs`) — flat hierarchical sidebar rendered by `ItemsRepeater`, with chunked inserts / expansion tracking | **MPL-2.0** (copyleft) | High-value flat-tree model; UI is WinUI | **ADAPT the model logic with copyleft awareness**; REIMPLEMENT the WinUI rendering |
| f | **Best-match file ops + undo (MIT)** | `Utils/Shell/ShellFileOperations2.cs` (`IFileOperation` + `IFileOperationProgressSink`, `FOF_ALLOWUNDO`, progress %, modern `Windows.Win32` source-gen P/Invoke, `unsafe`) · `Utils/Storage/Operations/FileOperationsHelpers.cs` · `Actions/*` · `Services/Storage/StorageTrashBinService.cs` | **`ShellFileOperations2.cs` = MIT** (header confirmed) | Moderate — depends on `Windows.Win32` NuGet (supports `net8.0-windows`) and `unsafe`; the COM pattern is clean | **REUSE / ADAPT** — top pick for item (f). Confirm `net8` compatibility of the source-gen P/Invoke during porting |
| g | **Strong match (mostly MIT)** | `UserControls/StatusBar.xaml.cs` (**MIT**) adaptive status bar with item counts + `Data/Enums/StatusBarItemCountFormat.cs` (**MPL**) · `ViewModels/UserControls/StatusCenterViewModel.cs` (op progress center) | StatusBar UI = MIT; count-format enum = MPL | Low–moderate | **REUSE** the count/format logic (MIT StatusBar); note MPL on the enum — reimplement or comply |

**Notes:** Files is a rich file-manager, not a launcher. Its multi-column details grid, flat sidebar tree, and `IFileOperation`-with-undo are the strongest item-(a)/(e)/(f) references in the set — but they sit across the MIT/MPL boundary, so reuse must be done **file-by-file** (see §6).

---

## 5. QuickLook — `github.com/QL-Win/QuickLook`
**License: GPL-3.0** (`LICENSE-GPL.txt`). **Framework:** `net462`, WPF + WinForms + WPF-UI. Purpose-built preview app; also has a named-pipe server (`PipeServerManager`) architecturally similar to EverythingToolbar's SDK3.

| Item | Verdict | Key files (relative) | Coupling | Recommendation |
|---|---|---|---|---|
| c | **Best overall preview reference — but GPL** | `QuickLook.Common/Plugin/IViewer.cs` + `ContextObject.cs` (plugin preview abstraction) · `QuickLook.Plugin/.../PreviewHandlerHost.cs` (WinForms `Control`-hosted IPreviewHandler; out-of-process `prevhost.exe` via `DllSurrogate`, cached class factories, init stream→item→file) · also `App/Services/QuickLookManager*.cs`, `App/Views/QuickLook/*` | High-quality & sophisticated (mirrors PowerToys Peek), but **GPL-3.0** + WinForms host | **REIMPLEMENT from the pattern — do NOT copy code** into an MIT app without GPL compliance. Reference-only. |
| a,b,d,e,f,g | Not the focus of this repo (preview-centric) | — | — | **SKIP** |

**Notes:** Technically the richest preview implementation, but the **GPL-3.0 license is a hard conflict** for an MIT fork (see §6). Its named-pipe client pattern is worth a *conceptual* look only.

---

## 6. License conflicts & caveats
1. **QuickLook = GPL-3.0 (whole repo).** Copyleft: any copied/reused preview code forces the derived work to be GPL-3.0. **HARD CONFLICT** for an MIT fork. Treat as reference-only; reimplement from the pattern. Also targets old `net462`.
2. **Files = dual MIT/MPL-2.0 (component-level copyleft).** MPL-2.0 files must remain MPL-2.0 and be disclosed if reused/modified. Manageable: reuse only the **MIT-covered** files (`ShellFileOperations2.cs`, `DataGridHeader.xaml.cs`, `StatusBar.xaml.cs`) with attribution, or comply with MPL for the ~233 MPL files (e.g. `SidebarViewModel.FlatTree.cs`, `FlatSidebarItem.cs`, BreadcrumbBar, Omnibar). The repo-level "MIT" does **not** cover all components — verify per file.
3. **SearchLight = MIT claimed in README only; NO committed `LICENSE` file.** Reusable under the README's MIT grant today, but **verify upstream license before shipping** and add attribution (consider committing a LICENSE). SOFT/PROCEDURAL caveat.
4. **Lertaro & Flow.Launcher = clean MIT with `LICENSE` files.** No repo-level conflicts. Caveat: Flow's `ShellContextMenu.cs` is adapted from a CodeProject article (Andreas Johansson) — preserve the original attribution.

## Framework compatibility vs EverythingToolbar (`net8.0-windows`)
- **Lertaro** (`net10.0-wpf`), **Flow** (`net9`), **SearchLight** (`net9`) — all forward-compatible; code ports to `net8` with minor tweaks.
- **Files** (`net10` WinAppSDK/WinUI) — WinUI controls do **not** port to WPF (reimplement UI); portable logic and the `Windows.Win32`-based `ShellFileOperations2.cs` can adapt (the NuGet supports `net8`).
- **QuickLook** (`net462`, GPL) — not a reuse target regardless of framework.

---

## 7. Summary table — recommended reusable components (best candidates for items a–g)

| Item | Best reuse candidate(s) | Repo / License | File path(s) (relative) | Action |
|---|---|---|---|---|
| **a) Virtualized multi-column result table** | Lertaro results grid + dynamic columns + sort | Lertaro / MIT | `App/Views/Controls/Results/ResultsControl.xaml`, `.../ResultsControlColumns.cs`, `PluginSdk/Abstractions/Plugins/IResultColumnProvider.cs` | **REUSE** grid/sort; **ADAPT** column model |
|  | (alt) Files details grid columns + sort logic | Files / MIT (`DataGridHeader`) | `Views/Layouts/DetailsLayoutPage.xaml`, `UserControls/DataGridHeader.xaml.cs` | **ADAPT** logic, **REIMPLEMENT** WinUI→WPF UI |
| **b) Tabbed interface** | — none suitable — | — | — | **REIMPLEMENT** (no prior art found for search-session tabs) |
| **c) Preview pane (IPreviewHandler)** | Lertaro HwndHost IPreviewHandler host + pool (+ interop) | Lertaro / MIT | `Plugins/CoreExtensions/Preview/Handlers/PreviewHandlerHost.cs`, `.../PreviewHandlerInterop.cs`, `.../PreviewHandlerPool.cs` | **REUSE** (cleanest WPF, MIT) |
|  | SearchLight preview hosting (registry CLSID + thumbnails) | SearchLight / MIT(README) | `PreviewWindow/PreviewFileHandler.cs`, `.../ThumbnailHandler.cs`, `.../PreviewManager.cs` | **REUSE** pattern; verify license before shipping |
|  | QuickLook PreviewHandlerHost (reference only — GPL) | QuickLook / GPL-3.0 | `QuickLook.Plugin/.../PreviewHandlerHost.cs`, `QuickLook.Common/Plugin/{IViewer,ContextObject}.cs` | **REIMPLEMENT**; do not copy (GPL conflict) |
| **d) Native Windows context menu** | Flow.Launcher shell-verb + icon enumeration → WPF | Flow.Launcher / MIT | `Plugins/Flow.Launcher.Plugin.Explorer/Helper/ShellContextMenuDisplayHelper.cs` (+ `ContextMenu.cs`, `Search/ResultManager.cs`) | **REUSE / ADAPT** (top pick) |
|  | Lertaro ShellContextMenuHelper (subclassing + dedup) | Lertaro / MIT | `App/services/ShellMenu/Presenter/ShellContextMenuHelper.cs` | **REUSE** (no WinForms dep) |
| **e) Folder tree / filter sidebar** | Files flat hierarchical sidebar model | Files / **MPL-2.0** | `ViewModels/UserControls/SidebarViewModel.FlatTree.cs`, `Controls/Sidebar/FlatSidebarItem.cs` | **ADAPT** model w/ copyleft awareness; **REIMPLEMENT** UI |
|  | Lertaro filter-item style (sidebar only) | Lertaro / MIT | `App/Resources/Styles/Windows/SearchWindow.xaml` (`SidebarFilterItemStyle`) | **ADAPT** filter pattern |
| **f) File ops + progress + undo (IFileOperation)** | Files IFileOperation wrapper w/ FOF_ALLOWUNDO + progress sink | Files / **MIT** | `Utils/Shell/ShellFileOperations2.cs`, `Utils/Storage/Operations/FileOperationsHelpers.cs` | **REUSE / ADAPT** (top pick; confirm net8 source-gen P/Invoke) |
| **g) Status bar (counts/timing)** | Files adaptive status bar + count format | Files / MIT (`StatusBar`) + MPL(enum) | `UserControls/StatusBar.xaml.cs`, `Data/Enums/StatusBarItemCountFormat.cs` | **REUSE** count logic (MIT); reimplement/comply on enum |

### Bottom line
- **Primary reuse sources (MIT, low risk):** **Lertaro** (items a, c, d) and **Flow.Launcher** (item d). These are the highest-value, cleanest-WPF, MIT-licensed candidates.
- **Strong logic references requiring porting/reimplement:** **Files** (items a, e, f, g) — excellent architecture but WinUI + dual MIT/MPL; reuse only MIT-covered files or honor MPL.
- **Reference-only (do not copy):** **QuickLook** (item c) — best preview implementation but GPL-3.0 copyleft conflict.
- **Verify before shipping:** **SearchLight** (item c) — MIT claimed in README with no committed LICENSE file.
- **No prior art found for:** item (b) search-session tabs, and item (f) undo is only covered by Files' `IFileOperation` wrapper.
