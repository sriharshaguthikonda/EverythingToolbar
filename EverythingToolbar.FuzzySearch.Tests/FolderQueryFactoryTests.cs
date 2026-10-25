using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EverythingToolbar.App;
using EverythingToolbar.App.Search;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Core.Search;
using EverythingToolbar.FuzzySearch;
using EverythingToolbar.FuzzySearch.Tests.Support;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    public class FolderQueryFactoryTests
    {
        [Theory]
        [InlineData("C:\\Users", "parent:\"C:\\Users\" folder:")]
        [InlineData("C:\\Users\\", "parent:\"C:\\Users\" folder:")]
        [InlineData("C:\\", "parent:\"C:\\\" folder:")]
        [InlineData("C:", "parent:\"C:\\\" folder:")]
        [InlineData("\\\\server\\share", "parent:\"\\\\server\\share\" folder:")]
        [InlineData("\\\\server\\share\\", "parent:\"\\\\server\\share\" folder:")]
        public void BuildChildrenQuery_ProducesExactParentFolderText(string folderPath, string expected)
        {
            var query = FolderQueryFactory.BuildChildrenQuery(folderPath);

            Assert.Equal(expected, query.SearchText);
            Assert.Equal(SortBy.Name, query.SortBy);
            Assert.False(query.SortDescending);
            Assert.False(query.UseRegex);
        }

        [Theory]
        [InlineData("C:\\Program Files", "parent:\"C:\\Program Files\"")]
        [InlineData("C:\\Program Files\\ ", "parent:\"C:\\Program Files\"")]
        public void BuildNavigateQuery_OmitsFolderSuffix(string folderPath, string expected)
        {
            var query = FolderQueryFactory.BuildNavigateQuery(folderPath);

            Assert.Equal(expected, query.SearchText);
        }

        [Fact]
        public void NavigateQuery_DiffersFromChildrenQuery_ByFolderSuffixOnly()
        {
            var children = FolderQueryFactory.BuildChildrenQuery("C:\\Tools");
            var navigate = FolderQueryFactory.BuildNavigateQuery("C:\\Tools");

            Assert.Equal(navigate.SearchText + " folder:", children.SearchText);
        }

        [Fact]
        public void BuildChildrenQuery_EmptyPath_ProducesEmptyQuotedTerm()
        {
            var query = FolderQueryFactory.BuildChildrenQuery("");

            Assert.Equal("parent:\"\" folder:", query.SearchText);
        }
    }

    /// <summary>
    /// Pins the pass-through guarantee the folder sidebar relies on: advanced-syntax queries built
    /// by <see cref="FolderQueryFactory"/> are never rewritten by the typo fallback planner, because
    /// every term (property prefix, quoted path, folder: operator) classifies as structural.
    /// </summary>
    public class SidebarQueryClassifierPassThroughTests
    {
        private static SearchQuery Query(string text)
        {
            return new SearchQuery(text, SortBy.Name, false, false, false, false, false);
        }

        [Theory]
        [InlineData("parent:\"C:\\Program Files\" folder:")]
        [InlineData("parent:\"C:\\\" folder:")]
        [InlineData("parent:\"\\\\server\\share\"")]
        public void Planner_NeverRewritesSidebarQueries(string searchText)
        {
            var planner = new FallbackQueryPlanner();
            var query = Query(searchText);

            var terms = new SafeLiteralClassifier().Classify(query.SearchText);
            Assert.All(terms, t => Assert.False(SafeLiteralClassifier.IsCorrectable(t)));

            // No aliases, no candidates: an all-structural query yields no corrections and no plan.
            Assert.Null(planner.Plan(query.SearchText));
        }

        [Fact]
        public void TypoFallbackClient_RangeCall_SendsSidebarQueryVerbatim()
        {
            var inner = new RecordingStubClient();
            var client = new TypoFallbackClient(inner, new FallbackQueryPlanner(), TestSettingsFactory.Create(false));
            var query = FolderQueryFactory.BuildChildrenQuery("C:\\Program Files");

            var results = client.QueryRangeSync(query, 0, 500, CancellationToken.None);

            Assert.Equal("parent:\"C:\\Program Files\" folder:", inner.RangeQueries.Single());
            Assert.Equal(3, results.Count);
        }

        private sealed class RecordingStubClient : IEverythingClient
        {
            public readonly List<string> RangeQueries = new();

            public Task<int> QueryCountAsync(SearchQuery query, int pageSize, CancellationToken ct) =>
                Task.FromResult(1);

            public int QueryCountSync(SearchQuery query, int pageSize, CancellationToken ct) => 1;

            public Task<IList<SearchResult>> QueryRangeAsync(
                SearchQuery query,
                int startIndex,
                int pageSize,
                CancellationToken ct
            ) => Task.FromResult(QueryRangeSync(query, startIndex, pageSize, ct));

            public IList<SearchResult> QueryRangeSync(
                SearchQuery query,
                int startIndex,
                int pageSize,
                CancellationToken ct
            )
            {
                RangeQueries.Add(query.SearchText);
                return new List<SearchResult> { CreateResult(), CreateResult(), CreateResult() };
            }

            public bool TryReadCachedFirstPage(SearchQuery query, out IList<SearchResult> results)
            {
                results = new List<SearchResult>();
                return false;
            }

            public Version GetEverythingVersion() => new(1, 5, 0);

            public void SetInstanceName(string name) { }

            public void IncrementRunCount(string path) { }

            public bool GetIsFastSort(SortBy sortBy, bool descending) => true;

            private static SearchResult CreateResult()
            {
                return new SearchResult("", "", "C:\\Program Files\\Common Files", false, 0, default);
            }
        }
    }
}
