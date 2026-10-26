using System;
using System.IO;
using EverythingToolbar.App;
using EverythingToolbar.App.Search;
using EverythingToolbar.App.Services;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Core.Platform;
using EverythingToolbar.Core.Search;
using EverythingToolbar.Core.Services;
using EverythingToolbar.FuzzySearch.Tests.Support;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    /// <summary>
    /// Browser-style tabs over the single SearchState: snapshot round-trips must reproduce the
    /// exact BuildSearchQuery, manager add/close/switch semantics follow Explorer/Everything
    /// behaviour (never zero tabs, snapshot-on-switch), and tabs.json round-trips via a temp path
    /// with corrupt files read as absent.
    /// </summary>
    public sealed class SearchTabTests : IDisposable
    {
        private readonly string _tabsPath = Path.Combine(
            Path.GetTempPath(),
            "etb-tabs-" + Guid.NewGuid().ToString("N") + ".json"
        );

        public void Dispose()
        {
            try
            {
                File.Delete(_tabsPath);
            }
            catch (IOException)
            {
                // Temp cleanup is best-effort.
            }
        }

        private static (SearchState State, FilterProvider Filters, ISettings Settings) CreateSearchState()
        {
            var settings = TestSettingsFactory.Create(typoTolerantSearchEnabled: true);
            var names = new StubFilterNames();
            var defaultProvider = new DefaultFilterProvider(names, settings);
            var everythingProvider = new EverythingFilterProvider(
                names,
                new StubNotifier(),
                new StubShellDialogs(),
                settings
            );
            var filters = new FilterProvider(defaultProvider, everythingProvider, settings);
            var history = new SearchHistory(settings);
            var state = new SearchState(history, filters, settings, new FakeEverythingClient());
            return (state, filters, settings);
        }

        private SearchTabManager CreateManager(SearchState state, FilterProvider filters)
        {
            return new SearchTabManager(state, filters, _tabsPath);
        }

        [Fact]
        public void SnapshotRoundTrip_RestoresSameSearchQuery()
        {
            var (state, filters, settings) = CreateSearchState();
            state.SearchTerm = "notes ext:txt";
            state.Filter = filters.Filters[1];
            state.SetSort(SortBy.Size, descending: true);
            settings.IsMatchCase = true;
            settings.IsRegExEnabled = true;
            var expected = state.BuildSearchQuery();

            var snapshot = state.CaptureSnapshot();

            // Mutate everything, then restore.
            state.SearchTerm = "different";
            state.Filter = filters.Filters[0];
            state.SetSort(SortBy.Name, descending: false);
            settings.IsMatchCase = false;
            settings.IsRegExEnabled = false;
            state.RestoreSnapshot(snapshot);

            Assert.Equal(expected, state.BuildSearchQuery());
        }

        [Fact]
        public void DisplayName_EmptyQuery_ShowsNewTab()
        {
            var tab = new SearchTab { SearchTerm = "  " };
            Assert.Equal("New Tab", tab.DisplayName);

            tab.SearchTerm = "query";
            Assert.Equal("query", tab.DisplayName);
        }

        [Fact]
        public void NewTab_SnapshotsOutgoingAndActivatesEmptyTab()
        {
            var (state, filters, _) = CreateSearchState();
            var manager = CreateManager(state, filters);
            manager.Load();
            state.SearchTerm = "first query";

            var newTab = manager.NewTab();

            Assert.Equal(2, manager.Tabs.Count);
            Assert.Equal(1, manager.ActiveIndex);
            Assert.Equal("first query", manager.Tabs[0].SearchTerm);
            Assert.Equal("", manager.Tabs[1].SearchTerm);
            Assert.Equal("", state.SearchTerm);
            Assert.Same(newTab, manager.Tabs[1]);
            Assert.True(manager.Tabs[1].IsActive);
            Assert.False(manager.Tabs[0].IsActive);
        }

        [Fact]
        public void Switch_RestoresSnapshotBothWays()
        {
            var (state, filters, _) = CreateSearchState();
            var manager = CreateManager(state, filters);
            manager.Load();
            state.SearchTerm = "first query";

            manager.NewTab();
            state.SearchTerm = "second query";
            state.SetSort(SortBy.Size, true);

            manager.SwitchTo(0);
            Assert.Equal("first query", state.SearchTerm);
            Assert.Equal(SortBy.Name, state.EffectiveSortBy);

            manager.SwitchTo(1);
            Assert.Equal("second query", state.SearchTerm);
            Assert.Equal(SortBy.Size, state.EffectiveSortBy);
            Assert.True(state.IsSortDescending);
        }

        [Fact]
        public void CloseActiveTab_SelectsNeighbourAndRestoresIt()
        {
            var (state, filters, _) = CreateSearchState();
            var manager = CreateManager(state, filters);
            manager.Load();
            state.SearchTerm = "tab zero";
            manager.NewTab();

            manager.CloseTab(manager.ActiveIndex);

            Assert.Single(manager.Tabs);
            Assert.Equal(0, manager.ActiveIndex);
            Assert.Equal("tab zero", state.SearchTerm);
        }

        [Fact]
        public void CloseLastTab_ClearsToEmptyInsteadOfClosing()
        {
            var (state, filters, _) = CreateSearchState();
            var manager = CreateManager(state, filters);
            manager.Load();
            state.SearchTerm = "only tab";

            manager.CloseTab(0);

            Assert.Single(manager.Tabs);
            Assert.Equal(0, manager.ActiveIndex);
            Assert.Equal("", manager.Tabs[0].SearchTerm);
            Assert.Equal("", state.SearchTerm);
        }

        [Fact]
        public void CloseTabBeforeActive_KeepsSameActiveTab()
        {
            var (state, filters, _) = CreateSearchState();
            var manager = CreateManager(state, filters);
            manager.Load();
            state.SearchTerm = "first";
            manager.NewTab();
            state.SearchTerm = "second"; // active (second) tab now holds this query

            manager.CloseTab(0); // close the non-active first tab

            Assert.Single(manager.Tabs);
            Assert.Equal(0, manager.ActiveIndex);
            Assert.Equal("second", manager.Tabs[0].SearchTerm);
            Assert.Equal("second", state.SearchTerm);
        }

        [Fact]
        public void SaveThenLoad_RestoresTabsQueriesAndActiveIndex()
        {
            var (state, filters, _) = CreateSearchState();
            var manager = CreateManager(state, filters);
            manager.Load();
            state.SearchTerm = "first";
            manager.NewTab();
            state.SearchTerm = "second query";
            manager.Save();

            var (reloadedState, reloadedFilters, _) = CreateSearchState();
            var reloaded = CreateManager(reloadedState, reloadedFilters);
            reloaded.Load();

            Assert.Equal(2, reloaded.Tabs.Count);
            Assert.Equal("first", reloaded.Tabs[0].SearchTerm);
            Assert.Equal("second query", reloaded.Tabs[1].SearchTerm);
            Assert.Equal(1, reloaded.ActiveIndex);
            Assert.Equal("second query", reloadedState.SearchTerm);
        }

        [Fact]
        public void Load_WithCorruptJson_YieldsSingleEmptyTab()
        {
            File.WriteAllText(_tabsPath, "{ not valid json !!");
            var (state, filters, _) = CreateSearchState();
            var manager = CreateManager(state, filters);

            manager.Load();

            Assert.Single(manager.Tabs);
            Assert.Equal("", manager.Tabs[0].SearchTerm);
            Assert.Equal(0, manager.ActiveIndex);
        }

        [Fact]
        public void Load_WithMissingFile_YieldsSingleEmptyTab()
        {
            var (state, filters, _) = CreateSearchState();
            var manager = CreateManager(state, filters);

            manager.Load();

            Assert.Single(manager.Tabs);
            Assert.Equal("", state.SearchTerm);
        }

        [Fact]
        public void Load_IsIdempotent_SecondCallKeepsCurrentTabs()
        {
            var (state, filters, _) = CreateSearchState();
            var manager = CreateManager(state, filters);
            manager.Load();
            manager.NewTab();

            manager.Load();

            Assert.Equal(2, manager.Tabs.Count);
        }

        private sealed class StubFilterNames : IFilterNames
        {
            public string All => "All";
            public string File => "File";
            public string Folder => "Folder";
            public string Audio => "Audio";
            public string Compressed => "Compressed";
            public string Document => "Document";
            public string Executable => "Executable";
            public string Picture => "Picture";
            public string Video => "Video";
        }

        private sealed class StubNotifier : INotifier
        {
            public void ShowError(string messageResourceKey, string? detail = null) { }

            public void ShowInformation(string messageResourceKey) { }
        }

        private sealed class StubShellDialogs : IShellDialogs
        {
            public void OpenWith(string filePath) { }

            public void OpenParentFolderAndSelect(string filePath) { }

            public void ShowFileProperties(string filePath) { }

            public void ShowWindowsContextMenu(string filePath) { }

            public string? BrowseForFile(string filterLabel, string filterPattern, string? initialDirectory) => null;
        }
    }
}
