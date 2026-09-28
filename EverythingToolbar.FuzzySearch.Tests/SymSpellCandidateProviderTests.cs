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
            Assert.Equal(0, EditDistancePolicy.MaxDistanceFor(1));
            Assert.Equal(0, EditDistancePolicy.MaxDistanceFor(3));
            Assert.Equal(2, EditDistancePolicy.MaxDistanceFor(4));
            Assert.Equal(2, EditDistancePolicy.MaxDistanceFor(7));
            Assert.Equal(2, EditDistancePolicy.MaxDistanceFor(8));
            Assert.Equal(2, EditDistancePolicy.MaxDistanceFor(20));
        }
    }
}
