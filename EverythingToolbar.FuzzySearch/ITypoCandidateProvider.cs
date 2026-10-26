using System.Collections.Generic;
using System.Threading;

namespace EverythingToolbar.FuzzySearch
{
    public sealed record TypoCandidate(string Correction, int EditDistance, long Frequency);

    public interface ITypoCandidateProvider
    {
        /// <summary>Increments whenever the underlying index changes; caches keyed to it re-plan.</summary>
        int IndexVersion { get; }

        /// <summary>
        /// Returns spelling candidates for <paramref name="term"/>, best first. Never returns the
        /// term itself. Must be efficient (indexed lookup, no linear scan over paths).
        /// </summary>
        IReadOnlyList<TypoCandidate> FindCandidates(string term, int maxResults, CancellationToken cancellationToken);
    }
}
