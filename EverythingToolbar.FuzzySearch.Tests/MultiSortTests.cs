using System;
using System.Collections.Generic;
using System.Threading;
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
    /// Everything 1.5 multi-sort (secondary/tertiary sorts): SearchQuery carries them, SearchState
    /// builds them into the query in slot order, and tab snapshots round-trip them (session-only,
    /// never persisted to settings).
    /// </summary>
    public class MultiSortTests
    {
        private static (SearchState State, FilterProvider Filters, ISettings Settings) CreateSearchState()
        {
            var settings = TestSettingsFactory.Create(typoTolerantSearchEnabled: false);
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

        public class SearchQueryEquality
        {
            private static SearchQuery CreateQuery(IReadOnlyList<(SortBy, bool)>? secondarySorts)
            {
                return new SearchQuery("test", SortBy.Name, false, false, false, false, false, false, secondarySorts);
            }

            [Fact]
            public void NullSecondarySortsDefaultsToNull()
            {
                var query = new SearchQuery("test", SortBy.Name, false, false, false, false, false);

                Assert.Null(query.SecondarySorts);
            }

            [Fact]
            public void SameSecondarySortsListInstanceIsEqual()
            {
                var sorts = new List<(SortBy, bool)> { (SortBy.Size, true) };
                var a = CreateQuery(sorts);
                var b = CreateQuery(sorts);

                Assert.Equal(a, b);
            }

            [Fact]
            public void NullVersusNonNullSecondarySortsIsNotEqual()
            {
                Assert.NotEqual(CreateQuery(null), CreateQuery(new List<(SortBy, bool)> { (SortBy.Size, true) }));
            }

            [Fact]
            public void DifferentSecondarySortsListsAreNotEqual()
            {
                // The list is compared by reference (kept identity-stable by SearchState so the
                // pipe client can still key result-set reuse on query equality).
                var a = CreateQuery(new List<(SortBy, bool)> { (SortBy.Size, true) });
                var b = CreateQuery(new List<(SortBy, bool)> { (SortBy.Size, true) });

                Assert.NotEqual(a, b);
            }
        }

        public class SearchStateSecondarySorts
        {
            [Fact]
            public void BuildSearchQueryIncludesSecondarySortsInSlotOrder()
            {
                var (state, _, _) = CreateSearchState();

                state.SetSecondarySort(1, SortBy.DateModified, true);
                state.SetSecondarySort(0, SortBy.Size, false);

                var query = state.BuildSearchQuery();

                Assert.NotNull(query.SecondarySorts);
                Assert.Equal(2, query.SecondarySorts!.Count);
                Assert.Equal((SortBy.Size, false), query.SecondarySorts[0]);
                Assert.Equal((SortBy.DateModified, true), query.SecondarySorts[1]);
            }

            [Fact]
            public void SlotsBeyondMaxAreIgnored()
            {
                var (state, _, _) = CreateSearchState();

                state.SetSecondarySort(SearchState.MaxSecondarySorts, SortBy.Size, false);
                state.SetSecondarySort(-1, SortBy.Size, false);

                Assert.Null(state.SecondarySorts);
            }

            [Fact]
            public void SetSecondarySortReplacesListSoUnchangedQueriesStayEqual()
            {
                var (state, _, _) = CreateSearchState();

                var before = state.BuildSearchQuery();
                var again = state.BuildSearchQuery();
                Assert.Equal(before, again);

                state.SetSecondarySort(0, SortBy.Path, true);
                var after = state.BuildSearchQuery();

                Assert.NotEqual(before, after);
                Assert.Equal(after, state.BuildSearchQuery());
            }

            [Fact]
            public void ClearSecondarySortsRemovesThemAndRebuildIsNull()
            {
                var (state, _, _) = CreateSearchState();
                state.SetSecondarySort(0, SortBy.Size, true);

                state.ClearSecondarySorts();

                Assert.Null(state.SecondarySorts);
                Assert.Null(state.BuildSearchQuery().SecondarySorts);
            }

            [Fact]
            public void ClearSecondarySortsWithNoneSetIsNoOp()
            {
                var (state, _, _) = CreateSearchState();

                state.ClearSecondarySorts();

                Assert.Null(state.SecondarySorts);
            }
        }

        public class TabSnapshotRoundTrip
        {
            [Fact]
            public void SnapshotCarriesSecondarySortsAndRestoreRebuildsThem()
            {
                var (state, _, _) = CreateSearchState();
                state.SearchTerm = "multi sort test";
                state.SetSecondarySort(0, SortBy.Size, true);
                state.SetSecondarySort(1, SortBy.Path, false);

                var snapshot = state.CaptureSnapshot();
                state.ClearSecondarySorts();
                Assert.Null(state.SecondarySorts);

                state.RestoreSnapshot(snapshot);

                var query = state.BuildSearchQuery();
                Assert.NotNull(query.SecondarySorts);
                Assert.Equal(2, query.SecondarySorts!.Count);
                Assert.Equal((SortBy.Size, true), query.SecondarySorts[0]);
                Assert.Equal((SortBy.Path, false), query.SecondarySorts[1]);
            }

            [Fact]
            public void SnapshotOfStateWithoutSecondarySortsRestoresToEmpty()
            {
                var (state, _, _) = CreateSearchState();

                var tab = new SearchTab();
                state.CaptureSnapshotInto(tab);
                state.SetSecondarySort(0, SortBy.Size, true);
                state.RestoreSnapshot(tab);

                Assert.Null(state.SecondarySorts);
                Assert.Null(state.BuildSearchQuery().SecondarySorts);
            }

            [Fact]
            public void CopyIntoCopiesSecondarySorts()
            {
                var (state, _, _) = CreateSearchState();
                state.SetSecondarySort(0, SortBy.RunCount, false);

                var source = state.CaptureSnapshot();
                var target = new SearchTab();
                source.CopyInto(target);

                Assert.NotNull(target.SecondarySorts);
                Assert.Equal((SortBy.RunCount, false), target.SecondarySorts![0]);
            }
        }

        public class PipeClientFacingQuery
        {
            [Fact]
            public void ClientReceivesSecondarySortsInOrderAfterPrimary()
            {
                var (state, _, _) = CreateSearchState();
                state.SetSecondarySort(0, SortBy.Size, true);
                state.SetSecondarySort(1, SortBy.DateModified, false);

                var client = new FakeEverythingClient();
                var query = state.BuildSearchQuery();
                client.QueryCountSync(query, 10, CancellationToken.None);

                Assert.Equal(query, client.LastQuery);
                var receivedSorts = client.LastQuery!.SecondarySorts;
                Assert.NotNull(receivedSorts);
                Assert.Equal(
                    new List<(SortBy, bool)> { (SortBy.Size, true), (SortBy.DateModified, false) },
                    receivedSorts
                );
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
