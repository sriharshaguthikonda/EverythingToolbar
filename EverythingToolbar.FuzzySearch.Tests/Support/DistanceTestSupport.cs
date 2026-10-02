using System;
using System.Collections.Generic;
using System.Linq;

namespace EverythingToolbar.FuzzySearch.Tests.Support
{
    /// <summary>
    /// Shared string-distance helpers for the correction corpus and live tests: a reference
    /// optimal-string-alignment implementation (the metric SymSpell implements,
    /// Damerau-Levenshtein OSA) so expected distances are computed, never assumed, and a
    /// deterministic spread-deletion mutator whose result is OSA-exact by construction.
    /// </summary>
    public static class DistanceTestSupport
    {
        /// <summary>Reference optimal string alignment (restricted Damerau-Levenshtein) distance.</summary>
        public static int OptimalStringAlignment(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (var i = 0; i <= a.Length; i++)
            {
                d[i, 0] = i;
            }

            for (var j = 0; j <= b.Length; j++)
            {
                d[0, j] = j;
            }

            for (var i = 1; i <= a.Length; i++)
            {
                for (var j = 1; j <= b.Length; j++)
                {
                    var substitution = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + substitution);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    {
                        d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                    }
                }
            }

            return d[a.Length, b.Length];
        }

        /// <summary>Removes the characters at the given indices.</summary>
        public static string DeleteChars(string word, params int[] indices)
        {
            var removed = new HashSet<int>(indices);
            return new string(word.Where((_, i) => !removed.Contains(i)).ToArray());
        }

        /// <summary>
        /// Deletes <paramref name="times"/> evenly spread characters. Indices stay unique whenever
        /// the word has at least nine-plus-times characters, so the result is exactly
        /// <paramref name="times"/> edits away (length-difference lower bound, subsequence upper
        /// bound) - the same construction the 1-7 corpus rows rely on.
        /// </summary>
        public static string DeleteSpread(string word, int times)
        {
            var removed = new HashSet<int>();
            for (var k = 0; k < times; k++)
            {
                var idx = times == 1 ? word.Length / 2 : 1 + (k * (word.Length - 2)) / (times - 1);
                removed.Add(Math.Min(idx, word.Length - 1));
            }

            return new string(word.Where((_, i) => !removed.Contains(i)).ToArray());
        }
    }
}
