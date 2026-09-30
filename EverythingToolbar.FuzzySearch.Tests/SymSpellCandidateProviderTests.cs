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

            var ceiling = new EditDistancePolicy(5, 5, 99);
            Assert.Equal(3, ceiling.MaxDictionaryEditDistance);
            Assert.Equal(3, ceiling.LongWordMaxEditDistance);
            Assert.Equal(99, ceiling.LongWordMinLength);
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
