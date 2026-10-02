using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using EverythingToolbar.App.SettingsSearch;
using Wpf.Ui.Controls;

namespace EverythingToolbar.Settings
{
    public readonly record struct SettingsPageDescriptor(
        string Title,
        SymbolRegular Icon,
        Type PageType,
        Type? InsertAfterPageType
    );

    /// <summary>View model for one search result row in the settings search popup.</summary>
    public sealed class SettingsSearchResult
    {
        public SettingsSearchResult(SettingsSearchEntry entry)
        {
            Entry = entry;
            Title = entry.Title();
            PageName = entry.PageName();
        }

        public SettingsSearchEntry Entry { get; }
        public string Title { get; }
        public string PageName { get; }
    }

    public partial class SettingsWindow
    {
        private static readonly List<SettingsPageDescriptor> ExternalPages = new();
        private readonly IEverythingClient _everythingClient = Ioc.Default.GetRequiredService<IEverythingClient>();
        private readonly IReadOnlyList<SettingsSearchEntry> _searchCatalog = SettingsSearchIndex.Build();

        public static void RegisterPage(SettingsPageDescriptor descriptor)
        {
            ExternalPages.Add(descriptor);
        }

        public SettingsWindow()
        {
            InitializeComponent();
            AddExternalPages();

            Loaded += (_, _) => Dispatcher.BeginInvoke(() => ThisNavigationView.Navigate(typeof(About)));
        }

        private void OnSettingsSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SettingsSearchBox.Text;
            var matches = SettingsSearchFilter.Filter(_searchCatalog, query);
            var results = new List<SettingsSearchResult>(matches.Count);
            foreach (var match in matches)
            {
                results.Add(new SettingsSearchResult(match));
            }

            SearchResultsList.ItemsSource = results;
            SearchResultsPopup.IsOpen = results.Count > 0;
        }

        private void OnSettingsSearchBoxKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down when SearchResultsPopup.IsOpen && SearchResultsList.Items.Count > 0:
                    SearchResultsList.SelectedIndex = Math.Max(SearchResultsList.SelectedIndex, 0);
                    SearchResultsList.Focus();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    if (NavigateToResult(SearchResultsList.SelectedItem as SettingsSearchResult))
                        e.Handled = true;
                    break;
                case Key.Escape when SearchResultsPopup.IsOpen:
                    SearchResultsPopup.IsOpen = false;
                    e.Handled = true;
                    break;
                case Key.Escape:
                    SettingsSearchBox.Clear();
                    e.Handled = true;
                    break;
            }
        }

        private void OnSearchResultKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    NavigateToResult(SearchResultsList.SelectedItem as SettingsSearchResult);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    SearchResultsPopup.IsOpen = false;
                    SettingsSearchBox.Focus();
                    e.Handled = true;
                    break;
            }
        }

        private void OnSearchResultClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                if (ContainerFromItem(SearchResultsList, source) is SettingsSearchResult result)
                {
                    SearchResultsList.SelectedItem = result;
                    NavigateToResult(result);
                    e.Handled = true;
                }
            }
        }

        private bool NavigateToResult(SettingsSearchResult? result)
        {
            if (result is null)
                return false;

            SearchResultsPopup.IsOpen = false;
            ThisNavigationView.Navigate(result.Entry.PageType);
            var anchor = (result.Entry.AnchorTitle ?? result.Entry.Title)();
            // The NavigationView hosts the page inside an internal frame, so the visual-tree walk
            // starts at the NavigationView itself and finds the SettingItem once the page is loaded.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => TryScrollToSettingItem(ThisNavigationView, anchor));
            return true;
        }

        private static bool TryScrollToSettingItem(DependencyObject? root, string title)
        {
            if (root is null)
                return false;

            var children = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < children; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is Controls.SettingItem item && string.Equals(item.Title, title, StringComparison.Ordinal))
                {
                    item.BringIntoView();
                    item.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                    return true;
                }

                if (TryScrollToSettingItem(child, title))
                    return true;
            }

            return false;
        }

        private static object? ContainerFromItem(ItemsControl control, DependencyObject source)
        {
            while (source is not null && source is not ListBoxItem)
            {
                source = VisualTreeHelper.GetParent(source);
            }

            return source is ListBoxItem item && control.ItemContainerGenerator.ItemFromContainer(item) is { } content
                ? content
                : null;
        }

        private void AddExternalPages()
        {
            foreach (var descriptor in ExternalPages)
            {
                var item = new NavigationViewItem
                {
                    Content = descriptor.Title,
                    Icon = new SymbolIcon { Symbol = descriptor.Icon },
                    TargetPageType = descriptor.PageType,
                };

                var index = -1;
                if (descriptor.InsertAfterPageType != null)
                {
                    for (var i = 0; i < ThisNavigationView.MenuItems.Count; i++)
                    {
                        if (
                            ThisNavigationView.MenuItems[i] is NavigationViewItem existing
                            && existing.TargetPageType == descriptor.InsertAfterPageType
                        )
                        {
                            index = i + 1;
                            break;
                        }
                    }
                }

                if (index >= 0)
                    ThisNavigationView.MenuItems.Insert(index, item);
                else
                    ThisNavigationView.MenuItems.Add(item);
            }
        }

        private void OnReportABugClicked(object sender, RoutedEventArgs e)
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";
            string everythingVersion = _everythingClient.GetEverythingVersion().ToString();
            string osVersion = Environment.OSVersion.ToString();

            string url =
                $"https://github.com/srwi/EverythingToolbar/issues/new?template=bug_report.yml"
                + $"&version={HttpUtility.UrlEncode(version)}"
                + $"&et_version={HttpUtility.UrlEncode(everythingVersion)}"
                + $"&windows_version={HttpUtility.UrlEncode(osVersion)}";

            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
    }
}
