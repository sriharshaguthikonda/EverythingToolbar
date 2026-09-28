using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Core.Search;
using EverythingToolbar.FuzzySearch;
using NLog;

namespace EverythingToolbar.App.Search
{
    /// <summary>
    /// Decorator around the Everything client: the raw query always runs first and its result count
    /// is authoritative. Only when the raw query returns zero results, the setting is on, and the
    /// query contains correctable plain literals, is a fallback query planned and counted. Whatever
    /// reaches Everything stays an Everything query — this layer never invents results.
    /// </summary>
    public sealed class TypoFallbackClient : IEverythingClient
    {
        private const int MaxCachedPlans = 128;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly IEverythingClient _inner;
        private readonly FallbackQueryPlanner _planner;
        private readonly ISettings _settings;
        private readonly ConcurrentDictionary<SearchQuery, FallbackPlan?> _plans = new();

        public TypoFallbackClient(IEverythingClient inner, FallbackQueryPlanner planner, ISettings settings)
        {
            _inner = inner;
            _planner = planner;
            _settings = settings;
        }

        /// <summary>Raised when a zero-result query was answered by a corrected fallback query.</summary>
        public event Action<SearchQuery, FallbackPlan, int>? FallbackActivated;

        public FallbackPlan? LastFallback { get; private set; }

        public SearchQuery? LastFallbackQuery { get; private set; }

        /// <summary>The fallback plan that is active for this exact raw query, if any.</summary>
        public FallbackPlan? GetActiveFallback(SearchQuery rawQuery)
        {
            return LastFallbackQuery == rawQuery ? LastFallback : null;
        }

        public async Task<int> QueryCountAsync(SearchQuery query, int pageSize, CancellationToken cancellationToken)
        {
            var count = await _inner.QueryCountAsync(query, pageSize, cancellationToken).ConfigureAwait(false);
            if (count != 0)
                return count;

            return await CountFallbackAsync(query, pageSize, cancellationToken).ConfigureAwait(false);
        }

        public int QueryCountSync(SearchQuery query, int pageSize, CancellationToken cancellationToken)
        {
            var count = _inner.QueryCountSync(query, pageSize, cancellationToken);
            if (count != 0)
                return count;

            return CountFallback(query, pageSize, isAsync: false, cancellationToken);
        }

        public Task<IList<SearchResult>> QueryRangeAsync(
            SearchQuery query,
            int startIndex,
            int pageSize,
            CancellationToken cancellationToken
        )
        {
            return _inner.QueryRangeAsync(Resolve(query), startIndex, pageSize, cancellationToken);
        }

        public IList<SearchResult> QueryRangeSync(
            SearchQuery query,
            int startIndex,
            int pageSize,
            CancellationToken cancellationToken
        )
        {
            return _inner.QueryRangeSync(Resolve(query), startIndex, pageSize, cancellationToken);
        }

        public bool TryReadCachedFirstPage(SearchQuery query, out IList<SearchResult> results)
        {
            return _inner.TryReadCachedFirstPage(Resolve(query), out results);
        }

        public Version GetEverythingVersion() => _inner.GetEverythingVersion();

        public void SetInstanceName(string name)
        {
            _plans.Clear();
            _inner.SetInstanceName(name);
        }

        public void IncrementRunCount(string path) => _inner.IncrementRunCount(path);

        public bool GetIsFastSort(SortBy sortBy, bool descending) => _inner.GetIsFastSort(sortBy, descending);

        private async Task<int> CountFallbackAsync(SearchQuery query, int pageSize, CancellationToken cancellationToken)
        {
            var plan = GetPlan(query, cancellationToken);
            if (plan is null)
                return 0;

            var count = await _inner
                .QueryCountAsync(WithCorrectedText(query, plan), pageSize, cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
                return 0;

            Activate(query, plan, count);
            return count;
        }

        private int CountFallback(SearchQuery query, int pageSize, bool isAsync, CancellationToken cancellationToken)
        {
            var plan = GetPlan(query, cancellationToken);
            if (plan is null)
                return 0;

            var count = _inner.QueryCountSync(WithCorrectedText(query, plan), pageSize, cancellationToken);
            if (count == 0)
                return 0;

            Activate(query, plan, count);
            return count;
        }

        private FallbackPlan? GetPlan(SearchQuery query, CancellationToken cancellationToken)
        {
            if (!_settings.IsTypoTolerantSearchEnabled || query.UseRegex)
                return null;

            if (_plans.Count >= MaxCachedPlans)
                _plans.Clear();

            return _plans.GetOrAdd(query, q => _planner.Plan(q.SearchText, cancellationToken));
        }

        private SearchQuery Resolve(SearchQuery rawQuery)
        {
            return _plans.TryGetValue(rawQuery, out var plan) && plan is not null
                ? WithCorrectedText(rawQuery, plan)
                : rawQuery;
        }

        private static SearchQuery WithCorrectedText(SearchQuery query, FallbackPlan plan)
        {
            return query with { SearchText = plan.CorrectedQuery };
        }

        private void Activate(SearchQuery rawQuery, FallbackPlan plan, int count)
        {
            LastFallback = plan;
            LastFallbackQuery = rawQuery;
            Logger.Info(
                "Typo fallback: \"{0}\" -> \"{1}\" ({2} results)",
                rawQuery.SearchText,
                plan.CorrectedQuery,
                count
            );
            FallbackActivated?.Invoke(rawQuery, plan, count);
        }
    }
}
