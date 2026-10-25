using System;
using System.Windows;
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
