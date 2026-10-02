using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Config.Net;
using EverythingToolbar.App;
using EverythingToolbar.App.Search;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Core.Search;
using EverythingToolbar.FuzzySearch.Tests.Support;
using EverythingToolbar.Platform.Search;
using Xunit;
using Xunit.Abstractions;

namespace EverythingToolbar.FuzzySearch.Tests
{
    /// <summary>
    /// Live integration against the installed Everything 1.5a instance. Runs only when
    /// LIVE_EVERYTHING=1 is set (requires Everything 1.5a running; creates and deletes only its
    /// own uniquely named files under %TEMP%). Verifies the SDK3 pipe path, zero-result typo
    /// fallback, advanced-syntax preservation and exact-match safety through the real decorator.
    /// </summary>
    [Trait("Category", "LiveEverything")]
    public class EverythingLiveIntegrationTests
    {
        private readonly ITestOutputHelper _output;

        public EverythingLiveIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static SearchQuery Query(string text)
        {
            return new SearchQuery(text, SortBy.Name, false, false, false, false, false);
        }

        [Fact]
        public void LiveEverything_TypoFallbackAndSyntaxSafety()
        {
            if (Environment.GetEnvironmentVariable("LIVE_EVERYTHING") is null)
            {
                _output.WriteLine("Skipped: set LIVE_EVERYTHING=1 with Everything 1.5a running.");
                return;
            }

            var testDir = Path.Combine(
                Path.GetTempPath(),
                "everythingtoolbar-fuzzy-" + Guid.NewGuid().ToString("N")[..8]
            );
            Directory.CreateDirectory(testDir);
            try
            {
                var neuroscienceFile = Path.Combine(testDir, "Neuroscience_Project_ABC123.txt");
                var attachmentFile = Path.Combine(testDir, "Clinical_Attachment_DEF456.pdf");
                var exactFile = Path.Combine(testDir, "form_GHI789.txt");
                File.WriteAllText(neuroscienceFile, "test");
                File.WriteAllText(attachmentFile, "test");
                File.WriteAllText(exactFile, "test");

                var pipeClient = new EverythingPipeClient();
                Assert.True(
                    pipeClient.TryConnect(),
                    "Everything 1.5a SDK3 pipe must be reachable. Access denied typically means "
                        + "Everything is running elevated while this test is not; run the test from "
                        + "an elevated shell (the elevated Everything instance owns the instance pipe)."
                );
                _output.WriteLine("Everything version: " + pipeClient.GetEverythingVersion());

                var iniPath = Path.Combine(testDir, "settings.ini");
                File.WriteAllText(iniPath, "[General]\nIsTypoTolerantSearchEnabled=true\n");
                var store = new ConfigurationBuilder<IToolbarSettings>().UseIniFile(iniPath).Build();
                ISettings settings = SettingsProxy.Create(store);

                var vocabulary = new TokenVocabulary();
                vocabulary.AddPath(neuroscienceFile);
                vocabulary.AddPath(attachmentFile);
                vocabulary.AddPath(exactFile);
                var planner = new FallbackQueryPlanner(AliasStore.Empty, new SymSpellCandidateProvider(vocabulary));
                var decorator = new TypoFallbackClient(pipeClient, planner, settings);

                // Wait for Everything to index the new files.
                var indexed = WaitFor(
                    () =>
                        pipeClient.QueryCountSync(Query("path:\"" + testDir + "\""), 256, CancellationToken.None) >= 3,
                    TimeSpan.FromSeconds(30)
                );
                Assert.True(indexed, "Everything did not index the test files in time");

                // Raw typo query returns nothing; the decorator falls back and finds the file.
                var typoQuery = Query("neurosicence path:\"" + testDir + "\"");
                var rawCount = pipeClient.QueryCountSync(typoQuery, 256, CancellationToken.None);
                Assert.Equal(0, rawCount);
                var fallbackCount = decorator.QueryCountSync(typoQuery, 256, CancellationToken.None);
                Assert.True(fallbackCount > 0, "typo fallback should find the file");
                Assert.NotNull(decorator.GetActiveFallback(typoQuery));
                _output.WriteLine($"typo fallback: raw={rawCount} fallback={fallbackCount}");

                // Advanced syntax passes through untouched (raw count already > 0, no fallback).
                var syntaxQuery = Query("path:\"" + testDir + "\" ext:pdf");
                var syntaxCount = pipeClient.QueryCountSync(syntaxQuery, 256, CancellationToken.None);
                Assert.Equal(1, syntaxCount);
                Assert.Null(decorator.GetActiveFallback(syntaxQuery));

                // Exact token match is served raw; fallback never activates.
                var exactQuery = Query("form path:\"" + testDir + "\"");
                var exactCount = decorator.QueryCountSync(exactQuery, 256, CancellationToken.None);
                Assert.Equal(1, exactCount);
                Assert.Null(decorator.GetActiveFallback(exactQuery));

                // Range results materialize through the decorator. SDK3 result lists fill
                // their viewport asynchronously after the count reply; the UI reads pages
                // lazily, so wait briefly for the rows to stream in.
                var fallbackRangeQuery = Query("neurosicence path:\"" + testDir + "\"");
                var results = WaitForResults(decorator, fallbackRangeQuery, TimeSpan.FromSeconds(10));
                Assert.Contains(
                    results,
                    r => r.FullPathAndFileName.Contains("Neuroscience_Project_", StringComparison.OrdinalIgnoreCase)
                );

                // Mixed query: only the plain literal gets corrected.
                var mixedQuery = Query("clinical attachement path:\"" + testDir + "\"");
                var mixedCount = decorator.QueryCountSync(mixedQuery, 256, CancellationToken.None);
                Assert.True(mixedCount > 0, "mixed query with corrected literal should find the pdf");
            }
            finally
            {
                Directory.Delete(testDir, recursive: true);
            }
        }

        [Fact]
        public void LiveEverything_DistanceSweepOneToSeven_EndToEnd()
        {
            if (Environment.GetEnvironmentVariable("LIVE_EVERYTHING") is null)
            {
                _output.WriteLine("Skipped: set LIVE_EVERYTHING=1 with Everything 1.5a running.");
                return;
            }

            // End-to-end acceptance of distances 1-7 against the real Everything index: files with
            // long real-looking names are created on disk, indexed by the running instance, and
            // each typo (exactly N edits, computed not assumed) must only correct at distance >= N
            // through the production pipe -> vocabulary -> planner -> decorator path.
            var testDir = Path.Combine(
                Path.GetTempPath(),
                "everythingtoolbar-sweep-" + Guid.NewGuid().ToString("N")[..8]
            );
            Directory.CreateDirectory(testDir);
            try
            {
                // Target N-1 must keep at least nine characters after N deletions so the typo
                // reaches the long-word tier at every distance.
                var targets = new[]
                {
                    "neuroscience",
                    "refrigerator",
                    "oligodendrocyte",
                    "electrophysiology",
                    "neurodegeneration",
                    "immunohistochemistry",
                    "electroencephalography",
                };
                foreach (var word in targets)
                {
                    File.WriteAllText(
                        Path.Combine(testDir, Capitalize(word) + "_Report_" + word.Length.ToString("D2") + ".txt"),
                        "test"
                    );
                }

                var pipeClient = new EverythingPipeClient();
                Assert.True(
                    pipeClient.TryConnect(),
                    "Everything 1.5a SDK3 pipe must be reachable. Access denied typically means "
                        + "Everything is running elevated while this test is not; run the test from "
                        + "an elevated shell."
                );
                _output.WriteLine("Everything version: " + pipeClient.GetEverythingVersion());

                var settings = TestSettingsFactory.Create(typoTolerantSearchEnabled: true);
                var vocabulary = new TokenVocabulary();
                foreach (var word in targets)
                {
                    for (var i = 0; i < 3; i++)
                    {
                        vocabulary.AddPath(Capitalize(word) + "_Report_" + word.Length.ToString("D2") + ".txt");
                    }
                }

                var provider = new SymSpellCandidateProvider(vocabulary, new EditDistancePolicy(7, 7, 9));
                var planner = new FallbackQueryPlanner(AliasStore.Empty, provider);
                var decorator = new TypoFallbackClient(pipeClient, planner, settings);

                var indexed = WaitFor(
                    () =>
                        pipeClient.QueryCountSync(Query("path:\"" + testDir + "\""), 256, CancellationToken.None)
                        >= targets.Length,
                    TimeSpan.FromSeconds(30)
                );
                Assert.True(indexed, "Everything did not index the sweep files in time");
                _output.WriteLine($"[SWEEP] {targets.Length} files indexed in the live Everything instance");

                for (var distance = 1; distance <= 7; distance++)
                {
                    var target = targets[distance - 1];
                    var typo = DistanceTestSupport.DeleteSpread(target, distance);
                    Assert.Equal(distance, DistanceTestSupport.OptimalStringAlignment(typo, target));
                    Assert.True(typo.Length >= 9, "sweep typos must reach the long-word tier");

                    // Below the required distance the candidate must not exist.
                    if (distance > 1)
                    {
                        var belowProvider = new SymSpellCandidateProvider(
                            vocabulary,
                            new EditDistancePolicy(distance - 1, distance - 1, 9)
                        );
                        var belowCandidates = belowProvider.FindCandidates(typo, 5, CancellationToken.None);
                        Assert.DoesNotContain(
                            belowCandidates,
                            c => c.Correction.Equals(target, StringComparison.OrdinalIgnoreCase)
                        );
                    }

                    var typoQuery = Query(typo + " path:\"" + testDir + "\"");
                    var rawCount = pipeClient.QueryCountSync(typoQuery, 256, CancellationToken.None);
                    Assert.Equal(0, rawCount);

                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var count = decorator.QueryCountSync(typoQuery, 256, CancellationToken.None);
                    watch.Stop();

                    Assert.True(count > 0, $"distance-{distance} typo '{typo}' must fall back to '{target}'");
                    Assert.NotNull(decorator.GetActiveFallback(typoQuery));

                    var results = WaitForResults(decorator, typoQuery, TimeSpan.FromSeconds(10));
                    Assert.Contains(
                        results,
                        r => r.FullPathAndFileName.Contains(target, StringComparison.OrdinalIgnoreCase)
                    );
                    _output.WriteLine(
                        $"[SWEEP] distance={distance} typo='{typo}' target='{target}' raw={rawCount} "
                            + $"corrected={count} latency={watch.Elapsed.TotalMilliseconds:F1} ms"
                    );
                }

                // The two original user-reported cases, verified live as well (both OSA
                // distance three against neuroscience).
                // The two original user-reported cases, verified live as well. neurosccien is
                // OSA distance three (inserted 'c' plus two deleted suffix characters);
                // neurosccience is distance one (an extra 'c' beside an existing one).
                var legacyCases = new (string Typo, int Distance)[] { ("neurosccien", 3), ("neurosccience", 1) };
                foreach (var (legacyTypo, legacyDistance) in legacyCases)
                {
                    Assert.Equal(
                        legacyDistance,
                        DistanceTestSupport.OptimalStringAlignment(legacyTypo, "neuroscience")
                    );
                    var legacyQuery = Query(legacyTypo + " path:\"" + testDir + "\"");
                    Assert.Equal(0, pipeClient.QueryCountSync(legacyQuery, 256, CancellationToken.None));
                    var legacyCount = decorator.QueryCountSync(legacyQuery, 256, CancellationToken.None);
                    Assert.True(legacyCount > 0, $"legacy typo '{legacyTypo}' must fall back to neuroscience");
                    Assert.NotNull(decorator.GetActiveFallback(legacyQuery));
                    _output.WriteLine(
                        $"[SWEEP] legacy typo='{legacyTypo}' target='neuroscience' corrected={legacyCount}"
                    );
                }
            }
            finally
            {
                Directory.Delete(testDir, recursive: true);
            }
        }

        private static string Capitalize(string word)
        {
            return char.ToUpperInvariant(word[0]) + word[1..];
        }

        /// <summary>
        /// SDK3 result lists fill their viewport asynchronously after the count reply; poll the
        /// range read until rows appear (the same way the UI's lazy page reads observe them).
        /// </summary>
        private static IList<SearchResult> WaitForResults(
            TypoFallbackClient decorator,
            SearchQuery query,
            TimeSpan timeout
        )
        {
            var deadline = DateTime.UtcNow + timeout;
            var results = decorator.QueryRangeSync(query, 0, 256, CancellationToken.None);
            while (results.Count == 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
                results = decorator.QueryRangeSync(query, 0, 256, CancellationToken.None);
            }

            return results;
        }

        private static bool WaitFor(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                    return true;
                Thread.Sleep(250);
            }

            return condition();
        }
    }
}
