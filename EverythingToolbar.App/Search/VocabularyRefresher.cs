using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EverythingToolbar.App.Helpers;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Core.Search;
using EverythingToolbar.FuzzySearch;
using NLog;

namespace EverythingToolbar.App.Search
{
    public enum TypoVocabularyState
    {
        Disabled,
        Building,
        Ready,
        Refreshing,
        Failed,
    }

    /// <summary>
    /// Owns the spelling vocabulary lifecycle, always derived from Everything through
    /// IEverythingClient (never a filesystem scan): on first start with no usable cache it
    /// bootstraps immediately off the UI thread by paging a broad, uniformly strided sample of
    /// the index; a versioned, instance-keyed cache makes later startups instant; a slow periodic
    /// refresh plus learning from materialized results keeps it current. Everything3 change
    /// tracking stays deliberately unused (per-query result lists are ephemeral here).
    /// </summary>
    public sealed class VocabularyRefresher : INotifyPropertyChanged, IDisposable
    {
        private const int BootstrapPageSize = 1000;
        internal const int MaxBootstrapPaths = 150_000;
        private const int SamplePages = 5;
        private const int SamplePageSize = 256;
        internal const int LearnedPathsPerRebuild = 2000;
        private const int RefreshPeriodMs = 6 * 60 * 60 * 1000;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly Random Random = new();

        private readonly IEverythingClient _client;
        private readonly TokenVocabulary _vocabulary;
        private readonly SymSpellCandidateProvider _provider;
        private readonly ISettings _settings;
        private readonly string _cacheDirectory;
        private readonly object _gate = new();

        private Timer? _timer;
        private CancellationTokenSource _cts = new();
        private Task? _initialPipeline;
        private int _started;
        private int _learnedPaths;
        private int _refreshQueued;
        private TypoVocabularyState _state = TypoVocabularyState.Disabled;

        public VocabularyRefresher(
            IEverythingClient client,
            TokenVocabulary vocabulary,
            SymSpellCandidateProvider provider,
            ISettings settings,
            string? cacheDirectory = null
        )
        {
            _client = client;
            _vocabulary = vocabulary;
            _provider = provider;
            _settings = settings;
            _cacheDirectory = cacheDirectory ?? ConfigPaths.GetConfigDirectory();
            _settings.PropertyChanged += OnSettingsPropertyChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public TypoVocabularyState State
        {
            get => _state;
            private set
            {
                if (_state == value)
                    return;
                _state = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WordCount)));
            }
        }

        /// <summary>Live vocabulary size for UI display ("Ready — N spelling terms").</summary>
        public int WordCount => _vocabulary.WordCount;

        /// <summary>Starts the cache-load/bootstrap pipeline once; safe to call repeatedly.</summary>
        public void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) == 1)
                return;

            lock (_gate)
            {
                _initialPipeline ??= RunPipelineAsync();
            }
        }

        /// <summary>Awaits the initial cache-load/bootstrap pipeline (primarily for tests).</summary>
        public async Task WaitForReadyAsync(TimeSpan timeout)
        {
            Start();
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                var current = State;
                if (current is TypoVocabularyState.Ready or TypoVocabularyState.Failed or TypoVocabularyState.Disabled)
                {
                    return;
                }

                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("Spelling vocabulary did not become ready in time. State=" + current);
                }

                await Task.Delay(25).ConfigureAwait(false);
            }
        }

        /// <summary>Manual/periodic refresh; repeated requests while one runs are coalesced.</summary>
        public void RequestRefresh()
        {
            if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
                return;

            Task.Run(async () =>
            {
                try
                {
                    await RefreshFromEverythingAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "Spelling vocabulary refresh skipped");
                }
                finally
                {
                    Interlocked.Exchange(ref _refreshQueued, 0);
                }
            });
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
                    RebuildIndexInBackground(persistCache: true);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Vocabulary learning failed");
            }
        }

        private async Task RunPipelineAsync()
        {
            try
            {
                if (!_settings.IsTypoTolerantSearchEnabled)
                {
                    State = TypoVocabularyState.Disabled;
                    return;
                }

                var instance = _settings.InstanceName ?? string.Empty;
                var cachePath = SpellingVocabularyCache.GetCachePath(_cacheDirectory, instance);
                var cache = SpellingVocabularyCache.Load(cachePath, instance);
                if (cache is not null)
                {
                    _vocabulary.Restore(
                        cache.Words.Select(w => new VocabularyEntry(w.N, w.D, w.F, IsCompound: false)),
                        cache.Compounds.Select(c => new VocabularyEntry(c.N, c.D, c.F, IsCompound: true))
                    );
                    State = TypoVocabularyState.Refreshing;
                    await Task.Run(() => _provider.Rebuild(), _cts.Token).ConfigureAwait(false);
                    State = TypoVocabularyState.Ready;
                    Logger.Info(
                        "Spelling cache loaded: terms={0}, compounds={1}",
                        _vocabulary.WordCount,
                        _vocabulary.CompoundCount
                    );
                    StartPeriodicTimer();
                    return;
                }

                Logger.Info("Spelling cache absent or incompatible; bootstrapping vocabulary from Everything");
                await BootstrapAsync(instance, cachePath).ConfigureAwait(false);
                StartPeriodicTimer();
            }
            catch (OperationCanceledException)
            {
                // Superseded (instance change or shutdown); the new pipeline owns the state.
                Logger.Info("Spelling pipeline canceled (instance change or shutdown)");
            }
            catch (Exception ex)
            {
                State = TypoVocabularyState.Failed;
                Logger.Warn(ex, "Spelling vocabulary bootstrap failed; exact search is unaffected");
            }
        }

        private async Task BootstrapAsync(string instance, string cachePath)
        {
            State = TypoVocabularyState.Building;
            var ct = _cts.Token;

            var paths = await Task.Run(
                    () =>
                    {
                        var query = CreateEmptyQuery();
                        var total = _client.QueryCountSync(query, BootstrapPageSize, ct);
                        if (total <= 0)
                        {
                            return 0L;
                        }

                        var totalPages = (int)Math.Ceiling(total / (double)BootstrapPageSize);
                        // Uniform stride so a multi-million-row index is covered evenly within the
                        // path budget instead of only its alphabetically-first slice.
                        var stride = Math.Max(
                            1,
                            (int)Math.Ceiling(totalPages / (double)(MaxBootstrapPaths / BootstrapPageSize))
                        );
                        long learned = 0;
                        for (var page = 0; page < totalPages && learned < MaxBootstrapPaths; page += stride)
                        {
                            ct.ThrowIfCancellationRequested();
                            var rows = _client.QueryRangeSync(query, page * BootstrapPageSize, BootstrapPageSize, ct);
                            foreach (var row in rows)
                            {
                                ct.ThrowIfCancellationRequested();
                                if (string.IsNullOrEmpty(row.FullPathAndFileName))
                                    continue;
                                _vocabulary.AddPath(row.FullPathAndFileName);
                                learned++;
                            }
                        }

                        return learned;
                    },
                    ct
                )
                .ConfigureAwait(false);

            await Task.Run(() => _provider.Rebuild(), ct).ConfigureAwait(false);
            var snapshot = await Task.Run(() => SpellingVocabularyCache.CreateSnapshot(instance, _vocabulary), ct)
                .ConfigureAwait(false);
            await Task.Run(() => SpellingVocabularyCache.Save(cachePath, snapshot), ct).ConfigureAwait(false);
            State = TypoVocabularyState.Ready;
            Logger.Info(
                "Spelling bootstrap complete: paths={0}, words={1}, compounds={2}",
                paths,
                _vocabulary.WordCount,
                _vocabulary.CompoundCount
            );
        }

        private async Task RefreshFromEverythingAsync()
        {
            if (!_settings.IsTypoTolerantSearchEnabled)
                return;

            State = TypoVocabularyState.Refreshing;
            var ct = _cts.Token;
            await Task.Run(
                    () =>
                    {
                        var query = CreateEmptyQuery();
                        var total = _client.QueryCountSync(query, SamplePageSize, ct);
                        if (total <= 0)
                            return;

                        for (var page = 0; page < SamplePages; page++)
                        {
                            var offset = page == 0 ? 0 : Random.Next(Math.Min(total, int.MaxValue - 1));
                            var rows = _client.QueryRangeSync(query, offset, SamplePageSize, ct);
                            foreach (var row in rows)
                            {
                                if (!string.IsNullOrEmpty(row.FullPathAndFileName))
                                    _vocabulary.AddPath(row.FullPathAndFileName);
                            }
                        }
                    },
                    ct
                )
                .ConfigureAwait(false);

            await Task.Run(() => _provider.Rebuild(), ct).ConfigureAwait(false);
            PersistCache();
            State = TypoVocabularyState.Ready;
            Logger.Info(
                "Spelling vocabulary refreshed: words={0}, compounds={1}",
                _vocabulary.WordCount,
                _vocabulary.CompoundCount
            );
        }

        private void PersistCache()
        {
            try
            {
                var instance = _settings.InstanceName ?? string.Empty;
                var cachePath = SpellingVocabularyCache.GetCachePath(_cacheDirectory, instance);
                SpellingVocabularyCache.Save(cachePath, SpellingVocabularyCache.CreateSnapshot(instance, _vocabulary));
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Spelling cache persist failed; will retry on next refresh");
            }
        }

        private void RebuildIndexInBackground(bool persistCache)
        {
            Task.Run(() =>
            {
                try
                {
                    _provider.Rebuild();
                    if (persistCache)
                        PersistCache();
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Typo index rebuild failed");
                }
            });
        }

        private void StartPeriodicTimer()
        {
            lock (_gate)
            {
                _timer ??= new Timer(_ => RequestRefresh(), null, dueTime: RefreshPeriodMs, period: RefreshPeriodMs);
            }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ISettings.InstanceName))
            {
                // Vocabulary from one instance must never leak into another.
                RestartPipeline();
            }
            else if (e.PropertyName == nameof(ISettings.IsTypoTolerantSearchEnabled))
            {
                if (_settings.IsTypoTolerantSearchEnabled)
                    RestartPipeline();
                else
                    State = TypoVocabularyState.Disabled;
            }
        }

        private void RestartPipeline()
        {
            if (Interlocked.Exchange(ref _started, 1) == 0)
                return; // Start() has not run yet; it will pick up current settings.

            Task old;
            lock (_gate)
            {
                old = _initialPipeline!;
                // Cancel first, swap the token source, clear the vocabulary, and only then start
                // the new pipeline: it must capture the fresh token and a clean vocabulary so
                // terms from one Everything instance never leak into another.
                _cts.Cancel();
                _cts.Dispose();
                _cts = new CancellationTokenSource();
                _vocabulary.Clear();
                _initialPipeline = RunPipelineAsync();
            }

            RebuildIndexInBackground(persistCache: false);
            _ = old.ContinueWith(
                t => Logger.Debug(t.Exception, "Previous spelling pipeline ended"),
                TaskContinuationOptions.OnlyOnFaulted
            );
        }

        private static SearchQuery CreateEmptyQuery()
        {
            return new SearchQuery("", default, false, false, false, false, false);
        }

        public void Dispose()
        {
            _settings.PropertyChanged -= OnSettingsPropertyChanged;
            _timer?.Dispose();
            _cts.Cancel();
        }
    }
}
