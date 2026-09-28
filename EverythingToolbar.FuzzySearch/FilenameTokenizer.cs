using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EverythingToolbar.FuzzySearch
{
    /// <summary>
    /// Turns filenames and paths into spelling-vocabulary words. Splits on separators (space,
    /// hyphen, underscore, dots), CamelCase boundaries and letter↔digit boundaries. Words are
    /// lowercase-ready runs of letters or digits; display casing stays with the caller.
    /// </summary>
    public sealed partial class FilenameTokenizer
    {
        [GeneratedRegex(@"(\p{Ll}|\p{Nd})(\p{Lu})")]
        private static partial Regex LowerToUpperRegex();

        [GeneratedRegex(@"(\p{Lu}+)(\p{Lu}\p{Ll})")]
        private static partial Regex AcronymToWordRegex();

        [GeneratedRegex(@"(\p{L})(\p{Nd})")]
        private static partial Regex LetterToDigitRegex();

        [GeneratedRegex(@"(\p{Nd})(\p{L})")]
        private static partial Regex DigitToLetterRegex();

        [GeneratedRegex(@"[^\p{L}\p{Nd}]+")]
        private static partial Regex NonWordRegex();

        /// <summary>Splits text into lowercase-normalizable words; original casing is preserved.</summary>
        public IReadOnlyList<string> TokenizeWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return [];
            }

            var spaced = LowerToUpperRegex().Replace(text, "$1 $2");
            spaced = AcronymToWordRegex().Replace(spaced, "$1 $2");
            spaced = LetterToDigitRegex().Replace(spaced, "$1 $2");
            spaced = DigitToLetterRegex().Replace(spaced, "$1 $2");

            return NonWordRegex().Split(spaced).Where(w => w.Length > 0).ToList();
        }

        /// <summary>
        /// Splits a full path into vocabulary words plus one compound form per segment (segment
        /// minus its last extension, separators and case kept as-is). Whole forms such as
        /// "RepoMaps" or "repo_maps" survive as compounds; the extension itself is only a word.
        /// </summary>
        public (IReadOnlyList<string> Words, IReadOnlyList<string> Compounds) TokenizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return ([], []);
            }

            var words = new List<string>();
            var compounds = new List<string>();

            foreach (var segment in path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
            {
                var compound = segment;
                var dot = segment.LastIndexOf('.');
                if (dot > 0 && dot < segment.Length - 1)
                {
                    compound = segment[..dot];
                }

                if (compound.Length > 0)
                {
                    compounds.Add(compound);
                }

                words.AddRange(TokenizeWords(segment));
            }

            return (words, compounds);
        }
    }
}
