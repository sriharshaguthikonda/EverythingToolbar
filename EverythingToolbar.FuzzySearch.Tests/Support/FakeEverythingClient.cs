using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Core.Search;

namespace EverythingToolbar.FuzzySearch.Tests.Support
{
    /// <summary>Minimal IEverythingClient double for App-layer tests (no Everything needed).</summary>
    public sealed class FakeEverythingClient : IEverythingClient
    {
        public int CountToReturn { get; set; }
        public IList<SearchResult> ResultsToReturn { get; set; } = new List<SearchResult>();

        public Task<int> QueryCountAsync(SearchQuery query, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(CountToReturn);

        public int QueryCountSync(SearchQuery query, int pageSize, CancellationToken cancellationToken) =>
            CountToReturn;

        public Task<IList<SearchResult>> QueryRangeAsync(
            SearchQuery query,
            int startIndex,
            int pageSize,
            CancellationToken cancellationToken
        ) => Task.FromResult(ResultsToReturn);

        public IList<SearchResult> QueryRangeSync(
            SearchQuery query,
            int startIndex,
            int pageSize,
            CancellationToken cancellationToken
        ) => ResultsToReturn;

        public bool TryReadCachedFirstPage(SearchQuery query, out IList<SearchResult> results)
        {
            results = ResultsToReturn;
            return ResultsToReturn.Count > 0;
        }

        public Version GetEverythingVersion() => new(1, 5, 0);

        public void SetInstanceName(string name) { }

        public void IncrementRunCount(string path) { }

        public bool GetIsFastSort(SortBy sortBy, bool descending) => false;
    }
}
