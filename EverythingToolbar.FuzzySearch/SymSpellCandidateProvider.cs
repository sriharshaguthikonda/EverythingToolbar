using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace EverythingToolbar.FuzzySearch
{
    /// <summary>
    /// ITypoCandidateProvider over the SymSpell delete-based index, built from the filename token
    /// vocabulary. SymSpell is used as a dependency, not reimplemented. Lookup is O(1)-ish per term;
    /// never returns the queried term itself and never returns candidates beyond the length policy.
    /// The policy is part of the index identity: rebuilding with a new policy bumps IndexVersion so
    /// version-gated plan caches re-plan against the new correction semantics.
    /// </summary>
    public sealed class SymSpellCandidateProvider : ITypoCandidateProvider
    {
        private const int SymSpellInitialCapacity = 82764;

        // SymSpell's default, and the prefix length used for index distances 1-6. Kept internal:
        // users tune edit distance, not this knob.
        private const int DefaultPrefixLength = 7;

        /// <summary>
        /// Smallest safe SymSpell prefix length for an index distance. SymSpell 6.7.3's constructor
        /// throws ArgumentOutOfRangeException unless prefixLength is strictly greater than the
        /// maxDictionaryEditDistance it is built with (verified in SymSpell/SymSpell.cs v6.7.3), so
        /// distance 7 requires a prefix of at least 8. The default 7 stays for distances 1-6: a
        /// larger prefix enumerates more deletes inside the prefix window, growing index size and
        /// build time without a measured recall benefit, and the delete index itself has no upper
        /// distance bound. Users control edit distance; this stays an implementation detail.
        /// </summary>
        public static int PrefixLengthFor(int maxDictionaryEditDistance) =>
            Math.Max(DefaultPrefixLength, maxDictionaryEditDistance + 1);

        private readonly TokenVocabulary _vocabulary;
        private readonly object _gate = new();
        private EditDistancePolicy _policy;
        private SymSpell _symSpell;
        private bool _built;
        private int _indexVersion;

        public SymSpellCandidateProvider(TokenVocabulary vocabulary)
            : this(vocabulary, EditDistancePolicy.Default) { }

        public SymSpellCandidateProvider(TokenVocabulary vocabulary, EditDistancePolicy policy)
        {
            _vocabulary = vocabulary;
            _policy = policy;
            _symSpell = new SymSpell(
                SymSpellInitialCapacity,
                policy.MaxDictionaryEditDistance,
                PrefixLengthFor(policy.MaxDictionaryEditDistance)
            );
        }

        /// <summary>The clamped policy of the active index (may differ from raw settings values).</summary>
        public EditDistancePolicy Policy
        {
            get
            {
                lock (_gate)
                {
                    return _policy;
                }
            }
        }

        public int IndexVersion => Volatile.Read(ref _indexVersion);

        public int EntryCount
        {
            get
            {
                EnsureBuilt();
                lock (_gate)
                {
                    return _symSpell.WordCount;
                }
            }
        }

        public IReadOnlyList<TypoCandidate> FindCandidates(
            string term,
            int maxResults,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureBuilt();

            // MaxDistanceFor never exceeds the policy's dictionary distance, which is exactly what
            // the active SymSpell instance was built with, so Lookup cannot throw.
            var maxDistance = Policy.MaxDistanceFor(term.Length);
            if (maxDistance == 0)
            {
                return [];
            }

            List<SymSpell.SuggestItem> suggestions;
            lock (_gate)
            {
                suggestions = _symSpell.Lookup(term.ToLowerInvariant(), SymSpell.Verbosity.Closest, maxDistance);
            }

            return suggestions
                .Where(s => s.distance > 0 && !s.term.Equals(term, System.StringComparison.OrdinalIgnoreCase))
                .Take(maxResults)
                .Select(s => new TypoCandidate(s.term, s.distance, s.count > 0 ? s.count : 1))
                .ToList();
        }

        /// <summary>
        /// Rebuilds the index from the current vocabulary state with the active policy. The new
        /// index is built from a vocabulary snapshot OUTSIDE the lookup lock; only the reference
        /// swap is locked, so candidate lookups never wait for a rebuild.
        /// </summary>
        public void Rebuild()
        {
            Rebuild(Policy);
        }

        /// <summary>
        /// Rebuilds the index with a new distance policy. The old index stays active until the
        /// replacement is complete; the swap also bumps <see cref="IndexVersion"/>, which
        /// invalidates cached fallback plans planned under the old semantics.
        /// </summary>
        public void Rebuild(EditDistancePolicy policy)
        {
            var replacement = Build(_vocabulary, policy);
            lock (_gate)
            {
                _policy = policy;
                _symSpell = replacement;
                _built = true;
                _indexVersion++;
            }
        }

        private void EnsureBuilt()
        {
            if (_built)
                return;

            var replacement = Build(_vocabulary, Policy);
            lock (_gate)
            {
                if (!_built)
                {
                    _symSpell = replacement;
                    _built = true;
                    _indexVersion++;
                }
            }
        }

        private static SymSpell Build(TokenVocabulary vocabulary, EditDistancePolicy policy)
        {
            var symSpell = new SymSpell(
                SymSpellInitialCapacity,
                policy.MaxDictionaryEditDistance,
                PrefixLengthFor(policy.MaxDictionaryEditDistance)
            );
            foreach (var entry in vocabulary.WordsByFrequency())
            {
                symSpell.CreateDictionaryEntry(entry.Normalized, entry.Frequency > 0 ? entry.Frequency : 1);
            }

            return symSpell;
        }
    }
}
