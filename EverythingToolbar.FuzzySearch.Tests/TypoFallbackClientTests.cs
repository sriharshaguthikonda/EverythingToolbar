using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EverythingToolbar.App.Search;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Core.Search;
using EverythingToolbar.FuzzySearch;
using EverythingToolbar.FuzzySearch.Tests.Support;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    /// <summary>
    /// Pins the TypoFallbackClient contract: raw queries are authoritative, fallback fires only on
    /// zero results with the feature enabled and a non-regex query, and range calls follow the
    /// activated correction.
    /// </summary>
    public class TypoFallbackClientTests
    {
        private static SearchQuery Query(string text, bool useRegex = false)
        {
            return new SearchQuery(text, SortBy.Name, false, false, false, false, useRegex);
        }

        private sealed class RecordingClient : IEverythingClient
        {
            public readonly List<string> CountQueries = new();
            public readonly List<string> RangeQueries = new();
            public Func<string, int> CountFor { get; set; } = _ => 0;
            public IList<SearchResult> Results { get; set; } = new List<SearchResult>();

            public Task<int> QueryCountAsync(SearchQuery query, int pageSize, CancellationToken ct) =>
                Task.FromResult(QueryCountSync(query, pageSize, ct));

            public int QueryCountSync(SearchQuery query, int pageSize, CancellationToken ct)
            {
                CountQueries.Add(query.SearchText);
                return CountFor(query.SearchText);
            }

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
                return Results;
            }

            public bool TryReadCachedFirstPage(SearchQuery query, out IList<SearchResult> results)
            {
                RangeQueries.Add(query.SearchText);
                results = Results;
                return Results.Count > 0;
            }

            public Version GetEverythingVersion() => new(1, 5, 0);

            public void SetInstanceName(string name) { }

            public void IncrementRunCount(string path) { }

            public bool GetIsFastSort(SortBy sortBy, bool descending) => false;
        }

        private static FallbackQueryPlanner CreatePlanner()
        {
            var provider = new FakeCandidateProvider(
                new Dictionary<string, IReadOnlyList<TypoCandidate>>
                {
                    ["neurosicence"] = new[] { new TypoCandidate("neuroscience", 2, 10) },
                }
            );
            return new FallbackQueryPlanner(AliasStore.Empty, provider);
        }

        [Fact]
        public void RawQueryWithResults_NeverTriggersFallback()
        {
            var inner = new RecordingClient { CountFor = q => (q == "neuroscience" ? 7 : 5) };
            var decorator = new TypoFallbackClient(
                inner,
                CreatePlanner(),
                TestSettingsFactory.Create(typoTolerantSearchEnabled: true)
            );

            var count = decorator.QueryCountSync(Query("neuroscience"), 256, CancellationToken.None);

            Assert.Equal(7, count);
            Assert.Equal(["neuroscience"], inner.CountQueries); // raw only, no corrected probe
            Assert.Null(decorator.GetActiveFallback(Query("neuroscience")));
        }

        [Fact]
        public void ZeroResultRawQuery_CountsAndActivatesFallback()
        {
            var inner = new RecordingClient { CountFor = q => q.Contains("neuroscience") ? 4 : 0 };
            var decorator = new TypoFallbackClient(
                inner,
                CreatePlanner(),
                TestSettingsFactory.Create(typoTolerantSearchEnabled: true)
            );

            var query = Query("neurosicence");
            var count = decorator.QueryCountSync(query, 256, CancellationToken.None);

            Assert.Equal(4, count);
            Assert.Contains("<neurosicence|neuroscience>", inner.CountQueries);
            Assert.NotNull(decorator.GetActiveFallback(query));
        }

        [Fact]
        public void ZeroResultWithNoCandidates_ReturnsZeroWithoutCorrectedProbe()
        {
            var inner = new RecordingClient();
            var decorator = new TypoFallbackClient(
                inner,
                new FallbackQueryPlanner(AliasStore.Empty, FakeCandidateProvider.FromCorrections()),
                TestSettingsFactory.Create(typoTolerantSearchEnabled: true)
            );

            var count = decorator.QueryCountSync(Query("zzzzunknown"), 256, CancellationToken.None);

            Assert.Equal(0, count);
            Assert.Equal(["zzzzunknown"], inner.CountQueries);
        }

        [Fact]
        public void DisabledFeature_NeverPlansFallback()
        {
            var inner = new RecordingClient();
            var decorator = new TypoFallbackClient(
                inner,
                CreatePlanner(),
                TestSettingsFactory.Create(typoTolerantSearchEnabled: false)
            );

            var count = decorator.QueryCountSync(Query("neurosicence"), 256, CancellationToken.None);

            Assert.Equal(0, count);
            Assert.Equal(["neurosicence"], inner.CountQueries);
            Assert.Null(decorator.GetActiveFallback(Query("neurosicence")));
        }

        [Fact]
        public void RegexQuery_NeverPlansFallback()
        {
            var inner = new RecordingClient();
            var decorator = new TypoFallbackClient(
                inner,
                CreatePlanner(),
                TestSettingsFactory.Create(typoTolerantSearchEnabled: true)
            );

            var count = decorator.QueryCountSync(Query("neurosicence", useRegex: true), 256, CancellationToken.None);

            Assert.Equal(0, count);
            Assert.Equal(["neurosicence"], inner.CountQueries);
        }

        [Fact]
        public void RangeFollowsTheActivatedCorrection()
        {
            var inner = new RecordingClient { CountFor = q => q.Contains("neuroscience") ? 2 : 0 };
            inner.Results = new List<SearchResult>
            {
                new(@"C:\t\Neuroscience.txt", "Neuroscience.txt", @"C:\t\Neuroscience.txt", true, 0, default),
            };
            var decorator = new TypoFallbackClient(
                inner,
                CreatePlanner(),
                TestSettingsFactory.Create(typoTolerantSearchEnabled: true)
            );

            var query = Query("neurosicence");
            Assert.Equal(2, decorator.QueryCountSync(query, 256, CancellationToken.None));
            var rows = decorator.QueryRangeSync(query, 0, 256, CancellationToken.None);

            Assert.Single(rows);
            Assert.Equal(["neurosicence", "<neurosicence|neuroscience>"], inner.CountQueries);
            Assert.Equal(["<neurosicence|neuroscience>"], inner.RangeQueries);
        }

        [Fact]
        public void UncorrectableZeroResult_IsServedRawTwice()
        {
            var inner = new RecordingClient();
            var decorator = new TypoFallbackClient(
                inner,
                new FallbackQueryPlanner(AliasStore.Empty, FakeCandidateProvider.FromCorrections()),
                TestSettingsFactory.Create(typoTolerantSearchEnabled: true)
            );

            var query = Query("zzzzunknown");
            decorator.QueryCountSync(query, 256, CancellationToken.None);
            decorator.QueryCountSync(query, 256, CancellationToken.None);

            // The null plan is cached; no repeated planning probes for the same raw query.
            Assert.Equal(2, inner.CountQueries.Count(q => q == "zzzzunknown"));
            Assert.DoesNotContain(inner.CountQueries, q => q != "zzzzunknown");
        }
    }
}
