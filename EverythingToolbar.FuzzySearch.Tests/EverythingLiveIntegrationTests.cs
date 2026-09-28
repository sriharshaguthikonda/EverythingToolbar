using System;
using System.IO;
using System.Threading;
using Config.Net;
using EverythingToolbar.App;
using EverythingToolbar.App.Search;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Core.Search;
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

                // Range results materialize through the decorator.
                var results = decorator.QueryRangeSync(
                    Query("neurosicence path:\"" + testDir + "\""),
                    0,
                    256,
                    CancellationToken.None
                );
                Assert.Contains(results, r => r.FileName.StartsWith("Neuroscience_Project_"));

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
