using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace EverythingToolbar.FuzzySearch.Tests
{
    /// <summary>
    /// Measurement harness, not a regression test: runs only when FUZZY_BENCH is set
    /// (dotnet test --filter Category=Benchmark with FUZZY_BENCH=1). Targets: candidate
    /// lookup p50 &lt; 5 ms, p95 &lt; 15 ms on this machine.
    /// </summary>
    [Trait("Category", "Benchmark")]
    public class PerformanceBenchmarks
    {
        private readonly ITestOutputHelper _output;

        public PerformanceBenchmarks(ITestOutputHelper output)
        {
            _output = output;
        }

        private static string RandomWord(Random random, int index)
        {
            var length = 4 + index % 12;
            var sb = new StringBuilder(length);
            for (var i = 0; i < length; i++)
            {
                sb.Append((char)('a' + random.Next(26)));
            }

            return sb.ToString();
        }

        [Fact]
        public void VocabularyAndLookupLatency()
        {
            if (Environment.GetEnvironmentVariable("FUZZY_BENCH") is null)
            {
                _output.WriteLine("Skipped: set FUZZY_BENCH=1 to run benchmarks.");
                return;
            }

            const int wordCount = 50000;
            const int probeCount = 1000;
            var random = new Random(42);

            var vocabulary = new TokenVocabulary();
            var buildWatch = Stopwatch.StartNew();
            for (var i = 0; i < wordCount; i++)
            {
                var word = i % 10 == 0 ? RealWordSamples[i % RealWordSamples.Length] : RandomWord(random, i);
                vocabulary.AddPath(word);
            }

            buildWatch.Stop();

            var provider = new SymSpellCandidateProvider(vocabulary);
            var rebuildWatch = Stopwatch.StartNew();
            provider.Rebuild();
            rebuildWatch.Stop();

            var process = Process.GetCurrentProcess();
            var ramMb = process.WorkingSet64 / 1024.0 / 1024.0;

            // Probe terms: half real typos from the sample set, half random words.
            var probes = new List<string>();
            for (var i = 0; i < probeCount; i++)
            {
                var baseWord =
                    i % 2 == 0 ? RealWordSamples[random.Next(RealWordSamples.Length)] : RandomWord(random, i);
                if (baseWord.Length > 4)
                {
                    var pos = random.Next(baseWord.Length - 1);
                    probes.Add(baseWord[..pos] + baseWord[pos + 1] + baseWord[pos] + baseWord[(pos + 2)..]);
                }
                else
                {
                    probes.Add(baseWord);
                }
            }

            // Warm-up, then measure.
            foreach (var probe in probes.Take(50))
            {
                provider.FindCandidates(probe, 3, CancellationToken.None);
            }

            var latencies = new List<double>(probeCount);
            var watch = new Stopwatch();
            foreach (var probe in probes)
            {
                watch.Restart();
                provider.FindCandidates(probe, 3, CancellationToken.None);
                watch.Stop();
                latencies.Add(watch.Elapsed.TotalMilliseconds);
            }

            latencies.Sort();
            double Percentile(double p)
            {
                var idx = (int)Math.Ceiling(p / 100.0 * latencies.Count) - 1;
                return latencies[Math.Clamp(idx, 0, latencies.Count - 1)];
            }

            _output.WriteLine($"vocabulary build ({wordCount} paths): {buildWatch.ElapsedMilliseconds} ms");
            _output.WriteLine($"SymSpell rebuild: {rebuildWatch.ElapsedMilliseconds} ms");
            _output.WriteLine($"vocabulary words: {vocabulary.WordCount}, compounds: {vocabulary.CompoundCount}");
            _output.WriteLine($"index entries: {provider.EntryCount}");
            _output.WriteLine($"process working set: {ramMb:F1} MB");
            _output.WriteLine(
                $"candidate lookup over {probeCount} probes: p50={Percentile(50):F3} ms p95={Percentile(95):F3} ms p99={Percentile(99):F3} ms"
            );
        }

        private static readonly string[] RealWordSamples =
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
    }
}
