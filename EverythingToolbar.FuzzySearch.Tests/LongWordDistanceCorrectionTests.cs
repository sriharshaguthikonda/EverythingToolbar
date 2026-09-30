using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    /// <summary>
    /// Permanent regression corpus for distance-three long-word correction. Expected distances are
    /// computed with a local optimal-string-alignment reference (the metric SymSpell implements,
    /// Damerau-Levenshtein OSA) instead of being guessed. The user-reported case is
    /// neurosccien -> neuroscience: an inserted 'c' plus two deleted suffix characters, OSA
    /// distance three, which the old fixed policy (max lookup distance two) could never return.
    /// </summary>
    public sealed class LongWordDistanceCorrectionTests
    {
        private static readonly string[] VocabularyWords =
        {
            "neuroscience",
            "independent",
            "refrigerator",
            "constitutional",
            "characteristic",
            "document",
            "repomaps",
            "powertoy",
            "guthikonda",
            "ollama",
            "kanata",
            "clinical",
            "project",
        };

        /// <summary>Reference optimal string alignment (restricted Damerau-Levenshtein) distance.</summary>
        private static int OptimalStringAlignment(string a, string b)
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

        /// <summary>Removes the characters at the given indices; three deletions of an n-char word.</summary>
        private static string DeleteChars(string word, params int[] indices)
        {
            var removed = new HashSet<int>(indices);
            return new string(word.Where((_, i) => !removed.Contains(i)).ToArray());
        }

        private static TokenVocabulary BuildVocabulary()
        {
            var vocabulary = new TokenVocabulary();
            foreach (var word in VocabularyWords)
            {
                vocabulary.AddPath(word);
                vocabulary.AddPath(word);
                vocabulary.AddPath(word);
            }

            return vocabulary;
        }

        private static SymSpellCandidateProvider CreateDistanceThreeProvider()
        {
            return new SymSpellCandidateProvider(BuildVocabulary(), new EditDistancePolicy(3, 3, 9));
        }

        public static TheoryData<string, string, int[]> DistanceThreeLongWordCorpus()
        {
            return new TheoryData<string, string, int[]>
            {
                // The user-observed case: insert 'c' after the first 'c', then drop final "ce".
                { "neurosccien", "neuroscience", Array.Empty<int>() },
                // Three-deletion typos of unrelated long words: length differs by exactly three, so
                // the OSA distance is exactly three by construction.
                { "rfrgrator", "refrigerator", new int[] { 1, 4, 6 } },
                { "chrctristic", "characteristic", new int[] { 2, 4, 7 } },
                { "constiuionl", "constitutional", new int[] { 6, 8, 12 } },
            };
        }

        [Theory]
        [MemberData(nameof(DistanceThreeLongWordCorpus))]
        public void Corpus_DistanceIsExactlyThree_AndPolicyThreeCorrectsIt(
            string typo,
            string correction,
            int[] deletedIndices
        )
        {
            if (deletedIndices.Length > 0)
            {
                typo = DeleteChars(correction, deletedIndices);
            }

            Assert.True(
                OptimalStringAlignment(typo, correction) == 3,
                $"{typo} -> {correction} must be OSA distance three"
            );
            Assert.True(typo.Length >= 9, "corpus typos must be long words under the default threshold");

            var provider = CreateDistanceThreeProvider();

            var candidates = provider.FindCandidates(typo, 5, CancellationToken.None);

            Assert.NotEmpty(candidates);
            Assert.Equal(correction, candidates[0].Correction, ignoreCase: true);
        }

        [Theory]
        [MemberData(nameof(DistanceThreeLongWordCorpus))]
        public void Corpus_DefaultPolicy_CorrectsDistanceThreeLongWords(
            string typo,
            string correction,
            int[] deletedIndices
        )
        {
            if (deletedIndices.Length > 0)
            {
                typo = DeleteChars(correction, deletedIndices);
            }

            var provider = new SymSpellCandidateProvider(BuildVocabulary());

            var candidates = provider.FindCandidates(typo, 5, CancellationToken.None);

            Assert.NotEmpty(candidates);
            Assert.Equal(correction, candidates[0].Correction, ignoreCase: true);
        }

        [Fact]
        public void DistanceThree_DoesNotLeakIntoMediumTerms()
        {
            // document -> dcmnt is three deletions (OSA distance three) but the five-character
            // result stays under the long-word threshold, so the medium cap of two applies even
            // when the index was built for distance three.
            var typo = DeleteChars("document", 1, 3, 5);
            Assert.Equal(3, OptimalStringAlignment(typo, "document"));
            Assert.Equal(5, typo.Length);

            var provider = CreateDistanceThreeProvider();

            Assert.Empty(provider.FindCandidates(typo, 5, CancellationToken.None));
        }

        [Fact]
        public void DistanceThree_ShortTermsStayUncorrected()
        {
            var provider = CreateDistanceThreeProvider();

            Assert.Empty(provider.FindCandidates("frm", 5, CancellationToken.None));
        }

        [Fact]
        public void UnknownLongWordTypos_ProduceNoFalseCorrection()
        {
            // "photobiology" is absent from the vocabulary; its distance-three mutations must not
            // be "corrected" to an unrelated vocabulary word.
            var provider = CreateDistanceThreeProvider();

            foreach (var typo in new[] { "photobiolgy", "phtobiology", "fotobiology" })
            {
                Assert.Empty(provider.FindCandidates(typo, 5, CancellationToken.None));
            }
        }

        [Fact]
        public void Planner_WithDistanceThreePolicy_ProposesNeurosccienCorrection()
        {
            var planner = new FallbackQueryPlanner(AliasStore.Empty, CreateDistanceThreeProvider());

            var plan = planner.Plan("neurosccien");

            Assert.NotNull(plan);
            Assert.Contains("neuroscience", plan!.CorrectedQuery);
            Assert.Contains(plan.Corrections, c => c.Original == "neurosccien");
        }

        [Fact]
        public void Planner_DefaultPolicy_ProposesCorrectionForDistanceThreeTypo()
        {
            var planner = new FallbackQueryPlanner(AliasStore.Empty, new SymSpellCandidateProvider(BuildVocabulary()));

            var plan = planner.Plan("neurosccien");

            Assert.NotNull(plan);
            Assert.Contains("neuroscience", plan!.CorrectedQuery);
        }
    }
}
