using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace EverythingToolbar.FuzzySearch.Tests.Support
{
    /// <summary>Test double: serves fixed candidate lists; never returns the term itself.</summary>
    public sealed class FakeCandidateProvider : ITypoCandidateProvider
    {
        private readonly Dictionary<string, IReadOnlyList<TypoCandidate>> _candidates;

        public FakeCandidateProvider(Dictionary<string, IReadOnlyList<TypoCandidate>> candidates)
        {
            _candidates = candidates;
        }

        public static FakeCandidateProvider FromCorrections(params (string Typo, string Correction)[] corrections)
        {
            return new FakeCandidateProvider(
                corrections.ToDictionary(
                    c => c.Typo,
                    c => (IReadOnlyList<TypoCandidate>)new[] { new TypoCandidate(c.Correction, 1, 1) }
                )
            );
        }

        public IReadOnlyList<TypoCandidate> FindCandidates(
            string term,
            int maxResults,
            CancellationToken cancellationToken
        )
        {
            return _candidates.TryGetValue(term, out var candidates)
                ? candidates.Take(maxResults).ToList()
                : new List<TypoCandidate>();
        }
    }
}
