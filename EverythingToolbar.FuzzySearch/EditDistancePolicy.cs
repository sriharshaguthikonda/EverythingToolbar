using System;

namespace EverythingToolbar.FuzzySearch
{
    /// <summary>
    /// Conservative distance limits by term length, with a configurable long-word tier. Short terms
    /// are never corrected (too ambiguous, think form/from). Medium terms allow two edits: the
    /// required corpus case repomsp->repomaps needs two (missing 'a' + transposition) at length 7,
    /// so the textbook 4-7->1 rule fails its own target; minimum distance is enforced by SymSpell's
    /// closest-verbosity instead. Long words may use a larger distance because the extra characters
    /// disambiguate (neurosccien->neuroscience is Damerau-Levenshtein OSA distance three), measured
    /// in the benchmark suite before defaults were chosen. SymSpell's Lookup throws when asked for a
    /// larger distance than the index was built with, so every lookup distance is clamped to
    /// MaxDictionaryEditDistance and out-of-range constructor values are clamped, never rejected.
    /// Tune from corpus evidence only.
    /// </summary>
    public sealed class EditDistancePolicy
    {
        public const int MinCorrectableLength = SafeLiteralClassifier.MinCorrectableLength;
        public const int MinDictionaryEditDistance = 1;
        public const int MaxSupportedDictionaryEditDistance = 3;
        public const int MediumTermMaxDistance = 2;
        public const int DefaultDictionaryEditDistance = 2;
        public const int DefaultLongWordEditDistance = 2;
        public const int DefaultLongWordMinLength = 9;

        public EditDistancePolicy(
            int maxDictionaryEditDistance = DefaultDictionaryEditDistance,
            int longWordMaxEditDistance = DefaultLongWordEditDistance,
            int longWordMinLength = DefaultLongWordMinLength
        )
        {
            MaxDictionaryEditDistance = Math.Clamp(
                maxDictionaryEditDistance,
                MinDictionaryEditDistance,
                MaxSupportedDictionaryEditDistance
            );
            LongWordMaxEditDistance = Math.Clamp(
                longWordMaxEditDistance,
                MinDictionaryEditDistance,
                MaxDictionaryEditDistance
            );
            LongWordMinLength = Math.Max(longWordMinLength, MinCorrectableLength);
        }

        public static EditDistancePolicy Default { get; } = new();

        /// <summary>Distance the SymSpell delete index is built with; upper bound for every lookup.</summary>
        public int MaxDictionaryEditDistance { get; }

        /// <summary>Lookup distance allowed for terms at least <see cref="LongWordMinLength"/> long.</summary>
        public int LongWordMaxEditDistance { get; }

        /// <summary>Term length from which the long-word distance applies.</summary>
        public int LongWordMinLength { get; }

        public int MaxDistanceFor(int termLength)
        {
            if (termLength <= 3)
            {
                return 0;
            }

            if (termLength < LongWordMinLength)
            {
                return Math.Min(MediumTermMaxDistance, MaxDictionaryEditDistance);
            }

            return Math.Min(LongWordMaxEditDistance, MaxDictionaryEditDistance);
        }
    }
}
