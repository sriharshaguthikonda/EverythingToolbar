using System;
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
    /// Keeps the spelling vocabulary derived from Everything, never from a filesystem scan:
    /// every result page the UI materializes feeds its paths into the vocabulary, and an idle
    /// timer samples a few small pages at random offsets straight from the Everything index.
    /// Everything3 result-list change tracking is deliberately not used: per-query result lists
    /// here are ephemeral, so a cheap periodic refresh is the lower-risk route.
    /// </summary>
    public sealed class VocabularyRefresher : IDisposable
    {
        private const int SamplePages = 5;
        private const int SamplePageSize = 256;
        private const int LearnedPathsPerRebuild = 2000;
        private const int FirstRefreshDelayMs = 2 * 60 * 1000;
        private const int RefreshPeriodMs = 6 * 60 * 60 * 1000;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly Random Random = new();

        private readonly IEverythingClient _client;
        private readonly TokenVocabulary _vocabulary;
        private readonly SymSpellCandidateProvider _provider;
        private readonly ISettings _settings;
        private readonly object _gate = new();
        private Timer? _timer;
        private int _learnedPaths;

        public VocabularyRefresher(
            IEverythingClient client,
            TokenVocabulary vocabulary,
            SymSpellCandidateProvider provider,
            ISettings settings
        )
        {
            _client = client;
            _vocabulary = vocabulary;
            _provider = provider;
            _settings = settings;
        }

        public void Start()
        {
            lock (_gate)
            {
                _timer ??= new Timer(_ => SafeSample(), null, dueTime: FirstRefreshDelayMs, period: RefreshPeriodMs);
            }
        }

        /// <summary>Called with each materialized result page; learning must never throw.</summary>
        public void OnResultsMaterialized(IList<SearchResult> results)
        {
            if (!_settings.IsTypoTolerantSearchEnabled)
                return;

            try
            {
                foreach (var result in results)
                {
                    if (string.IsNullOrEmpty(result.FullPathAndFileName))
                        continue;

                    _vocabulary.AddPath(result.FullPathAndFileName);
                }

                var learned = Interlocked.Add(ref _learnedPaths, results.Count);
                if (learned / LearnedPathsPerRebuild > (learned - results.Count) / LearnedPathsPerRebuild)
                    RebuildInBackground();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Vocabulary learning failed");
            }
        }

        private void SafeSample()
        {
            try
            {
                SampleFromEverything();
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Vocabulary sampling skipped");
            }
        }

        private void SampleFromEverything()
        {
            if (!_settings.IsTypoTolerantSearchEnabled)
                return;

            var sampleQuery = new SearchQuery(
                SearchText: "",
                SortBy: default,
                SortDescending: false,
                MatchCase: false,
                MatchPath: false,
                MatchWholeWord: false,
                UseRegex: false
            );
            var total = _client.QueryCountSync(sampleQuery, SamplePageSize, CancellationToken.None);
            if (total <= 0)
                return;

            for (var page = 0; page < SamplePages; page++)
            {
                var offset = page == 0 ? 0 : Random.Next(Math.Min(total, int.MaxValue - 1));
                var results = _client.QueryRangeSync(sampleQuery, offset, SamplePageSize, CancellationToken.None);
                if (results.Count == 0)
                    continue;

                foreach (var result in results)
                    _vocabulary.AddPath(result.FullPathAndFileName);
            }

            RebuildInBackground();
            Logger.Info(
                "Spelling vocabulary refreshed: {0} words, {1} compounds",
                _vocabulary.WordCount,
                _vocabulary.CompoundCount
            );
        }

        private void RebuildInBackground()
        {
            Task.Run(() =>
            {
                try
                {
                    _provider.Rebuild();
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Typo index rebuild failed");
                }
            });
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
