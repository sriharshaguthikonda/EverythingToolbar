using System.Threading;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    public sealed class SymSpellCandidateProviderTests
    {
        private static TokenVocabulary BuildVocabulary()
        {
            var vocabulary = new TokenVocabulary();
            foreach (
                var word in new[]
                {
                    "neuroscience",
                    "document",
                    "attachment",
                    "repomaps",
                    "powertoy",
                    "Guthikonda",
                    "ollama",
                    "kanata",
                    "form",
                    "from",
                    "clinical",
                    "project",
                    "tools",
                    "notes",
                }
            )
            {
                vocabulary.AddPath(word);
                vocabulary.AddPath(word);
                vocabulary.AddPath(word);
            }

            return vocabulary;
        }

        [Theory]
        [InlineData("neurosicence", "neuroscience")]
        [InlineData("docuemnt", "document")]
        [InlineData("attachement", "attachment")]
        [InlineData("repomsp", "repomaps")]
        [InlineData("powertyo", "powertoy")]
        [InlineData("guthikodna", "Guthikonda")]
        [InlineData("ollma", "ollama")]
        [InlineData("kanataa", "kanata")]
        public void FindCandidates_RepresentativeTypos_FindRightCorrection(string typo, string correction)
        {
            var provider = new SymSpellCandidateProvider(BuildVocabulary());

            var candidates = provider.FindCandidates(typo, 5, CancellationToken.None);

            Assert.NotEmpty(candidates);
            Assert.Equal(correction, candidates[0].Correction, ignoreCase: true);
        }

        [Fact]
        public void FindCandidates_ShortTerms_ReturnNothing()
        {
            var provider = new SymSpellCandidateProvider(BuildVocabulary());

            Assert.Empty(provider.FindCandidates("abc", 5, CancellationToken.None));
            Assert.Empty(provider.FindCandidates("frm", 5, CancellationToken.None));
        }

        [Fact]
        public void FindCandidates_NeverReturnsTermItself()
        {
            var provider = new SymSpellCandidateProvider(BuildVocabulary());

            var candidates = provider.FindCandidates("ollama", 5, CancellationToken.None);

            Assert.DoesNotContain(
                candidates,
                c => c.Correction.Equals("ollama", System.StringComparison.OrdinalIgnoreCase)
            );
        }

        [Fact]
        public void FindCandidates_RespectsMaxResults()
        {
            var provider = new SymSpellCandidateProvider(BuildVocabulary());

            var candidates = provider.FindCandidates("neurosicence", 1, CancellationToken.None);

            Assert.Single(candidates);
        }

        [Fact]
        public void Rebuild_PicksUpNewVocabularyWords()
        {
            var vocabulary = BuildVocabulary();
            var provider = new SymSpellCandidateProvider(vocabulary);
            Assert.Empty(provider.FindCandidates("fissh", 5, CancellationToken.None));

            vocabulary.AddPath("ZebraFish");
            provider.Rebuild();

            Assert.NotEmpty(provider.FindCandidates("fissh", 5, CancellationToken.None));
        }

        [Fact]
        public void EditDistancePolicy_ShortTermsNeverCorrected()
        {
            Assert.Equal(0, EditDistancePolicy.Default.MaxDistanceFor(1));
            Assert.Equal(0, EditDistancePolicy.Default.MaxDistanceFor(3));
            Assert.Equal(2, EditDistancePolicy.Default.MaxDistanceFor(4));
            Assert.Equal(2, EditDistancePolicy.Default.MaxDistanceFor(7));
            Assert.Equal(2, EditDistancePolicy.Default.MaxDistanceFor(8));
            Assert.Equal(3, EditDistancePolicy.Default.MaxDistanceFor(9));
            Assert.Equal(3, EditDistancePolicy.Default.MaxDistanceFor(20));
        }

        [Fact]
        public void EditDistancePolicy_ClampsOutOfRangeValues()
        {
            var floor = new EditDistancePolicy(0, 9, 2);
            Assert.Equal(1, floor.MaxDictionaryEditDistance);
            Assert.Equal(1, floor.LongWordMaxEditDistance);
            Assert.Equal(4, floor.LongWordMinLength);

            var longAboveDictionary = new EditDistancePolicy(2, 7, 2);
            Assert.Equal(2, longAboveDictionary.MaxDictionaryEditDistance);
            Assert.Equal(2, longAboveDictionary.LongWordMaxEditDistance);
            Assert.Equal(4, longAboveDictionary.LongWordMinLength);

            var ceiling = new EditDistancePolicy(9, 9, 99);
            Assert.Equal(EditDistancePolicy.MaxSupportedDictionaryEditDistance, ceiling.MaxDictionaryEditDistance);
            Assert.Equal(EditDistancePolicy.MaxSupportedDictionaryEditDistance, ceiling.LongWordMaxEditDistance);
            Assert.Equal(99, ceiling.LongWordMinLength);
        }

        [Fact]
        public void EditDistancePolicy_SupportsFullRangeOneToSeven()
        {
            for (
                var distance = EditDistancePolicy.MinDictionaryEditDistance;
                distance <= EditDistancePolicy.MaxSupportedDictionaryEditDistance;
                distance++
            )
            {
                var policy = new EditDistancePolicy(distance, distance, 9);
                Assert.Equal(distance, policy.MaxDictionaryEditDistance);
                Assert.Equal(distance, policy.LongWordMaxEditDistance);
                Assert.Equal(distance, policy.MaxDistanceFor(9));
            }
        }

        [Fact]
        public void EditDistancePolicy_ValueEquality()
        {
            var a = new EditDistancePolicy(3, 3, 9);
            var b = new EditDistancePolicy(3, 3, 9);
            var c = new EditDistancePolicy(4, 3, 9);

            Assert.True(a == b);
            Assert.False(a == c);
            Assert.True(a != c);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.True(a.Equals((object)b));
            Assert.False(a.Equals(null));
        }

        [Fact]
        public void PrefixLengthFor_SatisfiesSymSpellInvariantForEverySupportedDistance()
        {
            // SymSpell 6.7.3 throws when prefixLength <= maxDictionaryEditDistance (verified in its
            // constructor), so the derived prefix must stay strictly above the index distance.
            for (
                var distance = EditDistancePolicy.MinDictionaryEditDistance;
                distance <= EditDistancePolicy.MaxSupportedDictionaryEditDistance;
                distance++
            )
            {
                var prefix = SymSpellCandidateProvider.PrefixLengthFor(distance);
                Assert.True(
                    prefix > distance,
                    $"prefix {prefix} must exceed index distance {distance} or SymSpell's constructor throws"
                );
            }

            // Distances 1-6 keep SymSpell's default prefix; only distance 7 needs the bump to 8.
            Assert.Equal(7, SymSpellCandidateProvider.PrefixLengthFor(1));
            Assert.Equal(7, SymSpellCandidateProvider.PrefixLengthFor(6));
            Assert.Equal(8, SymSpellCandidateProvider.PrefixLengthFor(7));
        }

        [Fact]
        public void FindCandidates_DistanceSevenPolicy_CorrectsSevenEditLongWord()
        {
            var vocabulary = BuildVocabulary();
            vocabulary.AddPath("immunohistochemistry");
            var provider = new SymSpellCandidateProvider(vocabulary, new EditDistancePolicy(7, 7, 9));

            // Seven deletions from the 20-character word (a subsequence with seven characters
            // removed is OSA distance exactly seven); the asserted corpus lives in
            // LongWordDistanceCorrectionTests - here the policy ceiling is what is proven.
            var candidates = provider.FindCandidates("imuohstchmitr", 5, CancellationToken.None);

            Assert.NotEmpty(candidates);
            Assert.Equal("immunohistochemistry", candidates[0].Correction, ignoreCase: true);
        }

        [Fact]
        public void EditDistancePolicy_LongWordsUseConfiguredDistanceOnlyAboveThreshold()
        {
            var policy = new EditDistancePolicy(3, 3, 9);
            Assert.Equal(0, policy.MaxDistanceFor(3));
            Assert.Equal(2, policy.MaxDistanceFor(4));
            Assert.Equal(2, policy.MaxDistanceFor(8));
            Assert.Equal(3, policy.MaxDistanceFor(9));
            Assert.Equal(3, policy.MaxDistanceFor(20));
        }

        [Fact]
        public void FindCandidates_DistanceThreePolicy_CorrectsThreeEditLongWord()
        {
            var provider = new SymSpellCandidateProvider(BuildVocabulary(), new EditDistancePolicy(3, 3, 9));

            var candidates = provider.FindCandidates("neurosccien", 5, CancellationToken.None);

            Assert.NotEmpty(candidates);
            Assert.Equal("neuroscience", candidates[0].Correction, ignoreCase: true);
        }

        [Fact]
        public void FindCandidates_DefaultPolicy_CorrectsThreeEditLongWord()
        {
            var provider = new SymSpellCandidateProvider(BuildVocabulary());

            var candidates = provider.FindCandidates("neurosccien", 5, CancellationToken.None);

            Assert.NotEmpty(candidates);
            Assert.Equal("neuroscience", candidates[0].Correction, ignoreCase: true);
        }

        [Fact]
        public void Rebuild_WithNewPolicy_AppliesItAndBumpsIndexVersion()
        {
            var provider = new SymSpellCandidateProvider(BuildVocabulary(), new EditDistancePolicy(2, 2, 9));
            Assert.Empty(provider.FindCandidates("neurosccien", 5, CancellationToken.None));
            var versionBefore = provider.IndexVersion;

            provider.Rebuild(new EditDistancePolicy(3, 3, 9));

            Assert.Equal(3, provider.Policy.MaxDictionaryEditDistance);
            Assert.Equal(versionBefore + 1, provider.IndexVersion);
            Assert.Equal(
                "neuroscience",
                provider.FindCandidates("neurosccien", 5, CancellationToken.None)[0].Correction
            );
        }
    }
}
