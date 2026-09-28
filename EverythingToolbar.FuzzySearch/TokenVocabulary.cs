using System;
using System.Collections.Generic;
using System.Linq;

namespace EverythingToolbar.FuzzySearch
{
    /// <summary>
    /// Compact in-memory spelling vocabulary derived from Everything results: normalized word ->
    /// display casing + local frequency, plus compound whole forms such as "repo_maps". This is a
    /// word list, not a filesystem index. Thread-safe.
    /// </summary>
    public sealed class TokenVocabulary
    {
        private readonly object _gate = new();
        private readonly FilenameTokenizer _tokenizer = new();
        private readonly Dictionary<string, long> _wordFrequency = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _wordDisplay = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _compoundFrequency = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _compoundDisplay = new(StringComparer.Ordinal);

        public int WordCount
        {
            get
            {
                lock (_gate)
                {
                    return _wordFrequency.Count;
                }
            }
        }

        public int CompoundCount
        {
            get
            {
                lock (_gate)
                {
                    return _compoundFrequency.Count;
                }
            }
        }

        public void AddPath(string path)
        {
            var (words, compounds) = _tokenizer.TokenizePath(path);
            lock (_gate)
            {
                foreach (var word in words)
                {
                    AddWordLocked(word);
                }

                foreach (var compound in compounds)
                {
                    AddCompoundLocked(compound);
                }
            }
        }

        public void AddPaths(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                AddPath(path);
            }
        }

        /// <summary>Normalized word or compound lookup; null when unknown.</summary>
        public VocabularyEntry? Find(string normalized)
        {
            var key = normalized.ToLowerInvariant();
            lock (_gate)
            {
                if (_wordFrequency.TryGetValue(key, out var wordFrequency))
                {
                    return new VocabularyEntry(key, _wordDisplay[key], wordFrequency, IsCompound: false);
                }

                return _compoundFrequency.TryGetValue(key, out var compoundFrequency)
                    ? new VocabularyEntry(key, _compoundDisplay[key], compoundFrequency, IsCompound: true)
                    : null;
            }
        }

        public IReadOnlyList<VocabularyEntry> WordsByFrequency()
        {
            lock (_gate)
            {
                return _wordFrequency
                    .Select(kvp => new VocabularyEntry(kvp.Key, _wordDisplay[kvp.Key], kvp.Value, IsCompound: false))
                    .OrderByDescending(e => e.Frequency)
                    .ToList();
            }
        }

        private void AddWordLocked(string word)
        {
            var key = word.ToLowerInvariant();
            _wordFrequency[key] = _wordFrequency.GetValueOrDefault(key) + 1;
            _wordDisplay.TryAdd(key, word);
        }

        private void AddCompoundLocked(string compound)
        {
            var key = compound.ToLowerInvariant();
            _compoundFrequency[key] = _compoundFrequency.GetValueOrDefault(key) + 1;
            _compoundDisplay.TryAdd(key, compound);
        }
    }
}
