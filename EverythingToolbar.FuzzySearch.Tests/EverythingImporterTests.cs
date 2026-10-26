using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EverythingToolbar.App;
using EverythingToolbar.App.Search;
using EverythingToolbar.App.Services;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Core.Platform;
using EverythingToolbar.Core.Services;
using EverythingToolbar.FuzzySearch.Tests.Support;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    /// <summary>
    /// Everything 1.5 import: Session-1.5a.json windows map to named sessions (match flags,
    /// sort display-name lookup with unknown-name fallback, secondary/tertiary sorts, cursel
    /// clamp, filter fallback, absent fields), the history CSV yields search strings in file
    /// order (RFC 4180 quoting, header skipped), and SearchHistory.Import dedupes
    /// case-insensitively and caps at MaxHistorySize.
    /// </summary>
    public sealed class EverythingImporterTests : IDisposable
    {
        private readonly string _historyPath = Path.Combine(
            Path.GetTempPath(),
            "etb-history-" + Guid.NewGuid().ToString("N") + ".xml"
        );

        public void Dispose()
        {
            try
            {
                File.Delete(_historyPath);
            }
            catch (IOException)
            {
                // Temp cleanup is best-effort.
            }
        }

        private const string TwoWindowsJson = """
            [
              {
                "some_ui_field": "ignored",
                "tabs": [
                  {
                    "search": "ale",
                    "match_case": 1,
                    "match_path": 0,
                    "match_whole_word": 1,
                    "match_diacritics": 0,
                    "match_prefix": 1,
                    "match_suffix": 0,
                    "ignore_punctuation": 1,
                    "ignore_whitespace": 0,
                    "match_regex": 0,
                    "filter": "EVERYTHING",
                    "sort": "Date Modified",
                    "sort_ascending": 0,
                    "secondary_sort": "Name",
                    "secondary_sort_ascending": 1,
                    "tertiary_sort": "Path",
                    "tertiary_sort_ascending": 0
                  },
                  {
                    "search": "beta",
                    "match_regex": 1,
                    "sort": "Date Modified"
                  }
                ],
                "tab_cursel": 1
              },
              {
                "tabs": [ {} ],
                "tab_cursel": 7
              }
            ]
            """;

        [Fact]
        public void ParseSessions_TwoWindows_MapsTabsFlagsSortsAndCursel()
        {
            var parsed = EverythingImporter.TryParseSessions(TwoWindowsJson, null, out var result, out var error);

            Assert.True(parsed);
            Assert.Equal("", error);
            Assert.Equal(2, result.Sessions.Count);
            Assert.Equal(0, result.SkippedWindows);

            var first = result.Sessions[0];
            Assert.Equal("Everything 1", first.Snapshot.Name);
            Assert.Equal(2, first.TabCount);
            Assert.Equal(1, first.Snapshot.ActiveIndex);

            var tab = first.Snapshot.Tabs[0];
            Assert.Equal("ale", tab.SearchTerm);
            Assert.True(tab.IsMatchCase);
            Assert.False(tab.IsMatchPath);
            Assert.True(tab.IsMatchWholeWord);
            Assert.False(tab.IsMatchDiacritics);
            Assert.True(tab.IsMatchPrefix);
            Assert.False(tab.IsMatchSuffix);
            Assert.True(tab.IsIgnorePunctuation);
            Assert.False(tab.IsIgnoreWhitespace);
            Assert.False(tab.IsRegExEnabled);
            Assert.Equal(SortBy.DateModified, tab.SortBy);
            Assert.True(tab.IsSortDescending);
            Assert.Null(tab.Filter);
            Assert.NotNull(tab.SecondarySorts);
            Assert.Equal(new List<(SortBy, bool)> { (SortBy.Name, false), (SortBy.Path, true) }, tab.SecondarySorts);

            // sort_ascending absent defaults to ascending; unknown sort name falls back to Name.
            var secondTab = first.Snapshot.Tabs[1];
            Assert.True(secondTab.IsRegExEnabled);
            Assert.Equal(SortBy.DateModified, secondTab.SortBy);
            Assert.False(secondTab.IsSortDescending);

            // tab_cursel is clamped to the tab range.
            var second = result.Sessions[1];
            Assert.Equal("Everything 2", second.Snapshot.Name);
            Assert.Equal(0, second.Snapshot.ActiveIndex);
            Assert.Equal("", second.Snapshot.Tabs[0].SearchTerm);
        }

        [Fact]
        public void ParseSessions_NegativeCursel_ClampsToZero()
        {
            const string json = """[ { "tabs": [ { "search": "x" } ], "tab_cursel": -3 } ]""";

            EverythingImporter.TryParseSessions(json, null, out var result, out _);

            var session = Assert.Single(result.Sessions);
            Assert.Equal(0, session.Snapshot.ActiveIndex);
        }

        [Fact]
        public void ParseSessions_UnknownSortAndSecondary_FallsBackAndDropsSecondary()
        {
            const string json = """
                [ { "tabs": [ { "search": "x", "sort": "Bogus Sort", "secondary_sort": "Nope", "tertiary_sort": "Bogus" } ] } ]
                """;

            EverythingImporter.TryParseSessions(json, null, out var result, out _);

            var tab = Assert.Single(Assert.Single(result.Sessions).Snapshot.Tabs);
            Assert.Equal(SortBy.Name, tab.SortBy);
            Assert.False(tab.IsSortDescending);
            Assert.Null(tab.SecondarySorts);
        }

        [Fact]
        public void ParseSessions_EmptyTabWindow_SkippedButNumberingContinues()
        {
            const string json = """
                [ { "tabs": [] }, { "tabs": [ { "search": "x" } ] } ]
                """;

            EverythingImporter.TryParseSessions(json, null, out var result, out _);

            var session = Assert.Single(result.Sessions);
            Assert.Equal("Everything 2", session.Snapshot.Name);
            Assert.Equal(1, result.SkippedWindows);
        }

        [Fact]
        public void ParseSessions_UnknownFilterAndAllNames_FallBackToDefaultFilter()
        {
            var settings = TestSettingsFactory.Create(typoTolerantSearchEnabled: true);
            var filterProvider = CreateFilterProvider(settings);
            const string json = """
                [
                  { "tabs": [ { "search": "a", "filter": "EVERYTHING" } ] },
                  { "tabs": [ { "search": "b", "filter": "" } ] },
                  { "tabs": [ { "search": "c", "filter": "All" } ] },
                  { "tabs": [ { "search": "d", "filter": "File" } ] },
                  { "tabs": [ { "search": "e", "filter": "Nonexistent" } ] }
                ]
                """;

            EverythingImporter.TryParseSessions(json, filterProvider, out var result, out _);

            var defaultFilter = filterProvider.Filters[0];
            var fileFilter = filterProvider.Filters.First(f => f.Name == "File");
            Assert.Equal(defaultFilter, result.Sessions[0].Snapshot.Tabs[0].Filter);
            Assert.Equal(defaultFilter, result.Sessions[1].Snapshot.Tabs[0].Filter);
            Assert.Equal(defaultFilter, result.Sessions[2].Snapshot.Tabs[0].Filter);
            Assert.Equal(fileFilter, result.Sessions[3].Snapshot.Tabs[0].Filter);
            Assert.Equal(defaultFilter, result.Sessions[4].Snapshot.Tabs[0].Filter);
        }

        [Fact]
        public void ParseSessions_MalformedJson_ReturnsErrorAndEmptyResult()
        {
            var parsed = EverythingImporter.TryParseSessions("{not json", null, out var result, out var error);

            Assert.False(parsed);
            Assert.NotEqual("", error);
            Assert.Empty(result.Sessions);

            // A non-array root is rejected too.
            parsed = EverythingImporter.TryParseSessions("""{ "tabs": [] }""", null, out result, out error);
            Assert.False(parsed);
            Assert.Empty(result.Sessions);
        }

        [Fact]
        public void ParseSearchHistory_QuotedUnquotedAndHeader_YieldsSearchesInFileOrder()
        {
            const string csv = """
                Search,Search Count,Last Search Date
                "symptoms",1,134374550733075981
                plain term,2,134374550733075982
                "say ""hi"", ok",5,134374550733075983

                ,,134374550733075984
                """;

            var searches = EverythingImporter.ParseSearchHistory(csv);

            Assert.Equal(new[] { "symptoms", "plain term", "say \"hi\", ok" }, searches);
        }

        [Fact]
        public void ParseSearchHistory_EmptyAndHeaderOnly_YieldNothing()
        {
            Assert.Empty(EverythingImporter.ParseSearchHistory(""));
            Assert.Empty(EverythingImporter.ParseSearchHistory(null));
            Assert.Empty(EverythingImporter.ParseSearchHistory("Search,Search Count,Last Search Date\r\n"));
        }

        [Fact]
        public void ParseSearchHistory_CrlfLineEndings_AreHandled()
        {
            var searches = EverythingImporter.ParseSearchHistory(
                "Search,Search Count,Last Search Date\r\na,1,2\r\nb,1,2"
            );

            Assert.Equal(new[] { "a", "b" }, searches);
        }

        [Fact]
        public void Import_DedupesCaseInsensitively_AppendsAndPersists()
        {
            var settings = TestSettingsFactory.Create(typoTolerantSearchEnabled: true);
            var history = new SearchHistory(settings, _historyPath);
            settings.IsEnableHistory = true;
            history.AddToHistory("existing");

            var added = history.Import(new[] { "B", "b", "EXISTING", "a" });

            Assert.Equal(2, added);
            Assert.Equal(new[] { "existing", "B", "a" }, history.GetHistorySnapshot());

            // The import persisted through the normal save path (backgrounded by the service).
            history.WaitForPendingSave();
            var reloaded = new SearchHistory(settings, _historyPath);
            Assert.Equal(new[] { "existing", "B", "a" }, reloaded.GetHistorySnapshot());
        }

        [Fact]
        public void Import_BeyondCap_TrimsOldestKeepsNewest()
        {
            var settings = TestSettingsFactory.Create(typoTolerantSearchEnabled: true);
            var history = new SearchHistory(settings, _historyPath);
            settings.IsEnableHistory = true;

            var searches = Enumerable.Range(0, 60).Select(i => $"term {i}").ToList();
            var added = history.Import(searches);

            Assert.Equal(60, added);
            var snapshot = history.GetHistorySnapshot();
            Assert.Equal(50, snapshot.Count);
            // File order is most-recent-first, so "term 59" is the newest and must survive.
            Assert.Equal("term 59", snapshot.Last());
            Assert.Equal("term 10", snapshot[0]);
        }

        [Fact]
        public void Import_DisabledHistory_IsNoOp()
        {
            var settings = TestSettingsFactory.Create(typoTolerantSearchEnabled: true);
            var history = new SearchHistory(settings, _historyPath);
            settings.IsEnableHistory = false;

            Assert.Equal(0, history.Import(new[] { "a", "b" }));
            Assert.Empty(history.GetHistorySnapshot());
        }

        private static FilterProvider CreateFilterProvider(ISettings settings)
        {
            var names = new StubFilterNames();
            var defaultProvider = new DefaultFilterProvider(names, settings);
            var everythingProvider = new EverythingFilterProvider(
                names,
                new StubNotifier(),
                new StubShellDialogs(),
                settings
            );
            return new FilterProvider(defaultProvider, everythingProvider, settings);
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
