using System;
using System.Collections.Generic;
using System.Linq;
using EverythingToolbar.App.SettingsSearch;
using EverythingToolbar.Properties;
using EverythingToolbar.Settings;
using Xunit;
using AboutPage = EverythingToolbar.Settings.About;
using AdvancedPage = EverythingToolbar.Settings.Advanced;
using CustomActionsPage = EverythingToolbar.Settings.CustomActions;
using FiltersPage = EverythingToolbar.Settings.Filters;
using SearchPage = EverythingToolbar.Settings.Search;
using ShortcutsPage = EverythingToolbar.Settings.Shortcuts;
using UserInterfacePage = EverythingToolbar.Settings.UserInterface;

namespace EverythingToolbar.FuzzySearch.Tests
{
    /// <summary>
    /// Settings search: filter semantics over a synthetic catalog (deterministic matching rules)
    /// plus discoverability of the real production catalog - the new spelling settings must be
    /// reachable by their natural words, and hotkey/theme searches must cross pages.
    /// </summary>
    public sealed class SettingsSearchFilterTests
    {
        private static readonly IReadOnlyList<SettingsSearchEntry> Catalog = SettingsSearchIndex.Build();

        private sealed class PageA { }

        private sealed class PageB { }

        private static SettingsSearchEntry Synthetic(string title, string help, Type page, string[]? keywords = null) =>
            new(() => title, () => help, () => page.Name, page, null, keywords);

        [Fact]
        public void EmptyQuery_ReturnsNothing()
        {
            Assert.Empty(SettingsSearchFilter.Filter(Catalog, ""));
            Assert.Empty(SettingsSearchFilter.Filter(Catalog, "   "));
            Assert.Empty(SettingsSearchFilter.Filter(Catalog, null!));
        }

        [Fact]
        public void NoResultQuery_ReturnsNothing()
        {
            Assert.Empty(SettingsSearchFilter.Filter(Catalog, "zzzqqqxx"));
        }

        [Fact]
        public void Query_IsCaseInsensitiveAndWhitespaceTolerant()
        {
            Assert.NotEmpty(SettingsSearchFilter.Filter(Catalog, "SPELLING"));
            Assert.NotEmpty(SettingsSearchFilter.Filter(Catalog, "  spelling  "));
            Assert.NotEmpty(SettingsSearchFilter.Filter(Catalog, "Spelling Index Distance"));
        }

        [Fact]
        public void SubstringTokens_MatchAcrossTitleHelpAndKeywords()
        {
            var bySubstring = SettingsSearchFilter.Filter(Catalog, "dist");
            Assert.Contains(bySubstring, r => r.Title() == Resources.SettingsTypoMaxIndexDistance);

            // "hotkey" appears only in keywords, not in the setting's visible title.
            var byKeyword = SettingsSearchFilter.Filter(Catalog, "hotkey");
            Assert.Contains(byKeyword, r => r.Title() == Resources.SettingsOpenSearchWindow);
        }

        [Fact]
        public void MultipleTokens_MustAllMatch()
        {
            var both = SettingsSearchFilter.Filter(Catalog, "long word");
            Assert.Contains(both, r => r.Title() == Resources.SettingsTypoLongWordDistance);
            Assert.Contains(both, r => r.Title() == Resources.SettingsTypoLongWordThreshold);

            // A token that eliminates every candidate yields nothing (AND semantics).
            Assert.Empty(SettingsSearchFilter.Filter(Catalog, "long zzzqqqxx"));
        }

        [Fact]
        public void OneTerm_MatchesSeveralSettings()
        {
            var results = SettingsSearchFilter.Filter(Catalog, "distance");
            Assert.True(results.Count(r => r.PageType == typeof(SearchPage)) >= 2);
        }

        [Fact]
        public void Search_ReachesSettingsOnOtherPages()
        {
            // "theme" only exists on the user-interface and advanced pages; it must be found from
            // anywhere, not just the currently selected page.
            var results = SettingsSearchFilter.Filter(Catalog, "theme");
            Assert.NotEmpty(results);
            Assert.Contains(results, r => r.PageType == typeof(UserInterfacePage));
        }

        [Fact]
        public void Results_AreDeterministic()
        {
            var first = SettingsSearchFilter.Filter(Catalog, "distance");
            var second = SettingsSearchFilter.Filter(Catalog, "distance");
            Assert.Equal(
                first.Select(r => (r.Title(), r.PageName())).ToList(),
                second.Select(r => (r.Title(), r.PageName())).ToList()
            );

            // Ordering: page, then title - never catalog insertion order.
            var ordered = first.Select(r => (r.PageType.FullName, r.Title())).ToList();
            Assert.Equal(ordered.OrderBy(r => r.Item1, StringComparer.Ordinal).ToList(), ordered);
        }

        [Fact]
        public void DuplicateTitles_AreAllReturned()
        {
            var catalog = new[]
            {
                Synthetic("Same Title", "help one", typeof(PageA)),
                Synthetic("Same Title", "help two", typeof(PageB)),
            };

            var results = SettingsSearchFilter.Filter(catalog, "same");

            Assert.Equal(2, results.Count);
        }

        [Fact]
        public void ClearingQuery_RestoresEmptyResultState()
        {
            Assert.NotEmpty(SettingsSearchFilter.Filter(Catalog, "spelling"));
            Assert.Empty(SettingsSearchFilter.Filter(Catalog, ""));
        }

        [Fact]
        public void PageName_IsMatchable()
        {
            var results = SettingsSearchFilter.Filter(Catalog, "shortcuts");
            Assert.Contains(results, r => r.PageType == typeof(ShortcutsPage));
        }

        [Fact]
        public void NavigationTargets_AreSettingsPages()
        {
            var allowed = new[]
            {
                typeof(AboutPage),
                typeof(SearchPage),
                typeof(UserInterfacePage),
                typeof(FiltersPage),
                typeof(CustomActionsPage),
                typeof(ShortcutsPage),
                typeof(AdvancedPage),
            };
            Assert.All(Catalog, entry => Assert.Contains(entry.PageType, allowed));
        }

        [Fact]
        public void AnchoredEntries_ResolveToVisibleControlTitles()
        {
            // Every anchored entry must resolve to a non-empty string: it is matched against the
            // Title of a SettingItem on the target page.
            Assert.All(
                Catalog.Where(e => e.AnchorTitle is not null),
                entry => Assert.False(string.IsNullOrWhiteSpace(entry.AnchorTitle!()))
            );
        }

        [Fact]
        public void SpellingSettings_AreDiscoverableByRequiredTerms()
        {
            var expected = new[]
            {
                Resources.SettingsTypoTolerantSearch,
                Resources.SettingsTypoMaxIndexDistance,
                Resources.SettingsTypoLongWordDistance,
            };

            foreach (var term in new[] { "spelling", "distance", "typo", "index" })
            {
                var results = SettingsSearchFilter.Filter(Catalog, term);
                foreach (var title in expected)
                {
                    Assert.True(
                        results.Any(r => r.Title() == title),
                        $"term '{term}' must surface the setting '{title}'"
                    );
                }
            }
        }

        [Fact]
        public void Search_IsInstantAtCurrentCatalogSize()
        {
            // Deterministic latency measure, not a benchmark: filtering the full catalog a
            // thousand times must stay well under a second so search feels instant inside the
            // Preferences window.
            var queries = new[] { "spelling", "distance", "typo", "index", "hotkey", "theme", "a", "zzz" };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < 1000; i++)
            {
                SettingsSearchFilter.Filter(Catalog, queries[i % queries.Length]);
            }

            watch.Stop();
            Assert.True(
                watch.Elapsed.TotalMilliseconds < 1000,
                $"1000 filtered queries took {watch.Elapsed.TotalMilliseconds:F1} ms"
            );
        }
    }
}
