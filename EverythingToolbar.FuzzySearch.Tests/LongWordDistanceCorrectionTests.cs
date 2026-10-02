using System;
using System.Threading;
using EverythingToolbar.FuzzySearch.Tests.Support;
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
            // Long scientific targets for the distance 4-7 corpus rows.
            "oligodendrocyte",
            "electrophysiology",
            "neurodegeneration",
            "immunohistochemistry",
        };

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
                typo = DistanceTestSupport.DeleteChars(correction, deletedIndices);
            }

            Assert.True(
                DistanceTestSupport.OptimalStringAlignment(typo, correction) == 3,
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
                typo = DistanceTestSupport.DeleteChars(correction, deletedIndices);
            }

            var provider = new SymSpellCandidateProvider(BuildVocabulary());

            var candidates = provider.FindCandidates(typo, 5, CancellationToken.None);

            Assert.NotEmpty(candidates);
            Assert.Equal(correction, candidates[0].Correction, ignoreCase: true);
        }

        /// <summary>
        /// Full 1-7 corpus: typo = the target with the listed characters removed, so the OSA
        /// distance is exactly the deletion count (length difference lower bound, subsequence
        /// upper bound) and never an assumption. Targets are real-looking long terms where
        /// multi-edit misspellings are plausible.
        /// </summary>
        public static TheoryData<int, string, int[]> DistanceOneToSevenCorpus()
        {
            return new TheoryData<int, string, int[]>
            {
                { 1, "neuroscience", new[] { 3 } },
                { 2, "neuroscience", new[] { 1, 5 } },
                { 3, "refrigerator", new[] { 1, 4, 6 } },
                { 4, "oligodendrocyte", new[] { 2, 5, 8, 11 } },
                { 5, "electrophysiology", new[] { 1, 4, 7, 10, 14 } },
                { 6, "neurodegeneration", new[] { 0, 3, 6, 9, 12, 15 } },
                { 7, "immunohistochemistry", new[] { 1, 4, 7, 10, 13, 16, 19 } },
            };
        }

        [Theory]
        [MemberData(nameof(DistanceOneToSevenCorpus))]
        public void Corpus_EveryDistanceOneToSeven_CorrectsAtItsLevel_AndNotBelow(
            int distance,
            string correction,
            int[] deletedIndices
        )
        {
            var typo = DistanceTestSupport.DeleteChars(correction, deletedIndices);
            Assert.True(
                DistanceTestSupport.OptimalStringAlignment(typo, correction) == distance,
                $"{typo} -> {correction} must be OSA distance {distance}, not assumed"
            );
            Assert.True(typo.Length >= 9, "corpus typos must be long words under the default threshold");

            // At the required distance the target is the closest correction.
            var at = new SymSpellCandidateProvider(BuildVocabulary(), new EditDistancePolicy(distance, distance, 9));
            var candidatesAt = at.FindCandidates(typo, 5, CancellationToken.None);
            Assert.NotEmpty(candidatesAt);
            Assert.Equal(correction, candidatesAt[0].Correction, ignoreCase: true);

            // One level below, the target must not appear: higher distances never leak downward.
            if (distance > EditDistancePolicy.MinDictionaryEditDistance)
            {
                var below = new SymSpellCandidateProvider(
                    BuildVocabulary(),
                    new EditDistancePolicy(distance - 1, distance - 1, 9)
                );
                var candidatesBelow = below.FindCandidates(typo, 5, CancellationToken.None);
                Assert.DoesNotContain(
                    candidatesBelow,
                    c => c.Correction.Equals(correction, StringComparison.OrdinalIgnoreCase)
                );
            }
        }

        [Fact]
        public void DistanceSevenPolicy_DoesNotCorrectMediumTerms()
        {
            // repomsp -> repomaps is distance two; a distance-seven index must not turn the
            // medium tier into seven edits.
            var provider = new SymSpellCandidateProvider(BuildVocabulary(), new EditDistancePolicy(7, 7, 9));

            var candidates = provider.FindCandidates("repomsp", 5, CancellationToken.None);

            Assert.NotEmpty(candidates);
            Assert.Equal("repomaps", candidates[0].Correction, ignoreCase: true);
            Assert.DoesNotContain(candidates, c => c.EditDistance > EditDistancePolicy.MediumTermMaxDistance);
        }

        [Fact]
        public void DistanceSevenPolicy_UnknownLongWordStaysUnresolved()
        {
            var provider = new SymSpellCandidateProvider(BuildVocabulary(), new EditDistancePolicy(7, 7, 9));

            Assert.Empty(provider.FindCandidates("pneumonoultramicros", 5, CancellationToken.None));
        }

        [Fact]
        public void DistanceThree_DoesNotLeakIntoMediumTerms()
        {
            // document -> dcmnt is three deletions (OSA distance three) but the five-character
            // result stays under the long-word threshold, so the medium cap of two applies even
            // when the index was built for distance three.
            var typo = DistanceTestSupport.DeleteChars("document", 1, 3, 5);
            Assert.Equal(3, DistanceTestSupport.OptimalStringAlignment(typo, "document"));
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
