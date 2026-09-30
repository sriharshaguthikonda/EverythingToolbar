// IDE0005 false-positives `using System;` here even though Guid/IDisposable/TimeSpan require it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EverythingToolbar.App;
using EverythingToolbar.App.Search;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Core.Search;
using EverythingToolbar.FuzzySearch.Tests.Support;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    /// <summary>
    /// First-run bootstrap and cache lifecycle, exercised through the production path:
    /// VocabularyRefresher -> TokenVocabulary -> SymSpellCandidateProvider -> FallbackQueryPlanner
    /// -> TypoFallbackClient. The candidate provider is never pre-populated.
    /// </summary>
    public sealed class VocabularyRefresherTests : IDisposable
    {
        private static readonly string[] CorpusWords =
        {
            "neuroscience",
            "document",
            "attachment",
            "repomaps",
            "powertoy",
            "guthikonda",
            "ollama",
            "kanata",
        };

        private readonly string _rootDir;

        public VocabularyRefresherTests()
        {
            _rootDir = Path.Combine(Path.GetTempPath(), "etb-vocab-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDir);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_rootDir, recursive: true);
            }
            catch (IOException)
            {
                // Temp cleanup is best-effort.
            }
        }

        private sealed class Setup : IDisposable
        {
            public FakeEverythingClient Client { get; }
            public ISettings Settings { get; }
            public TokenVocabulary Vocabulary { get; }
            public SymSpellCandidateProvider Provider { get; }
            public VocabularyRefresher Refresher { get; }
            public TypoFallbackClient Decorator { get; }

            public Setup(
                string cacheDir,
                string instance = "",
                bool enabled = true,
                FakeEverythingClient? client = null
            )
            {
                Client = client ?? new FakeEverythingClient();
                Settings = TestSettingsFactory.Create(typoTolerantSearchEnabled: enabled);
                Settings.InstanceName = instance;
                Vocabulary = new TokenVocabulary();
                Provider = new SymSpellCandidateProvider(Vocabulary);
                Refresher = new VocabularyRefresher(Client, Vocabulary, Provider, Settings, cacheDirectory: cacheDir);
                Decorator = new TypoFallbackClient(
                    Client,
                    new FallbackQueryPlanner(AliasStore.Empty, Provider),
                    Settings
                );
            }

            public Task WaitReadyAsync()
            {
                Refresher.Start();
                return Refresher.WaitForReadyAsync(TimeSpan.FromSeconds(30));
            }

            public void Dispose()
            {
                Refresher.Dispose();
            }
        }

        private Setup CreateCorpusSetup(string instance = "", bool enabled = true, string? cacheDir = null)
        {
            var corpusDir = Path.Combine(_rootDir, "corpus-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(corpusDir);
            var corpus = CorpusWords.Select((word, i) => Path.Combine(corpusDir, $"{word}_file_{i:D3}.txt")).ToList();
            return new Setup(
                cacheDir ?? Path.Combine(_rootDir, "cache-" + Guid.NewGuid().ToString("N")),
                instance,
                enabled,
                new FakeEverythingClient { PathCorpus = corpus }
            );
        }

        private static SearchQuery Query(string text)
        {
            return new SearchQuery(text, SortBy.Name, false, false, false, false, false);
        }

        [Fact]
        public async Task FirstRun_BootstrapsImmediately_AndCorrectsThroughProductionPath()
        {
            using var setup = CreateCorpusSetup();

            // Before bootstrap the vocabulary is empty, so the typo cannot be corrected.
            Assert.Equal(0, setup.Decorator.QueryCountSync(Query("neurosicence"), 256, CancellationToken.None));
            Assert.Null(setup.Vocabulary.Find("neuroscience"));

            await setup.WaitReadyAsync();

            Assert.Equal(TypoVocabularyState.Ready, setup.Refresher.State);
            Assert.NotEqual(0, setup.Vocabulary.WordCount);
            Assert.NotNull(setup.Vocabulary.Find("neuroscience"));
            Assert.True(setup.Client.CorpusCountQueries >= 1, "bootstrap must consult Everything");
            Assert.True(setup.Client.CorpusRangeQueries >= 1, "bootstrap must page the index");

            var count = setup.Decorator.QueryCountSync(Query("neurosicence"), 256, CancellationToken.None);
            Assert.True(count > 0, "typo fallback should find the corpus file after bootstrap");
            Assert.NotNull(setup.Decorator.GetActiveFallback(Query("neurosicence")));
        }

        [Fact]
        public async Task SecondRun_LoadsCache_CorrectsWithoutReenumerating()
        {
            var cacheDir = Path.Combine(_rootDir, "cache-" + Guid.NewGuid().ToString("N"));
            using (var first = CreateCorpusSetup(cacheDir: cacheDir))
            {
                await first.WaitReadyAsync();
                Assert.Equal(TypoVocabularyState.Ready, first.Refresher.State);
            }

            using var second = new Setup(
                cacheDir,
                client: new FakeEverythingClient { PathCorpus = Array.Empty<string>() }
            );
            await second.WaitReadyAsync();

            Assert.Equal(TypoVocabularyState.Ready, second.Refresher.State);
            Assert.Equal(0, second.Client.CorpusRangeQueries);
            Assert.Equal(0, second.Client.CorpusCountQueries);
            Assert.NotNull(second.Vocabulary.Find("neuroscience"));

            // The empty corpus means Everything returns nothing here; the cache only has to make
            // the candidate available, never fake results.
            var plan = new FallbackQueryPlanner(AliasStore.Empty, second.Provider).Plan("neurosicence");
            Assert.NotNull(plan);
            Assert.Contains("neuroscience", plan!.CorrectedQuery);
        }

        [Fact]
        public async Task CorruptCache_IsIgnoredAndRebuilt()
        {
            var cacheDir = Path.Combine(_rootDir, "cache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(cacheDir);
            File.WriteAllText(SpellingVocabularyCache.GetCachePath(cacheDir, ""), "{ this is not json");

            using var setup = CreateCorpusSetup();
            await setup.WaitReadyAsync();

            Assert.Equal(TypoVocabularyState.Ready, setup.Refresher.State);
            Assert.True(setup.Client.CorpusRangeQueries > 0, "corrupt cache must trigger a fresh bootstrap");
            Assert.NotNull(setup.Vocabulary.Find("ollama"));
        }

        [Fact]
        public async Task CacheFromAnotherInstance_IsNotReused()
        {
            var cacheDir = Path.Combine(_rootDir, "cache-" + Guid.NewGuid().ToString("N"));
            using (var first = CreateCorpusSetup(instance: "alpha", cacheDir: cacheDir))
            {
                await first.WaitReadyAsync();
                Assert.True(File.Exists(SpellingVocabularyCache.GetCachePath(cacheDir, "alpha")));
            }

            using var second = CreateCorpusSetup(instance: "beta", cacheDir: cacheDir);
            await second.WaitReadyAsync();

            Assert.True(second.Client.CorpusRangeQueries > 0, "a cache keyed to another instance must not be reused");
            Assert.True(File.Exists(SpellingVocabularyCache.GetCachePath(cacheDir, "beta")));
        }

        [Fact]
        public async Task EmptyIndex_ReachReadyWithoutErrors()
        {
            using var setup = new Setup(
                Path.Combine(_rootDir, "cache-" + Guid.NewGuid().ToString("N")),
                client: new FakeEverythingClient { PathCorpus = Array.Empty<string>() }
            );

            await setup.WaitReadyAsync();

            Assert.Equal(TypoVocabularyState.Ready, setup.Refresher.State);
            Assert.Equal(0, setup.Vocabulary.WordCount);
            Assert.Equal(0, setup.Decorator.QueryCountSync(Query("neurosicence"), 256, CancellationToken.None));
        }

        [Fact]
        public async Task DisabledSetting_NoQueriesAndDisabledState()
        {
            using var setup = CreateCorpusSetup(enabled: false);

            await setup.WaitReadyAsync();

            Assert.Equal(TypoVocabularyState.Disabled, setup.Refresher.State);
            Assert.Equal(0, setup.Client.CorpusCountQueries);
            Assert.Equal(0, setup.Client.CorpusRangeQueries);
        }

        [Fact]
        public async Task InstanceChange_RebuildsVocabularyForNewInstance()
        {
            var cacheDir = Path.Combine(_rootDir, "cache-" + Guid.NewGuid().ToString("N"));
            using var setup = CreateCorpusSetup(instance: "alpha", cacheDir: cacheDir);
            await setup.WaitReadyAsync();
            Assert.True(setup.Vocabulary.WordCount > 0);
            var queriesBefore = setup.Client.CorpusRangeQueries;

            setup.Settings.InstanceName = "gamma";
            await setup.Refresher.WaitForReadyAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(TypoVocabularyState.Ready, setup.Refresher.State);
            Assert.True(
                setup.Client.CorpusRangeQueries > queriesBefore,
                "instance change must trigger a fresh enumeration for the new instance"
            );
        }

        [Fact]
        public async Task DistanceSettingChange_RebuildsIndexWithoutReenumerating()
        {
            using var setup = CreateCorpusSetup();
            await setup.WaitReadyAsync();
            var initialCount = setup.Decorator.QueryCountSync(Query("neurosccien"), 256, CancellationToken.None);
            Assert.True(initialCount > 0, "default policy must correct the three-edit typo");
            var rangeQueriesBefore = setup.Client.CorpusRangeQueries;
            var countQueriesBefore = setup.Client.CorpusCountQueries;
            var versionBefore = setup.Provider.IndexVersion;

            // Long-word distance three -> two: the three-edit typo must stop correcting while the
            // distance-one typo keeps working.
            setup.Settings.TypoLongWordMaxEditDistance = 2;
            await WaitForIndexRebuildAsync(setup, versionBefore);

            Assert.Equal(TypoVocabularyState.Ready, setup.Refresher.State);
            Assert.Equal(rangeQueriesBefore, setup.Client.CorpusRangeQueries);
            Assert.Equal(countQueriesBefore, setup.Client.CorpusCountQueries);
            Assert.NotNull(setup.Vocabulary.Find("neuroscience"));

            var threeEditCount = setup.Decorator.QueryCountSync(Query("neurosccien"), 256, CancellationToken.None);
            Assert.Equal(0, threeEditCount);
            var oneEditCount = setup.Decorator.QueryCountSync(Query("neurosicence"), 256, CancellationToken.None);
            Assert.True(oneEditCount > 0, "distance-one corrections must survive the distance change");
        }

        [Fact]
        public async Task InvalidDistanceSettings_AreClampedAndKeepWorking()
        {
            using var setup = CreateCorpusSetup();
            await setup.WaitReadyAsync();
            var versionBefore = setup.Provider.IndexVersion;

            setup.Settings.TypoMaxDictionaryEditDistance = 99;
            setup.Settings.TypoLongWordMaxEditDistance = 7;
            await WaitForIndexRebuildAsync(setup, versionBefore);

            Assert.Equal(TypoVocabularyState.Ready, setup.Refresher.State);
            Assert.Equal(3, setup.Provider.Policy.MaxDictionaryEditDistance);
            Assert.Equal(3, setup.Provider.Policy.LongWordMaxEditDistance);
            var count = setup.Decorator.QueryCountSync(Query("neurosccien"), 256, CancellationToken.None);
            Assert.True(count > 0, "clamped distance-three policy must still correct the corpus typo");
        }

        private static async Task WaitForIndexRebuildAsync(Setup setup, int versionBefore)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (
                (setup.Provider.IndexVersion <= versionBefore || setup.Refresher.State != TypoVocabularyState.Ready)
                && DateTime.UtcNow < deadline
            )
            {
                await Task.Delay(25);
            }

            Assert.True(setup.Provider.IndexVersion > versionBefore, "the index must rebuild after the setting change");
            Assert.Equal(TypoVocabularyState.Ready, setup.Refresher.State);
        }
    }
}
