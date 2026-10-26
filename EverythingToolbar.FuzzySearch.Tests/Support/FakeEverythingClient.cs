using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Core.Search;

namespace EverythingToolbar.FuzzySearch.Tests.Support
{
    /// <summary>
    /// Minimal IEverythingClient double for App-layer tests (no Everything needed). When
    /// PathCorpus is set it behaves like a pageable Everything index over that path list and
    /// records how often it was enumerated.
    /// </summary>
    public sealed class FakeEverythingClient : IEverythingClient
    {
        public int CountToReturn { get; set; }
        public IList<SearchResult> Results { get; set; } = new List<SearchResult>();
        public IReadOnlyList<string>? PathCorpus { get; set; }
        public int CorpusCountQueries { get; private set; }
        public int CorpusRangeQueries { get; private set; }

        public Task<int> QueryCountAsync(SearchQuery query, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(QueryCountSync(query, pageSize, cancellationToken));

        public int QueryCountSync(SearchQuery query, int pageSize, CancellationToken cancellationToken)
        {
            if (PathCorpus is null)
                return CountToReturn;

            CorpusCountQueries++;
            return MatchingPaths(query.SearchText).Count;
        }

        public Task<IList<SearchResult>> QueryRangeAsync(
            SearchQuery query,
            int startIndex,
            int pageSize,
            CancellationToken cancellationToken
        ) => Task.FromResult(QueryRangeSync(query, startIndex, pageSize, cancellationToken));

        public IList<SearchResult> QueryRangeSync(
            SearchQuery query,
            int startIndex,
            int pageSize,
            CancellationToken cancellationToken
        )
        {
            if (PathCorpus is null)
                return Results;

            CorpusRangeQueries++;
            return MatchingPaths(query.SearchText)
                .Skip(startIndex)
                .Take(pageSize)
                .Select(path => new SearchResult(
                    path,
                    Path.GetFileName(path),
                    path,
                    IsFile: true,
                    FileSize: 0,
                    default
                ))
                .ToList();
        }

        /// <summary>
        /// Tiny Everything-like matcher for tests: space-separated AND terms, with &lt;a|b&gt;
        /// groups matching when any alternative is a case-insensitive substring of the path.
        /// </summary>
        private List<string> MatchingPaths(string searchText)
        {
            if (PathCorpus is null)
                return new List<string>();

            var tokens = searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            IEnumerable<string> matches = PathCorpus;
            foreach (var token in tokens)
            {
                if (token.Length > 1 && token.StartsWith('<') && token.EndsWith('>'))
                {
                    var alternatives = token[1..^1].Split('|');
                    matches = matches.Where(path =>
                        alternatives.Any(alt => path.Contains(alt, StringComparison.OrdinalIgnoreCase))
                    );
                }
                else
                {
                    matches = matches.Where(path => path.Contains(token, StringComparison.OrdinalIgnoreCase));
                }
            }

            return matches.ToList();
        }

        public bool TryReadCachedFirstPage(SearchQuery query, out IList<SearchResult> results)
        {
            results = Results;
            return Results.Count > 0;
        }

        public Version GetEverythingVersion() => new(1, 5, 0);

        public void SetInstanceName(string name) { }

        public void IncrementRunCount(string path) { }

        public bool GetIsFastSort(SortBy sortBy, bool descending) => false;
    }
}
