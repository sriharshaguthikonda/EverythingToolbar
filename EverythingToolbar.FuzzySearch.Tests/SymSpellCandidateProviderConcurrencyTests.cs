using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    /// <summary>
    /// Rebuilds must never block or corrupt concurrent lookups: the candidate index is built from
    /// a vocabulary snapshot and swapped atomically.
    /// </summary>
    public sealed class SymSpellCandidateProviderConcurrencyTests
    {
        [Fact]
        public async Task Rebuild_ConcurrentWithLookups_NeverBlocksOrCorrupts()
        {
            var vocabulary = new TokenVocabulary();
            for (var i = 0; i < 2000; i++)
            {
                var word = "token" + i.ToString("D4");
                vocabulary.AddPath(word);
            }

            var provider = new SymSpellCandidateProvider(vocabulary);
            var version = provider.IndexVersion;
            var failures = new List<Exception>();
            var lookups = 0;

            var lookupTask = Task.Run(() =>
            {
                for (var i = 0; i < 2000; i++)
                {
                    try
                    {
                        var term = i % 2 == 0 ? "tokne" + (i % 1000).ToString("D4") : "token" + i.ToString("D4");
                        var candidates = provider.FindCandidates(term, 3, CancellationToken.None);
                        foreach (var candidate in candidates)
                        {
                            Assert.False(string.IsNullOrWhiteSpace(candidate.Correction));
                            Assert.InRange(candidate.EditDistance, 1, 2);
                        }

                        Interlocked.Increment(ref lookups);
                    }
                    catch (Exception ex)
                    {
                        lock (failures)
                        {
                            failures.Add(ex);
                        }
                    }
                }
            });

            var rebuildTask = Task.Run(() =>
            {
                for (var i = 0; i < 40; i++)
                {
                    try
                    {
                        vocabulary.AddPath("extra" + i.ToString("D3"));
                        provider.Rebuild();
                        Assert.True(provider.IndexVersion >= version);
                    }
                    catch (Exception ex)
                    {
                        lock (failures)
                        {
                            failures.Add(ex);
                        }
                    }
                }
            });

            await Task.WhenAll(lookupTask, rebuildTask);

            Assert.Empty(failures);
            Assert.Equal(2000, lookups);
            Assert.Equal(40, provider.IndexVersion - version);
        }
    }
}
