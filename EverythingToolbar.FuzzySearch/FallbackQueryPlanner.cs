using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace EverythingToolbar.FuzzySearch
{
    public sealed record TermCorrection(string Original, string Corrected);

    public sealed record FallbackPlan(string CorrectedQuery, IReadOnlyList<TermCorrection> Corrections);

    /// <summary>
    /// Builds the corrected fallback query for a raw query that returned zero results. Only safe
    /// plain literals (per <see cref="SafeLiteralClassifier"/>) are touched, and raw text is kept
    /// inside OR groups, never deleted. Everything structure is copied through verbatim. Zero-result
    /// gating (raw query wins) is enforced by the caller, not here.
    /// </summary>
    public sealed class FallbackQueryPlanner
    {
        /// <summary>Maximum alternatives kept inside one OR group (raw term excluded).</summary>
        public const int MaxAlternativesPerTerm = 3;

        private static readonly SafeLiteralClassifier Classifier = new();

        private readonly AliasStore? _aliases;
        private readonly ITypoCandidateProvider? _candidates;

        public FallbackQueryPlanner(AliasStore? aliases = null, ITypoCandidateProvider? candidates = null)
        {
            _aliases = aliases;
            _candidates = candidates;
        }

        public FallbackPlan? Plan(string rawQuery, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(rawQuery))
            {
                return null;
            }

            var wholeQueryCanonical = _aliases?.TryGetCanonical(rawQuery);
            if (!string.IsNullOrEmpty(wholeQueryCanonical))
            {
                return new FallbackPlan(
                    wholeQueryCanonical,
                    new[] { new TermCorrection(rawQuery, wholeQueryCanonical) }
                );
            }

            var terms = Classifier.Classify(rawQuery);
            var corrections = new List<TermCorrection>();
            var corrected = new StringBuilder(rawQuery);

            // Right-to-left so earlier term indices stay valid while spans are replaced.
            for (var i = terms.Count - 1; i >= 0; i--)
            {
                var term = terms[i];
                if (!SafeLiteralClassifier.IsCorrectable(term))
                {
                    continue;
                }

                var alternatives = CollectAlternatives(term.Text, cancellationToken);
                if (alternatives.Count == 0)
                {
                    continue;
                }

                var replacement = "<" + term.Text + "|" + string.Join("|", alternatives) + ">";
                corrected.Remove(term.Index, term.Text.Length);
                corrected.Insert(term.Index, replacement);
                corrections.Add(new TermCorrection(term.Text, string.Join("|", alternatives)));
            }

            return corrections.Count > 0 ? new FallbackPlan(corrected.ToString(), corrections) : null;
        }

        private List<string> CollectAlternatives(string term, CancellationToken cancellationToken)
        {
            var entries = new List<(string Correction, bool IsAlias, int Distance, long Frequency)>();

            void AddIfValid(string? correction, bool isAlias, int distance, long frequency)
            {
                if (
                    string.IsNullOrEmpty(correction)
                    || correction.IndexOf(' ') >= 0
                    || correction.Equals(term, StringComparison.OrdinalIgnoreCase)
                    || entries.Exists(e => e.Correction.Equals(correction, StringComparison.OrdinalIgnoreCase))
                )
                {
                    return;
                }

                entries.Add((correction, isAlias, distance, frequency));
            }

            // Explicit preferred spellings rank above inferred typo candidates.
            AddIfValid(_aliases?.TryGetCanonical(term), isAlias: true, distance: 0, frequency: long.MaxValue);

            if (_candidates is not null)
            {
                foreach (var candidate in _candidates.FindCandidates(term, MaxAlternativesPerTerm, cancellationToken))
                {
                    AddIfValid(candidate.Correction, isAlias: false, candidate.EditDistance, candidate.Frequency);
                }
            }

            return entries
                .OrderBy(e => e.IsAlias ? 0 : 1)
                .ThenBy(e => e.Distance)
                .ThenByDescending(e => e.Frequency)
                .ThenBy(e => e.Correction, StringComparer.OrdinalIgnoreCase)
                .Take(MaxAlternativesPerTerm)
                .Select(e => e.Correction)
                .ToList();
        }
    }
}
