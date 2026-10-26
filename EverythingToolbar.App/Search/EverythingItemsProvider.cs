using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Core.Search;

namespace EverythingToolbar.App.Search
{
    public sealed class EverythingItemsProvider : IItemsProvider<SearchResult>
    {
        private readonly IEverythingClient _client;
        private readonly SearchQuery _query;
        private readonly Action<IList<SearchResult>>? _onResultsMaterialized;
        private readonly Action<int>? _onCountTimed;

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (_isBusy != value)
                {
                    _isBusy = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBusy)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public EverythingItemsProvider(
            IEverythingClient client,
            SearchQuery query,
            Action<IList<SearchResult>>? onResultsMaterialized = null,
            Action<int>? onCountTimed = null
        )
        {
            _client = client;
            _query = query;
            _onResultsMaterialized = onResultsMaterialized;
            _onCountTimed = onCountTimed;
        }

        public Task<int> FetchCount(int pageSize, bool isAsync, CancellationToken cancellationToken)
        {
            // The token cannot interrupt a query already in flight, but it does stop a superseded
            // one from ever starting. The stopwatch covers the whole count fetch (including any
            // client-side phases) — this is the wall time the user actually waited for.
            var stopwatch = _onCountTimed is null ? null : System.Diagnostics.Stopwatch.StartNew();
            if (!isAsync)
            {
                var count = _client.QueryCountSync(_query, pageSize, cancellationToken);
                ReportCountElapsed(stopwatch);
                return Task.FromResult(count);
            }

            return TrackBusyState(
                TrackCountAsync(_client.QueryCountAsync(_query, pageSize, cancellationToken), stopwatch)
            );
        }

        private async Task<int> TrackCountAsync(Task<int> task, System.Diagnostics.Stopwatch? stopwatch)
        {
            var count = await task;
            ReportCountElapsed(stopwatch);
            return count;
        }

        private void ReportCountElapsed(System.Diagnostics.Stopwatch? stopwatch)
        {
            if (stopwatch != null)
                _onCountTimed?.Invoke((int)stopwatch.ElapsedMilliseconds);
        }

        public async Task<IList<SearchResult>> FetchRange(
            int startIndex,
            int pageSize,
            bool isAsync,
            CancellationToken cancellationToken
        )
        {
            IList<SearchResult> data;
            if (!isAsync)
                data = _client.QueryRangeSync(_query, startIndex, pageSize, cancellationToken);
            else
                data = await TrackBusyState(_client.QueryRangeAsync(_query, startIndex, pageSize, cancellationToken));

            _onResultsMaterialized?.Invoke(data);

            return data;
        }

        public bool TryFetchCachedFirstPage(out IList<SearchResult> items)
        {
            return _client.TryReadCachedFirstPage(_query, out items);
        }

        private async Task<T> TrackBusyState<T>(Task<T> task)
        {
            if (task.IsCompleted)
                return await task;

            IsBusy = true;
            try
            {
                return await task;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
