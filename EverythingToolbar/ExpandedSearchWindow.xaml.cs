using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using EverythingToolbar.App.Helpers;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Search;
using NLog;

namespace EverythingToolbar
{
    public partial class ExpandedSearchWindow : Window
    {
        private const string RepoUrl = "https://github.com/srwi/EverythingToolbar";

        private static readonly ILogger Logger = ToolbarLogger.GetLogger<ExpandedSearchWindow>();

        private readonly ISettings _settings = Ioc.Default.GetRequiredService<ISettings>();
        private readonly SearchCommands _commands = Ioc.Default.GetRequiredService<SearchCommands>();
        private readonly App.Search.ResultsExportService _exportService =
            Ioc.Default.GetRequiredService<App.Search.ResultsExportService>();
        private readonly SearchWindowController _controller = Ioc.Default.GetRequiredService<SearchWindowController>();
        private readonly App.Search.SearchTabManager _tabManager =
            Ioc.Default.GetRequiredService<App.Search.SearchTabManager>();
        private readonly App.Search.SearchBookmarkStore _bookmarkStore =
            Ioc.Default.GetRequiredService<App.Search.SearchBookmarkStore>();
        private readonly App.Search.SearchSessionStore _sessionStore =
            Ioc.Default.GetRequiredService<App.Search.SearchSessionStore>();
        private readonly App.Search.SearchState _searchState = Ioc.Default.GetRequiredService<App.Search.SearchState>();
        private readonly EditOperationsController _editOperations =
            Ioc.Default.GetRequiredService<EditOperationsController>();
        private readonly App.Services.FilterProvider _filterProvider =
            Ioc.Default.GetRequiredService<App.Services.FilterProvider>();
        private readonly App.Search.EverythingSearchLauncher _everythingLauncher =
            Ioc.Default.GetRequiredService<App.Search.EverythingSearchLauncher>();
        private CancellationTokenSource? _exportCts;

        public ExpandedSearchWindow()
        {
            InitializeComponent();
            Loaded += OnWindowLoaded;
            Closed += OnWindowClosed;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            // Idempotent: the first expanded window after app start loads tabs.json (single empty
            // tab on fresh install); later windows share the already-populated manager singleton.
            _tabManager.Load();

            // Same pattern for bookmarks.json (no-op on later windows).
            _bookmarkStore.Load();
            RebuildBookmarksMenu();
            _bookmarkStore.Bookmarks.CollectionChanged += OnBookmarksCollectionChanged;

            // And sessions.json.
            _sessionStore.Load();

            ApplyFolderSidebarVisibility(_settings.IsFolderSidebarEnabled);
            ApplyBookmarkBarVisibility();
            ApplyViewMenuChecks();
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            _bookmarkStore.Bookmarks.CollectionChanged -= OnBookmarksCollectionChanged;
            _tabManager.Save();
        }

        public void FocusSearchBox()
        {
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var modifiers = Keyboard.Modifiers;

            if (e.Key == Key.Escape)
            {
                // While an export is running, Esc cancels it instead of closing the window.
                if (_exportCts is not null)
                {
                    _exportCts.Cancel();
                    e.Handled = true;
                    return;
                }

                Close();
                e.Handled = true;
                return;
            }

            if (modifiers == ModifierKeys.Control && e.Key == Key.S)
            {
                ExportResults();
                e.Handled = true;
                return;
            }

            if (modifiers == ModifierKeys.Control && e.Key == Key.F)
            {
                FocusSearchBox();
                e.Handled = true;
                return;
            }

            if (modifiers == ModifierKeys.Control && e.Key == Key.B)
            {
                ToggleFolderSidebar();
                e.Handled = true;
                return;
            }

            if (modifiers == ModifierKeys.Control && e.Key == Key.P)
            {
                TogglePreview();
                e.Handled = true;
                return;
            }

            if (modifiers == ModifierKeys.Control && e.Key == Key.D)
            {
                AddBookmark();
                e.Handled = true;
                return;
            }

            if (modifiers == ModifierKeys.Control && e.Key == Key.T)
            {
                _tabManager.NewTab();
                e.Handled = true;
                return;
            }

            if (modifiers == ModifierKeys.Control && e.Key == Key.W)
            {
                CloseActiveTab();
                e.Handled = true;
                return;
            }

            if (modifiers == ModifierKeys.Control && e.Key == Key.Tab)
            {
                _tabManager.CycleNext();
                e.Handled = true;
                return;
            }

            if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.E)
            {
                _controller.ToggleExpanded();
                e.Handled = true;
                return;
            }

            if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                switch (e.Key)
                {
                    case Key.A:
                        ShowAdvancedSearch();
                        e.Handled = true;
                        return;
                    case Key.W:
                        _settings.IsMatchWholeWord = !_settings.IsMatchWholeWord;
                        e.Handled = true;
                        return;
                    case Key.U:
                        _settings.IsMatchPath = !_settings.IsMatchPath;
                        e.Handled = true;
                        return;
                    case Key.R:
                        _settings.IsRegExEnabled = !_settings.IsRegExEnabled;
                        e.Handled = true;
                        return;
                }
            }

            // Edit-menu file operations (Everything semantics: Ctrl+C copies the selected files;
            // copy path is Ctrl+Shift+C). The data view skips text-editing keys while the search
            // box has focus, so text editing keeps its native behaviour.
            if (ResultsView.TryHandleEditGesture(e))
            {
                e.Handled = true;
                return;
            }

            if (_commands.TranslateResultsGesture(e.Key, e.SystemKey, modifiers, fromSearchBox: true))
                e.Handled = true;
        }

        private void TogglePreview()
        {
            PreviewPane.Visibility =
                PreviewPane.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnEditSubmenuOpened(object sender, RoutedEventArgs e)
        {
            // Recomputed on every open so enablement always reflects the live selection and
            // clipboard (Everything re-evaluates the same way).
            var selection = ResultsView.GetSelectedResults();
            var canAct = _editOperations.CanActOnSelection(selection);
            EditCutMenuItem.IsEnabled = canAct;
            EditCopyMenuItem.IsEnabled = canAct;
            EditCopyNameMenuItem.IsEnabled = canAct;
            EditCopyPathMenuItem.IsEnabled = canAct;
            EditDeleteMenuItem.IsEnabled = canAct;
            EditPasteMenuItem.IsEnabled = _editOperations.GetPastePlan(selection).CanPaste;
        }

        private void OnEditCutClick(object sender, RoutedEventArgs e)
        {
            _editOperations.Cut(ResultsView.GetSelectedResults());
        }

        private void OnEditCopyClick(object sender, RoutedEventArgs e)
        {
            _editOperations.Copy(ResultsView.GetSelectedResults());
        }

        private void OnEditCopyNameClick(object sender, RoutedEventArgs e)
        {
            _editOperations.CopyNames(ResultsView.GetSelectedResults());
        }

        private void OnEditCopyPathClick(object sender, RoutedEventArgs e)
        {
            _editOperations.CopyPaths(ResultsView.GetSelectedResults());
        }

        private void OnEditPasteClick(object sender, RoutedEventArgs e)
        {
            _editOperations.Paste(ResultsView.GetSelectedResults());
        }

        private void OnEditDeleteClick(object sender, RoutedEventArgs e)
        {
            _editOperations.Delete(ResultsView.GetSelectedResults());
        }

        private void OnSortBySubmenuOpened(object sender, RoutedEventArgs e)
        {
            // Rebuild on every open (same pattern as the Bookmarks/Sessions menus) so the
            // checkmarks always reflect the current effective sort, which can also change
            // from result-list header clicks. ResetSort stays internal to header cycling;
            // there is intentionally no "default" reset item here.
            var currentSortBy = _searchState.EffectiveSortBy;
            var currentDescending = _searchState.EffectiveIsSortDescending;

            SortByMenu.Items.Clear();
            foreach (var (sortBy, displayName) in SortMenuItems.Items)
            {
                var item = new MenuItem
                {
                    Header = displayName,
                    IsCheckable = true,
                    IsChecked = sortBy == currentSortBy,
                    Tag = sortBy,
                };
                item.Click += OnSortByItemClick;
                SortByMenu.Items.Add(item);
            }

            SortByMenu.Items.Add(new Separator());
            SortByMenu.Items.Add(
                new MenuItem
                {
                    Header = "Ascending",
                    IsCheckable = true,
                    IsChecked = !currentDescending,
                    Tag = false,
                }
            );
            SortByMenu.Items.Add(
                new MenuItem
                {
                    Header = "Descending",
                    IsCheckable = true,
                    IsChecked = currentDescending,
                    Tag = true,
                }
            );

            // Everything 1.5 multi-sort: secondary and tertiary sorts are applied after the
            // primary. The state is session-only; the header glyph and header-click cycling
            // remain primary-only.
            var secondarySorts = _searchState.SecondarySorts;
            AddSecondarySortSection("Secondary Sort", 0, secondarySorts);
            AddSecondarySortSection("Tertiary Sort", 1, secondarySorts);

            SortByMenu.Items.Add(new Separator());
            var clearItem = new MenuItem { Header = "Clear Additional Sorts" };
            clearItem.Click += OnClearSecondarySortsClick;
            SortByMenu.Items.Add(clearItem);
        }

        private void AddSecondarySortSection(
            string header,
            int slot,
            IReadOnlyList<(SortBy SortBy, bool Descending)>? secondarySorts
        )
        {
            SortByMenu.Items.Add(new MenuItem { Header = header, IsEnabled = false });

            var hasSlot = secondarySorts is not null && slot < secondarySorts.Count;
            var currentSortBy = hasSlot ? secondarySorts[slot].SortBy : (SortBy?)null;
            var currentDescending = hasSlot && secondarySorts[slot].Descending;

            foreach (var (sortBy, displayName) in SortMenuItems.Items)
            {
                var item = new MenuItem
                {
                    Header = displayName,
                    IsCheckable = true,
                    IsChecked = sortBy == currentSortBy,
                };
                item.Click += (_, _) => _searchState.SetSecondarySort(slot, sortBy, currentDescending);
                SortByMenu.Items.Add(item);
            }

            SortByMenu.Items.Add(new Separator());
            var directionItems = new[] { (Descending: false, Label: "Ascending"), (true, "Descending") };
            foreach (var (descending, label) in directionItems)
            {
                var item = new MenuItem
                {
                    Header = label,
                    IsCheckable = true,
                    IsChecked = hasSlot && currentDescending == descending,
                };
                item.Click += (_, _) =>
                    _searchState.SetSecondarySort(slot, currentSortBy ?? _searchState.EffectiveSortBy, descending);
                SortByMenu.Items.Add(item);
            }
            SortByMenu.Items.Add(new Separator());
        }

        private void OnClearSecondarySortsClick(object sender, RoutedEventArgs e)
        {
            _searchState.ClearSecondarySorts();
        }

        private void OnSortByItemClick(object sender, RoutedEventArgs e)
        {
            // Tag carries either the SortBy field (sort items) or the descending flag
            // (Ascending/Descending items); the other dimension is kept at its current value.
            switch ((sender as FrameworkElement)?.Tag)
            {
                case SortBy sortBy:
                    _searchState.SetSort(sortBy, _searchState.EffectiveIsSortDescending);
                    break;
                case bool descending:
                    _searchState.SetSort(_searchState.EffectiveSortBy, descending);
                    break;
            }
        }

        private void OnToolsSubmenuOpened(object sender, RoutedEventArgs e)
        {
            // Recomputed on every open (same pattern as the Edit menu) so the rename item always
            // reflects the live selection.
            ToolsBatchRenameMenuItem.IsEnabled = ResultsView.GetSelectedResults().Count > 0;
            ToolsAdvancedRenameMenuItem.IsEnabled = ResultsView.GetSelectedResults().Count > 0;
        }

        private void OnEverythingClick(object sender, RoutedEventArgs e)
        {
            // No arguments: opens/activates Everything's own main search window.
            _everythingLauncher.Launch();
        }

        private void OnBatchRenameClick(object sender, RoutedEventArgs e)
        {
            var paths = ResultsView
                .GetSelectedResults()
                .Where(result => result is not null)
                .Select(result => result!.FullPathAndFileName)
                .ToList();
            if (paths.Count == 0)
                return;

            var dialog = new App.BatchRenameDialog(paths) { Owner = Window.GetWindow(this) };
            dialog.ShowDialog();
        }

        private void OnAdvancedRenameClick(object sender, RoutedEventArgs e)
        {
            var paths = ResultsView
                .GetSelectedResults()
                .Where(result => result is not null)
                .Select(result => result!.FullPathAndFileName)
                .ToList();
            if (paths.Count == 0)
                return;

            _everythingLauncher.Launch(
                new[] { "-rename" }.Concat(App.Search.EverythingLauncherArguments.BuildRenameArguments(paths)).ToList()
            );
        }

        private void OnFindDuplicateNamesClick(object sender, RoutedEventArgs e)
        {
            // Bare "dupe:" is Everything's documented duplicate-name query (Tools > Find
            // Duplicate Names equivalent); the SearchTerm setter rebuilds the session.
            _searchState.SearchTerm = App.Search.EverythingLauncherArguments.DuplicateNamesSearchTerm;
            FocusSearchBox();
        }

        private void OnAdvancedSearchClick(object sender, RoutedEventArgs e)
        {
            ShowAdvancedSearch();
        }

        private void ShowAdvancedSearch()
        {
            var dialog = new App.AdvancedSearchDialog(_searchState) { Owner = Window.GetWindow(this) };
            dialog.ShowDialog();
            FocusSearchBox();
        }

        private void OnNewWindowClick(object sender, RoutedEventArgs e)
        {
            var window = new ExpandedSearchWindow();
            window.Show();
        }

        private void OnNewTabClick(object sender, RoutedEventArgs e)
        {
            _tabManager.NewTab();
        }

        private void OnCloseTabClick(object sender, RoutedEventArgs e)
        {
            CloseActiveTab();
        }

        private void CloseActiveTab()
        {
            _tabManager.CloseTab(_tabManager.ActiveIndex);
        }

        private void OnTabHeaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: App.Search.SearchTab tab })
                _tabManager.SwitchTo(_tabManager.Tabs.IndexOf(tab));
        }

        private void OnTabStripMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Double-click on empty strip background opens a new tab (clicks on tab headers are
            // handled by OnTabHeaderMouseDown and never reach the ItemsControl background).
            if (ReferenceEquals(e.OriginalSource, TabHeaders) && e.ClickCount == 2)
                _tabManager.NewTab();
        }

        private void OnTabCloseClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: App.Search.SearchTab tab })
                _tabManager.CloseTab(_tabManager.Tabs.IndexOf(tab));
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnTogglePreviewClick(object sender, RoutedEventArgs e)
        {
            TogglePreview();
        }

        private void OnToggleStatusBarClick(object sender, RoutedEventArgs e)
        {
            StatusBar.Visibility =
                StatusBar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        // View > Details / Thumbnails radio pair (Everything-1.5 parity): both items are checkable
        // and kept mutually exclusive by hand since WPF MenuItems do not group. The setting is the
        // source of truth; SearchResultsDataView listens for changes and swaps its presentation.
        private void OnDetailsViewClick(object sender, RoutedEventArgs e)
        {
            _settings.IsThumbnailsView = false;
            ApplyViewMenuChecks();
        }

        private void OnThumbnailsViewClick(object sender, RoutedEventArgs e)
        {
            _settings.IsThumbnailsView = true;
            ApplyViewMenuChecks();
        }

        private void ApplyViewMenuChecks()
        {
            DetailsViewMenuItem.IsChecked = !_settings.IsThumbnailsView;
            ThumbnailsViewMenuItem.IsChecked = _settings.IsThumbnailsView;
        }

        private void OnToggleFolderSidebarClick(object sender, RoutedEventArgs e)
        {
            ToggleFolderSidebar();
        }

        private void ToggleFolderSidebar()
        {
            _settings.IsFolderSidebarEnabled = !_settings.IsFolderSidebarEnabled;
            ApplyFolderSidebarVisibility(_settings.IsFolderSidebarEnabled);
        }

        private void ApplyFolderSidebarVisibility(bool visible)
        {
            FolderSidebar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            SidebarColumn.Width = visible ? new GridLength(240) : new GridLength(0);
            FolderSidebarMenuItem.IsChecked = visible;
        }

        private void OnToggleBookmarkBarClick(object sender, RoutedEventArgs e)
        {
            _settings.IsBookmarkBarEnabled = !_settings.IsBookmarkBarEnabled;
            ApplyBookmarkBarVisibility();
        }

        private void ApplyBookmarkBarVisibility()
        {
            // The bar is only shown when enabled AND non-empty (an empty bar would be dead
            // chrome); the pure rule lives in BookmarkBarInfo.IsBarVisible for unit testing.
            BookmarkBar.Visibility = App.Search.BookmarkBarInfo.IsBarVisible(
                _settings.IsBookmarkBarEnabled,
                _bookmarkStore.Bookmarks.Count
            )
                ? Visibility.Visible
                : Visibility.Collapsed;
            BookmarkBarMenuItem.IsChecked = _settings.IsBookmarkBarEnabled;
        }

        private void OnBookmarksCollectionChanged(
            object? sender,
            System.Collections.Specialized.NotifyCollectionChangedEventArgs e
        )
        {
            // The bar binds directly to the store's ObservableCollection, so item changes refresh
            // it automatically; only the empty/non-empty collapse needs a manual pass.
            ApplyBookmarkBarVisibility();
        }

        private void OnBookmarkBarButtonClick(object sender, RoutedEventArgs e)
        {
            // Same run path as the Bookmarks menu items (shared RunBookmark below).
            if (sender is FrameworkElement { DataContext: App.Search.SearchBookmark bookmark })
                RunBookmark(bookmark);
        }

        private void OnBookmarkBarButtonToolTipOpening(object sender, ToolTipEventArgs e)
        {
            // Computed lazily on open via the pure helper (query text + filter).
            if (sender is FrameworkElement { DataContext: App.Search.SearchBookmark bookmark })
            {
                var element = (FrameworkElement)sender;
                element.ToolTip = App.Search.BookmarkBarInfo.BuildTooltip(bookmark);
            }
        }

        private void OnBookmarkBarRemoveClick(object sender, RoutedEventArgs e)
        {
            // ContextMenu items inherit the placement target's DataContext, which is the bookmark.
            if (sender is FrameworkElement { DataContext: App.Search.SearchBookmark bookmark })
                _bookmarkStore.Remove(bookmark.Name);
        }

        private void OnBookmarkBarRenameClick(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: App.Search.SearchBookmark bookmark })
                return;

            var dialog = new App.BookmarkNameDialog(bookmark.Name) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true)
                return;

            // Add replaces by name; a genuine rename (new name differs) also removes the old
            // entry so the bookmark is moved rather than duplicated.
            _bookmarkStore.Remove(bookmark.Name);
            _bookmarkStore.Add(
                dialog.BookmarkName,
                bookmark.SearchTerm,
                bookmark.FilterName,
                bookmark.SortBy,
                bookmark.IsSortDescending
            );
        }

        private void OnToggleMatchCaseClick(object sender, RoutedEventArgs e)
        {
            _settings.IsMatchCase = !_settings.IsMatchCase;
        }

        private void OnToggleMatchWholeWordClick(object sender, RoutedEventArgs e)
        {
            _settings.IsMatchWholeWord = !_settings.IsMatchWholeWord;
        }

        private void OnToggleMatchPathClick(object sender, RoutedEventArgs e)
        {
            _settings.IsMatchPath = !_settings.IsMatchPath;
        }

        private void OnToggleRegexClick(object sender, RoutedEventArgs e)
        {
            _settings.IsRegExEnabled = !_settings.IsRegExEnabled;
        }

        private void OnBookmarksSubmenuOpened(object sender, RoutedEventArgs e)
        {
            // Rebuild on every open so bookmarks added/removed elsewhere (or in another window)
            // are always current; the store is the single source of truth.
            RebuildBookmarksMenu();
        }

        private void RebuildBookmarksMenu()
        {
            BookmarksMenu.Items.Clear();

            var addItem = new MenuItem { Header = "Add Bookmark…", InputGestureText = "Ctrl+D" };
            addItem.Click += OnAddBookmarkClick;
            BookmarksMenu.Items.Add(addItem);
            BookmarksMenu.Items.Add(new Separator());

            var bookmarks = _bookmarkStore.Bookmarks;
            if (bookmarks.Count == 0)
            {
                BookmarksMenu.Items.Add(new MenuItem { Header = "(no bookmarks)", IsEnabled = false });
            }
            else
            {
                foreach (var bookmark in bookmarks)
                {
                    var item = new MenuItem { Header = bookmark.Name, Tag = bookmark };
                    item.Click += OnBookmarkClick;
                    BookmarksMenu.Items.Add(item);
                }
            }

            BookmarksMenu.Items.Add(new Separator());

            // Remove UX choice: a "Remove…" submenu listing bookmark names (alphabetical, like
            // the run items) instead of right-click context menus on menu items.
            var removeItem = new MenuItem { Header = "Remove…" };
            foreach (var bookmark in bookmarks)
            {
                var entry = new MenuItem { Header = bookmark.Name, Tag = bookmark };
                entry.Click += OnRemoveBookmarkClick;
                removeItem.Items.Add(entry);
            }
            removeItem.IsEnabled = bookmarks.Count > 0;
            BookmarksMenu.Items.Add(removeItem);
        }

        private void OnAddBookmarkClick(object sender, RoutedEventArgs e)
        {
            AddBookmark();
        }

        private void AddBookmark()
        {
            var dialog = new App.BookmarkNameDialog(_searchState.SearchTerm) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true)
                return;

            _bookmarkStore.Add(
                dialog.BookmarkName,
                _searchState.SearchTerm,
                _searchState.Filter?.Name,
                _searchState.EffectiveSortBy,
                _searchState.EffectiveIsSortDescending
            );
        }

        private void OnBookmarkClick(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: App.Search.SearchBookmark bookmark })
                return;

            RunBookmark(bookmark);
        }

        private void RunBookmark(App.Search.SearchBookmark bookmark)
        {
            // Plain property sets + SetSort only (never RestoreSnapshot): bookmarks must not
            // touch the match flags. A filter name that no longer resolves degrades silently
            // to no filter (the default "All" filter).
            _searchState.SearchTerm = bookmark.SearchTerm;
            _searchState.Filter =
                _filterProvider.Filters.FirstOrDefault(f => f.Name == bookmark.FilterName)
                ?? _filterProvider.Filters[0];
            _searchState.SetSort(bookmark.SortBy, bookmark.IsSortDescending);

            FocusSearchBox();
        }

        private void OnRemoveBookmarkClick(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: App.Search.SearchBookmark bookmark })
                return;

            _bookmarkStore.Remove(bookmark.Name);
        }

        private void OnSessionsSubmenuOpened(object sender, RoutedEventArgs e)
        {
            // Rebuild on every open (same pattern as the Bookmarks menu) so sessions saved or
            // deleted elsewhere are always current; the store is the single source of truth.
            RebuildSessionsMenu();
        }

        private void RebuildSessionsMenu()
        {
            SessionsMenu.Items.Clear();

            var saveItem = new MenuItem { Header = "Save Session…" };
            saveItem.Click += OnSaveSessionClick;
            SessionsMenu.Items.Add(saveItem);
            SessionsMenu.Items.Add(new Separator());

            var sessions = _sessionStore.Sessions;
            if (sessions.Count == 0)
            {
                SessionsMenu.Items.Add(new MenuItem { Header = "(no sessions)", IsEnabled = false });
            }
            else
            {
                foreach (var session in sessions)
                {
                    var item = new MenuItem { Header = session.Name, Tag = session };
                    item.Click += OnSessionClick;
                    SessionsMenu.Items.Add(item);
                }
            }

            SessionsMenu.Items.Add(new Separator());

            // Delete UX mirrors the Bookmarks menu: a "Delete Session…" submenu listing session
            // names, no confirmation (Everything doesn't confirm either).
            var deleteItem = new MenuItem { Header = "Delete Session…" };
            foreach (var session in sessions)
            {
                var entry = new MenuItem { Header = session.Name, Tag = session };
                entry.Click += OnDeleteSessionClick;
                deleteItem.Items.Add(entry);
            }
            deleteItem.IsEnabled = sessions.Count > 0;
            SessionsMenu.Items.Add(deleteItem);
        }

        private void OnSaveSessionClick(object sender, RoutedEventArgs e)
        {
            // Capture the active tab's live SearchState first so the current query/filter/flags
            // are part of the saved set, then snapshot the whole tab set (the snapshot deep-copies).
            if (_tabManager.ActiveIndex >= 0 && _tabManager.ActiveIndex < _tabManager.Tabs.Count)
                _searchState.CaptureSnapshotInto(_tabManager.Tabs[_tabManager.ActiveIndex]);

            var dialog = new App.BookmarkNameDialog("") { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true)
                return;

            _sessionStore.Save(
                dialog.BookmarkName,
                new App.Search.SearchSessionSnapshot(dialog.BookmarkName, _tabManager.Tabs, _tabManager.ActiveIndex)
            );
        }

        private void OnSessionClick(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: App.Search.SearchSessionSnapshot session })
                return;

            RestoreSession(session);
        }

        private void RestoreSession(App.Search.SearchSessionSnapshot session)
        {
            // Destructive by design (v1, no confirm): the current tab set is replaced by the
            // session's deep-copied tabs, then persisted so tabs.json matches. The passed tabs
            // are fresh copies, so the stored session is never aliased by the manager.
            _tabManager.ReplaceAll(session.CreateRestoreTabs(), session.ActiveIndex);
            _tabManager.Save();
        }

        private void OnDeleteSessionClick(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: App.Search.SearchSessionSnapshot session })
                return;

            _sessionStore.Delete(session.Name);
        }

        private void OnExportClick(object sender, RoutedEventArgs e)
        {
            ExportResults();
        }

        private async void ExportResults()
        {
            // One export at a time; the menu item is disabled while running anyway.
            if (_exportCts is not null)
                return;

            // The Export dialog picks format + CSV columns first; the SaveFileDialog only asks
            // where to put the already-decided format.
            var session = Ioc.Default.GetRequiredService<App.Search.SearchSession>();
            var exportDialog = new App.ExportDialog(session.TotalCount) { Owner = this };
            if (exportDialog.ShowDialog() != true)
                return;

            var format = exportDialog.SelectedFormat;
            var csvColumns = exportDialog.SelectedColumns;

            var searchTerm = _searchState.SearchTerm;
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Search Results",
                Filter = "CSV file (*.csv)|*.csv|Text file (*.txt)|*.txt|EFU file list (*.efu)|*.efu",
                FilterIndex = format switch
                {
                    ExportFormat.Txt => 2,
                    ExportFormat.Efu => 3,
                    _ => 1,
                },
                DefaultExt = ".csv",
                FileName = SanitizeFileName(searchTerm),
            };

            if (dialog.ShowDialog(this) != true)
                return;

            var expectedExtension = format switch
            {
                ExportFormat.Txt => ".txt",
                ExportFormat.Efu => ".efu",
                _ => ".csv",
            };
            if (!Path.HasExtension(dialog.FileName))
                dialog.FileName += expectedExtension;

            // Build the query on the UI thread so it captures exactly the displayed state
            // (including the typo-fallback behaviour) before any background work starts.
            var query = _searchState.BuildSearchQuery();
            var destinationPath = dialog.FileName;
            var destinationName = Path.GetFileName(destinationPath);

            _exportCts = new CancellationTokenSource();
            ExportMenuItem.IsEnabled = false;
            SetExportStatus("Exporting…");
            var progress = new Progress<int>(count => SetExportStatus($"Exporting {count}…"));
            var cancellationToken = _exportCts.Token;

            try
            {
                var exported = await Task.Run(async () =>
                {
                    using var stream = new FileStream(
                        destinationPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None
                    );
                    return await _exportService.ExportAsync(
                        query,
                        stream,
                        format,
                        csvColumns,
                        progress,
                        cancellationToken
                    );
                });

                if (cancellationToken.IsCancellationRequested)
                {
                    SetExportStatus("Export canceled");
                    ScheduleExportStatusClear();
                }
                else
                {
                    SetExportStatus($"Exported {exported} to {destinationName}");
                    ScheduleExportStatusClear();
                }
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to export search results to {0}.", destinationPath);
                SetExportStatus("Export failed");
                ScheduleExportStatusClear();
            }
            finally
            {
                _exportCts.Dispose();
                _exportCts = null;
                ExportMenuItem.IsEnabled = true;
            }
        }

        private void SetExportStatus(string text)
        {
            ExportStatusText.Text = text;
        }

        private static string SanitizeFileName(string searchTerm)
        {
            var name = string.IsNullOrWhiteSpace(searchTerm) ? "Search Results" : searchTerm.Trim();
            foreach (var invalidChar in Path.GetInvalidFileNameChars())
                name = name.Replace(invalidChar, '_');
            return name;
        }

        private void ScheduleExportStatusClear()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (sender, _) =>
            {
                ((DispatcherTimer)sender).Stop();
                ExportStatusText.Text = string.Empty;
            };
            timer.Start();
        }

        private void OnAboutClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(this, $"EverythingToolbar\n{RepoUrl}", "About EverythingToolbar");
        }

        private void OnChipClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2)
                return;

            switch ((sender as FrameworkElement)?.Tag as string)
            {
                case "Case":
                    _settings.IsMatchCase = !_settings.IsMatchCase;
                    break;
                case "Word":
                    _settings.IsMatchWholeWord = !_settings.IsMatchWholeWord;
                    break;
                case "Path":
                    _settings.IsMatchPath = !_settings.IsMatchPath;
                    break;
                case "Regex":
                    _settings.IsRegExEnabled = !_settings.IsRegExEnabled;
                    break;
            }
        }
    }
}
