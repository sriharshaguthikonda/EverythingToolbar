using System;
using System.IO;
using System.Linq;
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
    /// Named sessions (saved tab sets): sessions.json round-trips via a temp path with
    /// corrupt files read as absent, names are key + sorted like bookmarks, snapshots are
    /// deep-copied on both save and restore, and the tab manager's ReplaceAll clamps the
    /// active index and never yields an empty tab set.
    /// </summary>
    public sealed class SearchSessionTests : IDisposable
    {
        private readonly string _sessionsPath = Path.Combine(
            Path.GetTempPath(),
            "etb-sessions-" + Guid.NewGuid().ToString("N") + ".json"
        );

        public void Dispose()
        {
            try
            {
                File.Delete(_sessionsPath);
            }
            catch (IOException)
            {
                // Temp cleanup is best-effort.
            }
        }

        private SearchSessionStore CreateStore() => new(_sessionsPath);

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

        private static SearchTab MakeTab(string searchTerm, bool matchCase = false)
        {
            return new SearchTab { SearchTerm = searchTerm, IsMatchCase = matchCase };
        }

        [Fact]
        public void RoundTrip_PreservesNameTabsAndActiveIndex()
        {
            var store = CreateStore();
            store.Load();
            store.Save("work", new SearchSessionSnapshot("work", new[] { MakeTab("first"), MakeTab("second") }, 1));

            var reloaded = CreateStore();
            reloaded.Load();

            var session = Assert.Single(reloaded.Sessions);
            Assert.Equal("work", session.Name);
            Assert.Equal(2, session.Tabs.Count);
            Assert.Equal("first", session.Tabs[0].SearchTerm);
            Assert.Equal("second", session.Tabs[1].SearchTerm);
            Assert.Equal(1, session.ActiveIndex);
        }

        [Fact]
        public void Save_SameNameDifferentCase_Replaces()
        {
            var store = CreateStore();
            store.Load();
            store.Save("Alpha", new SearchSessionSnapshot("Alpha", new[] { MakeTab("a") }, 0));
            store.Save("ALPHA", new SearchSessionSnapshot("ALPHA", new[] { MakeTab("a"), MakeTab("b") }, 1));

            var session = Assert.Single(store.Sessions);
            Assert.Equal("ALPHA", session.Name);
            Assert.Equal(2, session.Tabs.Count);
        }

        [Fact]
        public void Save_EmptyName_IsRejected()
        {
            var store = CreateStore();
            store.Load();

            store.Save("   ", new SearchSessionSnapshot("   ", new[] { MakeTab("a") }, 0));

            Assert.Empty(store.Sessions);
        }

        [Fact]
        public void Delete_RemovesSessionAndPersists()
        {
            var store = CreateStore();
            store.Load();
            store.Save("keep", new SearchSessionSnapshot("keep", new[] { MakeTab("a") }, 0));
            store.Save("drop", new SearchSessionSnapshot("drop", new[] { MakeTab("b") }, 0));

            Assert.True(store.Delete("DROP"));
            Assert.False(store.Delete("missing"));

            var reloaded = CreateStore();
            reloaded.Load();
            var remaining = Assert.Single(reloaded.Sessions);
            Assert.Equal("keep", remaining.Name);
        }

        [Fact]
        public void Load_WithCorruptJson_YieldsEmptyList()
        {
            File.WriteAllText(_sessionsPath, "{ not valid json !!");
            var store = CreateStore();
            store.Load();

            Assert.Empty(store.Sessions);
        }

        [Fact]
        public void Load_WithMissingFile_YieldsEmptyList()
        {
            var store = CreateStore();
            store.Load();

            Assert.Empty(store.Sessions);
        }

        [Fact]
        public void Sessions_AreSortedByNameCaseInsensitive()
        {
            var store = CreateStore();
            store.Load();
            store.Save("beta", new SearchSessionSnapshot("beta", new[] { MakeTab("b") }, 0));
            store.Save("Alpha", new SearchSessionSnapshot("Alpha", new[] { MakeTab("a") }, 0));
            store.Save("ALPHA2", new SearchSessionSnapshot("ALPHA2", new[] { MakeTab("a2") }, 0));

            Assert.Equal(new[] { "Alpha", "ALPHA2", "beta" }, store.Sessions.Select(s => s.Name).ToArray());
        }

        [Fact]
        public void Snapshot_MutatingSourceTabsAfterSave_DoesNotAffectStoredSession()
        {
            var store = CreateStore();
            store.Load();
            var liveTab = MakeTab("original");
            var snapshot = new SearchSessionSnapshot("s", new[] { liveTab }, 0);
            store.Save("s", snapshot);

            liveTab.SearchTerm = "mutated";

            Assert.Equal("original", store.Sessions.Single().Tabs[0].SearchTerm);
        }

        [Fact]
        public void Restore_MutatingRestoredTab_DoesNotAffectStoredSession()
        {
            var store = CreateStore();
            store.Load();
            store.Save("s", new SearchSessionSnapshot("s", new[] { MakeTab("original") }, 0));

            var restored = store.Sessions.Single().CreateRestoreTabs();
            restored[0].SearchTerm = "mutated";
            restored[0].IsMatchCase = true;

            var storedTab = store.Sessions.Single().Tabs[0];
            Assert.Equal("original", storedTab.SearchTerm);
            Assert.False(storedTab.IsMatchCase);
        }

        [Fact]
        public void Restore_CopiesAgain_SecondRestoreIsIndependent()
        {
            var snapshot = new SearchSessionSnapshot("s", new[] { MakeTab("q") }, 0);

            var first = snapshot.CreateRestoreTabs();
            var second = snapshot.CreateRestoreTabs();

            Assert.NotSame(first[0], second[0]);
            first[0].SearchTerm = "changed";
            Assert.Equal("q", second[0].SearchTerm);
        }

        [Fact]
        public void ReplaceAll_ReplacesTabsClampsActiveIndexAndRestoresActiveTab()
        {
            var (state, filters, _) = CreateSearchState();
            var manager = new SearchTabManager(state, filters, _sessionsPath);
            manager.Load();
            state.SearchTerm = "old";

            manager.ReplaceAll(new[] { MakeTab("a"), MakeTab("b"), MakeTab("c") }, 99);

            Assert.Equal(3, manager.Tabs.Count);
            Assert.Equal(2, manager.ActiveIndex);
            Assert.Equal("c", state.SearchTerm);
            Assert.True(manager.Tabs[2].IsActive);
        }

        [Fact]
        public void ReplaceAll_EmptyTabSet_YieldsSingleEmptyTab()
        {
            var (state, filters, _) = CreateSearchState();
            var manager = new SearchTabManager(state, filters, _sessionsPath);
            manager.Load();
            state.SearchTerm = "old";

            manager.ReplaceAll(Array.Empty<SearchTab>(), 0);

            Assert.Single(manager.Tabs);
            Assert.Equal(0, manager.ActiveIndex);
            Assert.Equal("", state.SearchTerm);
        }

        [Fact]
        public void ReplaceAll_ThenSave_PersistsReplacedSet()
        {
            var (state, filters, _) = CreateSearchState();
            var tabsPath = Path.Combine(Path.GetTempPath(), "etb-tabs-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var manager = new SearchTabManager(state, filters, tabsPath);
                manager.Load();
                manager.ReplaceAll(new[] { MakeTab("a"), MakeTab("b") }, 1);
                manager.Save();

                var reloaded = new SearchTabManager(state, filters, tabsPath);
                reloaded.Load();

                Assert.Equal(2, reloaded.Tabs.Count);
                Assert.Equal("b", reloaded.Tabs[1].SearchTerm);
                Assert.Equal(1, reloaded.ActiveIndex);
            }
            finally
            {
                try
                {
                    File.Delete(tabsPath);
                }
                catch (IOException) { }
            }
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
