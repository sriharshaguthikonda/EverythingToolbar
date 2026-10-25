using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.DependencyInjection;
using EverythingToolbar.App;
using EverythingToolbar.Search;
using EverythingToolbar.Services;

namespace EverythingToolbar
{
    public partial class ExpandedSearchWindow : Window
    {
        private const string RepoUrl = "https://github.com/srwi/EverythingToolbar";

        private readonly ISettings _settings = Ioc.Default.GetRequiredService<ISettings>();
        private readonly SearchSession _session = Ioc.Default.GetRequiredService<SearchSession>();
        private readonly SearchCommands _commands = Ioc.Default.GetRequiredService<SearchCommands>();
        private readonly SearchWindowController _controller = Ioc.Default.GetRequiredService<SearchWindowController>();
        private readonly App.Search.SearchTabManager _tabManager =
            Ioc.Default.GetRequiredService<App.Search.SearchTabManager>();
        private readonly App.Search.SearchBookmarkStore _bookmarkStore =
            Ioc.Default.GetRequiredService<App.Search.SearchBookmarkStore>();
        private readonly App.Search.SearchState _searchState = Ioc.Default.GetRequiredService<App.Search.SearchState>();
        private readonly App.Services.FilterProvider _filterProvider =
            Ioc.Default.GetRequiredService<App.Services.FilterProvider>();

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
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
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
                Close();
                e.Handled = true;
                return;
            }

            if (modifiers == ModifierKeys.Control && e.Key == Key.F)
            {
                FocusSearchBox();
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
                    case Key.C:
                        _settings.IsMatchCase = !_settings.IsMatchCase;
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

            if (_commands.TranslateResultsGesture(e.Key, e.SystemKey, modifiers, fromSearchBox: true))
                e.Handled = true;
        }

        private void TogglePreview()
        {
            PreviewPane.Visibility =
                PreviewPane.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
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
