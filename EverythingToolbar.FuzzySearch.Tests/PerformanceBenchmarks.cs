using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EverythingToolbar.App.Search;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Core.Search;
using EverythingToolbar.FuzzySearch;
using EverythingToolbar.FuzzySearch.Tests.Support;
using EverythingToolbar.Platform.Search;
using Xunit;
using Xunit.Abstractions;

namespace EverythingToolbar.FuzzySearch.Tests
{
    /// <summary>
    /// Latency benchmark suite for the edit-distance correction path. SymSpell implements
    /// Damerau-Levenshtein distance (optimal string alignment), not plain Levenshtein - adjacent
    /// transpositions such as docuemnt->document cost one edit.
    ///
    /// Reporting/diagnostic only: runs when FUZZY_BENCH=1 (dotnet test --filter Category=Benchmark).
    /// Live Everything checks additionally require LIVE_EVERYTHING=1 and are reported BLOCKED with
    /// evidence when the SDK3 pipe is not reachable from this process.
    /// </summary>
    [Trait("Category", "Benchmark")]
    public class PerformanceBenchmarks
    {
        private static readonly string[] RealWords =
        {
            "neuroscience",
            "document",
            "documents",
            "attachment",
            "attachments",
            "repomaps",
            "powertoy",
            "powertoys",
            "guthikonda",
            "ollama",
            "kanata",
            "clinical",
            "project",
            "projects",
            "settings",
            "configuration",
            "repository",
            "repositories",
            "windows",
            "everything",
            "toolbar",
            "keyboard",
            "shortcut",
            "shortcuts",
            "application",
            "development",
            "environment",
            "temporary",
            "directory",
            "directories",
        };

        private static readonly (string Typo, string Correction)[] RequiredCases =
        {
            ("neurosicence", "neuroscience"),
            ("neurosccience", "neuroscience"),
            ("neurosccien", "neuroscience"),
            ("docuemnt", "document"),
            ("attachement", "attachment"),
            ("repomsp", "repomaps"),
            ("powertyo", "powertoy"),
            ("guthikodna", "guthikonda"),
            ("ollma", "ollama"),
            ("kanataa", "kanata"),
        };

        private readonly ITestOutputHelper _output;
        private readonly Random _probeRandom = new(20260930);

        public PerformanceBenchmarks(ITestOutputHelper output)
        {
            _output = output;
        }

        private static string RandomWord(Random random, int index)
        {
            var length = 4 + index % 12;
            var chars = new char[length];
            for (var i = 0; i < length; i++)
            {
                chars[i] = (char)('a' + random.Next(26));
            }

            return new string(chars);
        }

        private static string Transpose(string word)
        {
            if (word.Length < 3)
            {
                return word;
            }

            var pos = word.Length / 2;
            return word[..pos] + word[pos + 1] + word[pos] + word[(pos + 2)..];
        }

        private static string Delete(string word)
        {
            return word[..(word.Length / 2)] + word[((word.Length / 2) + 1)..];
        }

        private static string Insert(string word)
        {
            return word[..(word.Length / 2)] + "x" + word[(word.Length / 2)..];
        }

        private static string Substitute(string word)
        {
            var mid = word.Length / 2;
            var replacement = word[mid] == 'x' ? 'q' : 'x';
            return word[..mid] + replacement + word[(mid + 1)..];
        }

        private static TokenVocabulary BuildVocabulary(int wordCount, int seed, out long buildMs)
        {
            var random = new Random(seed);
            var vocabulary = new TokenVocabulary();
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < wordCount; i++)
            {
                var word = i % 10 == 0 ? RealWords[i % RealWords.Length] : RandomWord(random, i);
                vocabulary.AddPath(word);
            }

            watch.Stop();
            buildMs = watch.ElapsedMilliseconds;
            return vocabulary;
        }

        private static double[] Latencies(Action probe, int count, int warmup)
        {
            var watch = new Stopwatch();
            for (var i = 0; i < warmup; i++)
            {
                probe();
            }

            var results = new double[count];
            for (var i = 0; i < count; i++)
            {
                watch.Restart();
                probe();
                watch.Stop();
                results[i] = watch.Elapsed.TotalMilliseconds;
            }

            Array.Sort(results);
            return results;
        }

        private static double Percentile(double[] sorted, double p)
        {
            var idx = (int)Math.Ceiling(p / 100.0 * sorted.Length) - 1;
            return sorted[Math.Clamp(idx, 0, sorted.Length - 1)];
        }

        private void Report(string label, double[] latencies)
        {
            _output.WriteLine(
                $"{label}: p50={Percentile(latencies, 50):F3} ms p95={Percentile(latencies, 95):F3} ms p99={Percentile(latencies, 99):F3} ms max={latencies[^1]:F3} ms (n={latencies.Length})"
            );
        }

        [Fact]
        public void BenchmarkA_CandidateLookup()
        {
            if (Environment.GetEnvironmentVariable("FUZZY_BENCH") is null)
            {
                _output.WriteLine("Skipped: set FUZZY_BENCH=1 to run benchmarks.");
                return;
            }

            foreach (var size in new[] { 1_000, 10_000, 50_000, 100_000 })
            {
                var vocabulary = BuildVocabulary(size, 42, out var buildMs);
                var provider = new SymSpellCandidateProvider(vocabulary);
                var rebuildWatch = Stopwatch.StartNew();
                provider.Rebuild();
                rebuildWatch.Stop();
                var ramMb = Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0;

                _output.WriteLine(
                    $"[A] size={size} dict={provider.Policy.MaxDictionaryEditDistance}: vocabBuild={buildMs} ms indexRebuild={rebuildWatch.ElapsedMilliseconds} ms words={vocabulary.WordCount} indexEntries={provider.EntryCount} workingSet={ramMb:F1} MB"
                );

                var cold = Stopwatch.StartNew();
                provider.FindCandidates("neurosicence", 3, CancellationToken.None);
                cold.Stop();
                _output.WriteLine(
                    $"[A] size={size} dict={provider.Policy.MaxDictionaryEditDistance}: coldFirstLookup={cold.Elapsed.TotalMilliseconds:F3} ms"
                );

                var random = new Random(size);
                foreach (
                    var (shapeName, mutate) in new (string, Func<string, string>)[]
                    {
                        ("transposition", Transpose),
                        ("deletion", Delete),
                        ("insertion", Insert),
                        ("substitution", Substitute),
                    }
                )
                {
                    var probes = new List<string>();
                    while (probes.Count < 500)
                    {
                        var word =
                            random.Next(10) == 0
                                ? RealWords[random.Next(RealWords.Length)]
                                : RandomWord(random, probes.Count * 7 + size);
                        if (SafeLiteralClassifier.IsCorrectable(new SearchTerm(word, 0, SearchTermKind.PlainLiteral)))
                        {
                            probes.Add(mutate(word));
                        }
                    }

                    var latencies = Latencies(
                        () =>
                            provider.FindCandidates(probes[_probeRandom.Next(probes.Count)], 3, CancellationToken.None),
                        probes.Count,
                        25
                    );
                    Report(
                        $"[A] size={size} dict={provider.Policy.MaxDictionaryEditDistance} shape={shapeName}",
                        latencies
                    );
                }
            }
        }

        [Fact]
        public void BenchmarkA_RequiredTypoCases()
        {
            if (Environment.GetEnvironmentVariable("FUZZY_BENCH") is null)
            {
                _output.WriteLine("Skipped: set FUZZY_BENCH=1 to run benchmarks.");
                return;
            }

            var vocabulary = BuildVocabulary(100_000, 7, out _);
            foreach (var (_, correction) in RequiredCases)
            {
                vocabulary.AddPath(correction);
            }

            var provider = new SymSpellCandidateProvider(vocabulary);
            provider.Rebuild();

            foreach (var (typo, correction) in RequiredCases)
            {
                var latencies = Latencies(() => provider.FindCandidates(typo, 3, CancellationToken.None), 200, 10);
                var found = provider
                    .FindCandidates(typo, 5, CancellationToken.None)
                    .Any(c => c.Correction.Equals(correction, StringComparison.OrdinalIgnoreCase));
                Report(
                    $"[A] dict={provider.Policy.MaxDictionaryEditDistance} case={typo}->{correction} found={found}",
                    latencies
                );
            }
        }

        [Fact]
        public void BenchmarkB_PlannerLatency()
        {
            if (Environment.GetEnvironmentVariable("FUZZY_BENCH") is null)
            {
                _output.WriteLine("Skipped: set FUZZY_BENCH=1 to run benchmarks.");
                return;
            }

            var vocabulary = BuildVocabulary(50_000, 11, out _);
            var provider = new SymSpellCandidateProvider(vocabulary);
            var planner = new FallbackQueryPlanner(AliasStore.Empty, provider);

            Report(
                $"[B] dict={provider.Policy.MaxDictionaryEditDistance} planner classify+lookup+plan (mixed query)",
                Latencies(() => planner.Plan("clinical attachement ext:pdf"), 500, 25)
            );
            Report(
                $"[B] dict={provider.Policy.MaxDictionaryEditDistance} planner single literal",
                Latencies(() => planner.Plan("neurosicence"), 500, 25)
            );
        }

        [Fact]
        public void BenchmarkC_FastPathOverhead()
        {
            if (Environment.GetEnvironmentVariable("FUZZY_BENCH") is null)
            {
                _output.WriteLine("Skipped: set FUZZY_BENCH=1 to run benchmarks.");
                return;
            }

            var corpus = RealWords.Select((w, i) => $@"C:\corpus\{w}\{w}_file_{i}.txt").ToList();
            var inner = new FakeEverythingClient { PathCorpus = corpus };
            var settings = TestSettingsFactory.Create(typoTolerantSearchEnabled: true);
            var vocabulary = new TokenVocabulary();
            var provider = new SymSpellCandidateProvider(vocabulary);
            var refresher = new VocabularyRefresher(
                inner,
                vocabulary,
                provider,
                settings,
                cacheDirectory: Path.Combine(Path.GetTempPath(), "etb-bench-" + Guid.NewGuid().ToString("N"))
            );
            var decorator = new TypoFallbackClient(
                inner,
                new FallbackQueryPlanner(AliasStore.Empty, provider),
                settings
            );
            var query = new SearchQuery("ollama", SortBy.Name, false, false, false, false, false);

            var raw = Latencies(() => inner.QueryCountSync(query, 256, CancellationToken.None), 2000, 100);
            var decorated = Latencies(() => decorator.QueryCountSync(query, 256, CancellationToken.None), 2000, 100);

            Report("[C] raw IEverythingClient exact-count", raw);
            Report(
                $"[C] dict={provider.Policy.MaxDictionaryEditDistance} decorated exact-count (fast path)",
                decorated
            );
            _output.WriteLine(
                $"[C] mean overhead: {(decorated.Average() - raw.Average()) * 1000:F1} us (raw mean {raw.Average():F4} ms, decorated mean {decorated.Average():F4} ms)"
            );
            refresher.Dispose();
        }

        [Fact]
        public void BenchmarkD_ZeroResultCorrectionPath()
        {
            if (Environment.GetEnvironmentVariable("FUZZY_BENCH") is null)
            {
                _output.WriteLine("Skipped: set FUZZY_BENCH=1 to run benchmarks.");
                return;
            }

            var corpus = new List<string>();
            for (var i = 0; i < 20_000; i++)
            {
                corpus.Add($@"C:\corpus\{RandomWord(new Random(i), i)}\file_{i}.txt");
            }

            corpus.AddRange(RealWords.Select(w => $@"C:\corpus\{w}\{w}.txt"));

            var inner = new FakeEverythingClient { PathCorpus = corpus };
            var settings = TestSettingsFactory.Create(typoTolerantSearchEnabled: true);
            var vocabulary = new TokenVocabulary();
            var provider = new SymSpellCandidateProvider(vocabulary);
            var refresher = new VocabularyRefresher(
                inner,
                vocabulary,
                provider,
                settings,
                cacheDirectory: Path.Combine(Path.GetTempPath(), "etb-bench-" + Guid.NewGuid().ToString("N"))
            );
            var decorator = new TypoFallbackClient(
                inner,
                new FallbackQueryPlanner(AliasStore.Empty, provider),
                settings
            );
            refresher.Start();
            refresher.WaitForReadyAsync(TimeSpan.FromMinutes(5)).GetAwaiter().GetResult();

            var latencies = new List<double>();
            for (var i = 0; i < 300; i++)
            {
                var typo = i % 2 == 0 ? "ollma" : Transpose(RealWords[i % RealWords.Length]);
                var query = new SearchQuery(typo, SortBy.Name, false, false, false, false, false);
                var watch = Stopwatch.StartNew();
                var count = decorator.QueryCountSync(query, 256, CancellationToken.None);
                if (count > 0)
                {
                    decorator.QueryRangeSync(query, 0, 256, CancellationToken.None);
                }

                watch.Stop();
                latencies.Add(watch.Elapsed.TotalMilliseconds);
            }

            latencies.Sort();
            Report(
                $"[D] dict={provider.Policy.MaxDictionaryEditDistance} zero-result raw -> correction -> count(+range)",
                latencies.ToArray()
            );
            refresher.Dispose();
        }

        [Fact]
        public void BenchmarkE_BootstrapAndCache()
        {
            if (Environment.GetEnvironmentVariable("FUZZY_BENCH") is null)
            {
                _output.WriteLine("Skipped: set FUZZY_BENCH=1 to run benchmarks.");
                return;
            }

            const int corpusSize = 50_000;
            var random = new Random(99);
            var corpus = new List<string>(corpusSize);
            for (var i = 0; i < corpusSize; i++)
            {
                var word = i % 10 == 0 ? RealWords[i % RealWords.Length] : RandomWord(random, i);
                corpus.Add($@"C:\corpus\{word}\{word}_{i}.txt");
            }

            var cacheDir = Path.Combine(Path.GetTempPath(), "etb-bench-" + Guid.NewGuid().ToString("N"));
            var client = new FakeEverythingClient { PathCorpus = corpus };
            var settings = TestSettingsFactory.Create(typoTolerantSearchEnabled: true);
            var vocabulary = new TokenVocabulary();
            var provider = new SymSpellCandidateProvider(vocabulary);
            var refresher = new VocabularyRefresher(client, vocabulary, provider, settings, cacheDirectory: cacheDir);

            var bootstrapWatch = Stopwatch.StartNew();
            refresher.Start();
            refresher.WaitForReadyAsync(TimeSpan.FromMinutes(10)).GetAwaiter().GetResult();
            bootstrapWatch.Stop();
            _output.WriteLine(
                $"[E] cold bootstrap ({corpusSize} paths): {bootstrapWatch.ElapsedMilliseconds} ms, words={vocabulary.WordCount}"
            );

            var cachePath = SpellingVocabularyCache.GetCachePath(cacheDir, "");
            var saveWatch = Stopwatch.StartNew();
            SpellingVocabularyCache.Save(cachePath, SpellingVocabularyCache.CreateSnapshot("", vocabulary));
            saveWatch.Stop();
            _output.WriteLine(
                $"[E] cache save: {saveWatch.ElapsedMilliseconds} ms, fileSize={new FileInfo(cachePath).Length / 1024.0:F0} KB"
            );

            var loadWatch = Stopwatch.StartNew();
            var loaded = SpellingVocabularyCache.Load(cachePath, "");
            loadWatch.Stop();
            _output.WriteLine(
                $"[E] cache load: {loadWatch.ElapsedMilliseconds} ms, entries={loaded?.Words.Count ?? 0}"
            );

            var freshVocabulary = new TokenVocabulary();
            var restoreWatch = Stopwatch.StartNew();
            freshVocabulary.Restore(
                loaded!.Words.Select(w => new VocabularyEntry(w.N, w.D, w.F, IsCompound: false)),
                loaded.Compounds.Select(c => new VocabularyEntry(c.N, c.D, c.F, IsCompound: true))
            );
            var freshProvider = new SymSpellCandidateProvider(freshVocabulary);
            freshProvider.Rebuild();
            restoreWatch.Stop();
            _output.WriteLine(
                $"[E] restore + index build (startup-to-ready path): {restoreWatch.ElapsedMilliseconds} ms"
            );

            var secondRefresher = new VocabularyRefresher(
                new FakeEverythingClient { PathCorpus = Array.Empty<string>() },
                new TokenVocabulary(),
                new SymSpellCandidateProvider(new TokenVocabulary()),
                settings,
                cacheDirectory: cacheDir
            );
            var readyWatch = Stopwatch.StartNew();
            secondRefresher.Start();
            secondRefresher.WaitForReadyAsync(TimeSpan.FromMinutes(5)).GetAwaiter().GetResult();
            readyWatch.Stop();
            _output.WriteLine($"[E] subsequent-startup time-to-Ready: {readyWatch.ElapsedMilliseconds} ms");
            refresher.Dispose();
            secondRefresher.Dispose();
        }

        [Fact]
        public void BenchmarkG_DictionaryDistanceTwoVersusThree()
        {
            if (Environment.GetEnvironmentVariable("FUZZY_BENCH") is null)
            {
                _output.WriteLine("Skipped: set FUZZY_BENCH=1 to run benchmarks.");
                return;
            }

            // Compares index distance 2 vs 3 at increasing vocabulary sizes: build/rebuild cost,
            // working-set delta, and lookup latency by typo shape and distance level. The distance-3
            // probe sets only contain probes of at least nine characters so the long-word policy
            // tier is what is measured.
            foreach (var size in new[] { 10_000, 50_000, 100_000, 150_000 })
            {
                var vocabulary = BuildVocabulary(size, 42, out _);
                foreach (var dictionaryDistance in new[] { 2, 3 })
                {
                    var policy = new EditDistancePolicy(dictionaryDistance, dictionaryDistance, 9);
                    var provider = new SymSpellCandidateProvider(vocabulary, policy);
                    var random = new Random(dictionaryDistance * 1000 + size);

                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    var workingSetBefore = Process.GetCurrentProcess().WorkingSet64;
                    var buildWatch = Stopwatch.StartNew();
                    provider.Rebuild();
                    buildWatch.Stop();
                    var workingSetAfter = Process.GetCurrentProcess().WorkingSet64;

                    var rebuildWatch = Stopwatch.StartNew();
                    provider.Rebuild();
                    rebuildWatch.Stop();

                    _output.WriteLine(
                        $"[G] size={size} dict={dictionaryDistance}: words={vocabulary.WordCount} entries={provider.EntryCount} "
                            + $"indexBuild={buildWatch.ElapsedMilliseconds} ms indexRebuild={rebuildWatch.ElapsedMilliseconds} ms "
                            + $"workingSet={(workingSetAfter - workingSetBefore) / 1024.0 / 1024.0:F1} MB delta, total {workingSetAfter / 1024.0 / 1024.0:F0} MB"
                    );

                    var cold = Stopwatch.StartNew();
                    provider.FindCandidates("neurosicence", 3, CancellationToken.None);
                    cold.Stop();
                    _output.WriteLine(
                        $"[G] size={size} dict={dictionaryDistance}: coldFirstLookup={cold.Elapsed.TotalMilliseconds:F3} ms"
                    );

                    foreach (
                        var (shapeName, mutate, minWordLength) in new[]
                        {
                            ("distance1-deletion", (Func<string, string>)Delete, 4),
                            ("distance1-transposition", (Func<string, string>)Transpose, 4),
                            ("distance1-insertion", (Func<string, string>)Insert, 4),
                            ("distance1-substitution", (Func<string, string>)Substitute, 4),
                            ("distance2-doubledeletion", (Func<string, string>)(w => Delete(Delete(w))), 6),
                            ("distance3-tripledeletion", (Func<string, string>)(w => Delete(Delete(Delete(w)))), 12),
                            ("distance3-mixed", (Func<string, string>)(w => Insert(Transpose(Delete(w)))), 12),
                        }
                    )
                    {
                        var probes = BuildShapeProbes(vocabulary, mutate, random, 400, minWordLength);
                        if (probes.Count == 0)
                        {
                            continue;
                        }

                        var latencies = Latencies(
                            () => provider.FindCandidates(probes[random.Next(probes.Count)], 3, CancellationToken.None),
                            probes.Count,
                            25
                        );
                        var avgCandidates = probes.Average(p =>
                            provider.FindCandidates(p, 5, CancellationToken.None).Count
                        );
                        Report(
                            $"[G] size={size} dict={dictionaryDistance} shape={shapeName} avgCandidates={avgCandidates:F2}",
                            latencies
                        );
                    }
                }
            }
        }

        private static List<string> BuildShapeProbes(
            TokenVocabulary vocabulary,
            Func<string, string> mutate,
            Random random,
            int count,
            int minWordLength
        )
        {
            var words = vocabulary
                .WordsByFrequency()
                .Select(e => e.Normalized)
                .Where(w =>
                    w.Length >= minWordLength
                    && SafeLiteralClassifier.IsCorrectable(new SearchTerm(w, 0, SearchTermKind.PlainLiteral))
                )
                .ToList();
            if (words.Count == 0)
            {
                return [];
            }

            var probes = new List<string>();
            var attempts = 0;
            while (probes.Count < count && attempts < count * 10)
            {
                attempts++;
                var probe = mutate(words[random.Next(words.Count)]);
                if (probe.Length >= SafeLiteralClassifier.MinCorrectableLength)
                {
                    probes.Add(probe);
                }
            }

            return probes;
        }

        [Fact]
        public void BenchmarkF_LiveEverything()
        {
            if (Environment.GetEnvironmentVariable("LIVE_EVERYTHING") is null)
            {
                _output.WriteLine("Skipped: set LIVE_EVERYTHING=1 (with Everything 1.5a running).");
                return;
            }

            var pipeClient = new EverythingPipeClient();
            if (!pipeClient.TryConnect())
            {
                _output.WriteLine(
                    "BLOCKED: Everything SDK3 pipe not reachable from this process (typically ERROR_ACCESS_DENIED 5: "
                        + "the running Everything instance is elevated, this process is not). Rerun from an elevated "
                        + "shell. No synthetic substitute was used."
                );
                return;
            }

            var settings = TestSettingsFactory.Create(typoTolerantSearchEnabled: true);
            var vocabulary = new TokenVocabulary();
            var provider = new SymSpellCandidateProvider(vocabulary);
            var decorator = new TypoFallbackClient(
                pipeClient,
                new FallbackQueryPlanner(AliasStore.Empty, provider),
                settings
            );

            foreach (var word in new[] { "neuroscience", "document", "attachment", "ollama", "kanata" })
            {
                var exactQuery = new SearchQuery(word, SortBy.Name, false, false, false, false, false);
                if (pipeClient.QueryCountSync(exactQuery, 256, CancellationToken.None) == 0)
                {
                    _output.WriteLine($"[F] live exact '{word}': no matches in this index, case skipped");
                    continue;
                }

                Report(
                    $"[F] live exact '{word}'",
                    Latencies(() => pipeClient.QueryCountSync(exactQuery, 256, CancellationToken.None), 100, 5)
                );

                var typo = Transpose(word) == word ? Delete(word) : Transpose(word);
                var typoQuery = new SearchQuery(typo, SortBy.Name, false, false, false, false, false);
                var rawCount = pipeClient.QueryCountSync(typoQuery, 256, CancellationToken.None);
                var viaDecorator = Latencies(
                    () => decorator.QueryCountSync(typoQuery, 256, CancellationToken.None),
                    50,
                    3
                );
                _output.WriteLine($"[F] live typo '{typo}' (raw matches={rawCount}): fallback path below");
                Report($"[F] live typo '{typo}' via decorator", viaDecorator);
            }
        }
    }
}
