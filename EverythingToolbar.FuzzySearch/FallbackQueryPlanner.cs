using System.Collections.Generic;

namespace EverythingToolbar.FuzzySearch
{
    public sealed record TermCorrection(string Original, string Corrected);

    public sealed record FallbackPlan(string CorrectedQuery, IReadOnlyList<TermCorrection> Corrections);

    /// <summary>
    /// Builds the corrected fallback query for a raw query that returned zero results. Only safe
    /// plain literals (per <see cref="SafeLiteralClassifier"/>) are replaced; all other query
    /// structure is copied through verbatim.
    /// </summary>
    public sealed class FallbackQueryPlanner
    {
        private readonly ITypoCandidateProvider _candidates;

        public FallbackQueryPlanner(ITypoCandidateProvider candidates)
        {
            _candidates = candidates;
        }

        public FallbackPlan? Plan(string rawQuery)
        {
            // Harness stub (commit 2): no fallback planning yet.
            return null;
        }
    }
}
