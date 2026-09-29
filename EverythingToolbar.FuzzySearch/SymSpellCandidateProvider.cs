using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace EverythingToolbar.FuzzySearch
{
    /// <summary>
    /// ITypoCandidateProvider over the SymSpell delete-based index, built from the filename token
    /// vocabulary. SymSpell is used as a dependency, not reimplemented. Lookup is O(1)-ish per term;
    /// never returns the queried term itself and never returns candidates beyond the length policy.
    /// </summary>
    public sealed class SymSpellCandidateProvider : ITypoCandidateProvider
    {
        private const int SymSpellInitialCapacity = 82764;
        private const int MaxDictionaryEditDistance = 2;
        private const int PrefixLength = 7;

        private readonly TokenVocabulary _vocabulary;
        private readonly object _gate = new();
        private SymSpell _symSpell = new(SymSpellInitialCapacity, MaxDictionaryEditDistance, PrefixLength);
        private bool _built;
        private int _indexVersion;

        public SymSpellCandidateProvider(TokenVocabulary vocabulary)
        {
            _vocabulary = vocabulary;
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

            var maxDistance = EditDistancePolicy.MaxDistanceFor(term.Length);
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
        /// Rebuilds the index from the current vocabulary state (e.g. after a refresh). The new
        /// index is built from a vocabulary snapshot OUTSIDE the lookup lock; only the reference
        /// swap is locked, so candidate lookups never wait for a rebuild.
        /// </summary>
        public void Rebuild()
        {
            var replacement = Build(_vocabulary);
            lock (_gate)
            {
                _symSpell = replacement;
                _built = true;
                _indexVersion++;
            }
        }

        private void EnsureBuilt()
        {
            if (_built)
                return;

            var replacement = Build(_vocabulary);
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

        private static SymSpell Build(TokenVocabulary vocabulary)
        {
            var symSpell = new SymSpell(SymSpellInitialCapacity, MaxDictionaryEditDistance, PrefixLength);
            foreach (var entry in vocabulary.WordsByFrequency())
            {
                symSpell.CreateDictionaryEntry(entry.Normalized, entry.Frequency > 0 ? entry.Frequency : 1);
            }

            return symSpell;
        }
    }
}
